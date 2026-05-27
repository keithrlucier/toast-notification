using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ToastRevival.Agent;

/// <summary>
/// M12 — the BgInfo-style desktop info overlay, done WITHOUT touching the
/// wallpaper. A borderless, layered, click-through window anchored at the bottom
/// of the Z-order: it floats above the wallpaper and below every app and desktop
/// icon. The user can never move, resize, focus, or click it — mouse events fall
/// straight through to the desktop behind it.
///
/// Hosted on the existing tray-icon STA thread (window creation + the message
/// pump it needs both live there); all window operations are marshalled onto that
/// thread via the <c>postToUi</c> delegate handed in by the caller. No second
/// thread, no coordination with the SignalR loop.
///
/// Painted per-pixel via <c>UpdateLayeredWindow</c> from a 32bpp premultiplied
/// bitmap — white drop-shadowed text over a translucent dark rounded box. A 5s
/// timer re-asserts the bottom Z-order to recover from drift and explorer.exe
/// restarts. Primary monitor only for M12.
/// </summary>
internal sealed class DesktopOverlayService : IDisposable
{
    private readonly Action<Action> _postToUi;
    private readonly System.Threading.Timer _reanchorTimer;
    private OverlayForm? _form;
    private bool _disposed;

    public DesktopOverlayService(Action<Action> postToUi)
    {
        _postToUi = postToUi;
        // Re-assert bottom Z-order every 5s. Harmless no-op while hidden.
        _reanchorTimer = new System.Threading.Timer(
            _ => _postToUi(ReanchorZOrder), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// Resolves the configured field values on the calling thread (cheap, thread-
    /// agnostic), then marshals the paint (or hide) onto the UI thread. Safe to
    /// call repeatedly; re-renders if the content changed.
    /// </summary>
    public void Apply(OverlayConfig? config, string? tenantName)
    {
        if (_disposed) return;

        var lines = config is { Enabled: true } ? ResolveLines(config, tenantName) : [];
        if (lines.Count == 0)
        {
            _postToUi(Hide);
            return;
        }

        var position = TenantAppearancePosition.Normalize(config!.Position);
        _postToUi(() => RenderOrHide(lines, position));
    }

    // ── Field resolution (off the UI thread) ────────────────────────────────

    private static List<OverlayLine> ResolveLines(OverlayConfig config, string? tenantName)
    {
        var fields = (config.Fields ?? [])
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f.Trim().ToLowerInvariant())
            .ToHashSet();

        var lines = new List<OverlayLine>(6);

        // Canonical render order, regardless of the order keys arrive in.
        if (fields.Contains("hostname"))
            lines.Add(new OverlayLine("Hostname", Environment.MachineName));
        if (fields.Contains("user"))
            lines.Add(new OverlayLine("Logged-in User", Environment.UserName));
        if (fields.Contains("os"))
            lines.Add(new OverlayLine("OS Version", RuntimeInformation.OSDescription));
        if (fields.Contains("ip"))
        {
            var ip = GetLocalIPv4();
            if (!string.IsNullOrEmpty(ip))
                lines.Add(new OverlayLine("IP Address", ip));
            // No qualifying address → omit the field entirely; never show 169.254.x.x.
        }
        // Tenant Name and Custom Text render as value-only lines (no "Label:" prefix).
        if (fields.Contains("tenant") && !string.IsNullOrWhiteSpace(tenantName))
            lines.Add(new OverlayLine(null, tenantName!.Trim()));
        if (fields.Contains("customtext") && !string.IsNullOrWhiteSpace(config.CustomText))
            lines.Add(new OverlayLine(null, config.CustomText!.Trim()));

        return lines;
    }

    /// <summary>First non-loopback, non-link-local IPv4 on an operational
    /// interface, or null if none qualify.</summary>
    private static string? GetLocalIPv4()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var ip = ua.Address;
                    if (IPAddress.IsLoopback(ip)) continue;
                    var b = ip.GetAddressBytes();
                    if (b[0] == 169 && b[1] == 254) continue; // 169.254/16 link-local (APIPA)
                    return ip.ToString();
                }
            }
        }
        catch (Exception ex)
        {
            DiagLog.Write($"DesktopOverlay.GetLocalIPv4: {ex.GetType().Name}: {ex.Message}");
        }
        return null;
    }

    // ── Rendering (UI/STA thread only) ──────────────────────────────────────

    private void RenderOrHide(List<OverlayLine> lines, string position)
    {
        if (_disposed) return;
        try
        {
            _form ??= new OverlayForm();
            // Realize the handle so DeviceDpi and Show behave; never activates
            // (WS_EX_NOACTIVATE + ShowWithoutActivation).
            if (!_form.Visible) _form.Show();

            var dpiScale = _form.DeviceDpi / 96f;
            using var bmp = RenderBitmap(lines, dpiScale, out var size);

            var wa     = Screen.PrimaryScreen!.WorkingArea; // excludes the taskbar
            var inset  = (int)Math.Round(24 * dpiScale);
            var (x, y) = position switch
            {
                "bottom-left" => (wa.Left + inset, wa.Bottom - size.Height - inset),
                "top-right"   => (wa.Right - size.Width - inset, wa.Top + inset),
                "top-left"    => (wa.Left + inset, wa.Top + inset),
                _             => (wa.Right - size.Width - inset, wa.Bottom - size.Height - inset), // bottom-right
            };

            PushLayeredBitmap(_form.Handle, bmp, new Point(x, y));
            ReanchorZOrder();
            _reanchorTimer.Change(5000, 5000);
        }
        catch (Exception ex)
        {
            DiagLog.Write($"DesktopOverlay.RenderOrHide: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void Hide()
    {
        _reanchorTimer.Change(Timeout.Infinite, Timeout.Infinite);
        if (_form is { IsDisposed: false } && _form.Visible) _form.Hide();
    }

    private void ReanchorZOrder()
    {
        if (_form is not { IsDisposed: false } f || !f.Visible) return;
        // Push to the very bottom of the Z-order (above wallpaper, below apps/icons).
        Native.SetWindowPos(f.Handle, Native.HWND_BOTTOM, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    /// <summary>
    /// Renders the overlay to a premultiplied 32bpp bitmap: a translucent dark
    /// rounded box with white drop-shadowed text. Format32bppPArgb so GDI+ stores
    /// premultiplied alpha — exactly what UpdateLayeredWindow's AC_SRC_ALPHA wants.
    /// </summary>
    private static Bitmap RenderBitmap(List<OverlayLine> lines, float scale, out Size size)
    {
        float fontPx   = 14f * scale;
        float pad      = 12f * scale;
        float lineGap  = 4f  * scale;
        float radius   = 6f  * scale;
        float shadow   = Math.Max(1f, scale); // 1px drop shadow, DPI-scaled

        using var font = new Font("Segoe UI", fontPx, FontStyle.Regular, GraphicsUnit.Pixel);
        var fmt = StringFormat.GenericTypographic;

        // Measure on a scratch graphics.
        float contentW = 0, lineH = 0;
        using (var scratch = Graphics.FromImage(new Bitmap(1, 1)))
        {
            scratch.TextRenderingHint = TextRenderingHint.AntiAlias;
            foreach (var ln in lines)
            {
                var text = ln.Label is null ? ln.Value : $"{ln.Label}: {ln.Value}";
                var sz = scratch.MeasureString(text, font, int.MaxValue, fmt);
                contentW = Math.Max(contentW, sz.Width);
                lineH = Math.Max(lineH, sz.Height);
            }
        }

        int boxW = (int)Math.Ceiling(contentW + pad * 2);
        int boxH = (int)Math.Ceiling(lineH * lines.Count + lineGap * (lines.Count - 1) + pad * 2);
        // Leave room so the drop shadow / AA isn't clipped at the box edges.
        int bmpW = boxW + (int)Math.Ceiling(shadow) + 1;
        int bmpH = boxH + (int)Math.Ceiling(shadow) + 1;

        var bmp = new Bitmap(bmpW, bmpH, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode     = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias; // grayscale AA — ClearType needs an opaque bg
            g.Clear(Color.Transparent);

            using (var boxBrush = new SolidBrush(Color.FromArgb(153, 0, 0, 0))) // rgba(0,0,0,0.6)
            using (var boxPath = RoundedRect(new RectangleF(0, 0, boxW, boxH), radius))
                g.FillPath(boxBrush, boxPath);

            using var shadowBrush = new SolidBrush(Color.FromArgb(204, 0, 0, 0)); // rgba(0,0,0,0.8)
            using var labelBrush  = new SolidBrush(Color.FromArgb(178, 255, 255, 255)); // dim white ~0.7
            using var valueBrush  = new SolidBrush(Color.White);

            float y = pad;
            foreach (var ln in lines)
            {
                float x = pad;
                if (ln.Label is null)
                {
                    g.DrawString(ln.Value, font, shadowBrush, x + shadow, y + shadow, fmt);
                    g.DrawString(ln.Value, font, valueBrush, x, y, fmt);
                }
                else
                {
                    var label = $"{ln.Label}: ";
                    var labelW = g.MeasureString(label, font, int.MaxValue, fmt).Width;
                    // Shadow under the whole line, then label (dim) + value (full white).
                    g.DrawString(label + ln.Value, font, shadowBrush, x + shadow, y + shadow, fmt);
                    g.DrawString(label, font, labelBrush, x, y, fmt);
                    g.DrawString(ln.Value, font, valueBrush, x + labelW, y, fmt);
                }
                y += lineH + lineGap;
            }
        }

        size = new Size(boxW, boxH);
        return bmp;
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        float d = radius * 2;
        var path = new GraphicsPath();
        if (d <= 0) { path.AddRectangle(r); return path; }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>Pushes a premultiplied 32bpp bitmap to the layered window at the
    /// given screen position via UpdateLayeredWindow (per-pixel alpha).</summary>
    private static void PushLayeredBitmap(IntPtr hWnd, Bitmap bmp, Point screenPos)
    {
        IntPtr screenDc = Native.GetDC(IntPtr.Zero);
        IntPtr memDc    = Native.CreateCompatibleDC(screenDc);
        IntPtr hBitmap  = IntPtr.Zero;
        IntPtr oldBmp   = IntPtr.Zero;
        try
        {
            hBitmap = bmp.GetHbitmap(Color.FromArgb(0)); // 32bpp DIB carrying the PArgb bytes
            oldBmp  = Native.SelectObject(memDc, hBitmap);

            var size   = new Native.SIZE { cx = bmp.Width, cy = bmp.Height };
            var src    = new Native.POINT { x = 0, y = 0 };
            var dst    = new Native.POINT { x = screenPos.X, y = screenPos.Y };
            var blend  = new Native.BLENDFUNCTION
            {
                BlendOp             = Native.AC_SRC_OVER,
                BlendFlags          = 0,
                SourceConstantAlpha = 255,
                AlphaFormat         = Native.AC_SRC_ALPHA,
            };

            Native.UpdateLayeredWindow(hWnd, screenDc, ref dst, ref size, memDc, ref src,
                0, ref blend, Native.ULW_ALPHA);
        }
        finally
        {
            Native.ReleaseDC(IntPtr.Zero, screenDc);
            if (hBitmap != IntPtr.Zero)
            {
                Native.SelectObject(memDc, oldBmp);
                Native.DeleteObject(hBitmap);
            }
            Native.DeleteDC(memDc);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _reanchorTimer.Dispose(); } catch { /* best-effort */ }
        // Tear the window down on its own STA thread.
        _postToUi(() =>
        {
            try { _form?.Close(); _form?.Dispose(); } catch { /* best-effort */ }
            _form = null;
        });
    }

    private readonly record struct OverlayLine(string? Label, string Value);

    /// <summary>
    /// Borderless, layered, click-through, no-taskbar, no-activate tool window.
    /// All four extended styles come from the M12 architecture spec.
    /// </summary>
    private sealed class OverlayForm : Form
    {
        public OverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar   = false;
            StartPosition   = FormStartPosition.Manual;
            Text            = string.Empty;
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED   // per-pixel alpha via UpdateLayeredWindow
                            | Native.WS_EX_TRANSPARENT // click-through: mouse falls to desktop behind
                            | Native.WS_EX_NOACTIVATE  // never take focus
                            | Native.WS_EX_TOOLWINDOW; // no Alt+Tab / taskbar presence
                return cp;
            }
        }
    }

    private static class Native
    {
        public const int WS_EX_LAYERED     = 0x00080000;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_NOACTIVATE  = 0x08000000;
        public const int WS_EX_TOOLWINDOW  = 0x00000080;

        public static readonly IntPtr HWND_BOTTOM = new(1);
        public const uint SWP_NOSIZE     = 0x0001;
        public const uint SWP_NOMOVE     = 0x0002;
        public const uint SWP_NOACTIVATE = 0x0010;

        public const byte AC_SRC_OVER  = 0x00;
        public const byte AC_SRC_ALPHA = 0x01;
        public const int  ULW_ALPHA    = 0x00000002;

        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int x; public int y; }
        [StructLayout(LayoutKind.Sequential)] public struct SIZE  { public int cx; public int cy; }
        [StructLayout(LayoutKind.Sequential)] public struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UpdateLayeredWindow(
            IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
            IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(
            IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateCompatibleDC(IntPtr hDC);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteDC(IntPtr hDC);

        [DllImport("gdi32.dll")]
        public static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteObject(IntPtr hObject);
    }
}

/// <summary>Agent-side position normalizer — mirrors the server's
/// TenantAppearance.NormalizePosition so the overlay anchors correctly even on a
/// null/garbage value.</summary>
internal static class TenantAppearancePosition
{
    private static readonly HashSet<string> Valid =
        ["bottom-right", "bottom-left", "top-right", "top-left"];

    public static string Normalize(string? p)
    {
        var v = p?.Trim().ToLowerInvariant();
        return !string.IsNullOrEmpty(v) && Valid.Contains(v) ? v : "bottom-right";
    }
}
