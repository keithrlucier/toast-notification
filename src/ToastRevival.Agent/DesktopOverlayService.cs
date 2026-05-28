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
/// M12 desktop info overlay (0.4.14).
///
/// History — we tried two architectures before landing here:
///
///   1. 0.4.9–0.4.11 — Top-level layered window with HWND_BOTTOM / NOTOPMOST.
///      Painted correctly via UpdateLayeredWindow but Z-order was always wrong:
///      either parked below the desktop icon ListView (invisible on populated
///      desktops) or composited at NOTOPMOST with desktop icons overlapping
///      the text.
///
///   2. 0.4.12–0.4.13 — WorkerW / Progman parenting (Wallpaper Engine recipe).
///      Window successfully became a child of Progman, but on Windows 11 25H2
///      (build 26200) the magic message 0x052C does NOT spawn the sibling
///      WorkerW that DWM recognizes as a desktop overlay target. Falling back
///      to Progman put the window in DWM dead-zone — the window exists in the
///      kernel at the right rect with IsWindowVisible=true, but its surface is
///      not composited to the desktop output. Invisible.
///
/// Current approach (0.4.14): top-level WS_EX_LAYERED window with
/// per-pixel-alpha rendering (UpdateLayeredWindow) at HWND_NOTOPMOST z-order.
/// Click-through, no-activate, no-Alt-Tab. Sits above the wallpaper. May be
/// overlapped by desktop icons (known limitation — documented in FIX-LIST as
/// FIX-OVERLAY-005, deferred until/unless we figure out how to make DWM
/// composite a Progman child on Win11 26200+).
///
/// Hosted on the tray's STA thread via the postToUi delegate — no second
/// thread. Re-anchor timer (5s) reasserts NOTOPMOST z-order to recover from
/// other windows promoting themselves above us. Primary monitor only.
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
        _reanchorTimer = new System.Threading.Timer(
            _ => _postToUi(ReanchorZOrder), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// Resolves the configured field values on the calling thread (cheap, thread-
    /// agnostic), then marshals the paint (or hide) onto the UI thread.
    /// </summary>
    public void Apply(OverlayConfig? config, string? tenantName)
    {
        if (_disposed) return;

        var fieldsCount = config?.Fields?.Length ?? 0;
        DiagLog.Write($"DesktopOverlay.Apply: enabled={config?.Enabled.ToString() ?? "(null-config)"}; fieldsCount={fieldsCount}; position={config?.Position ?? "(null)"}; tenantName={(tenantName ?? "(null)")}; customText={(string.IsNullOrEmpty(config?.CustomText) ? "(none)" : "(set)")}");

        var lines = config is { Enabled: true } ? ResolveLines(config, tenantName) : [];
        if (lines.Count == 0)
        {
            DiagLog.Write("DesktopOverlay.Apply: lines=0 — calling Hide.");
            _postToUi(Hide);
            return;
        }

        var position = TenantAppearancePosition.Normalize(config!.Position);
        var opacity  = NormalizeOpacity(config.OpacityPercent);
        DiagLog.Write($"DesktopOverlay.Apply: lines={lines.Count}; position={position}; opacity={opacity}% — posting RenderOrHide.");
        _postToUi(() => RenderOrHide(lines, position, opacity));
    }

    /// <summary>
    /// Clamps the configured opacity to the supported range. The server validates
    /// to [10, 100] in 5% increments; this is a defensive normalizer for older
    /// configs or hand-edited DB rows.
    /// </summary>
    private static int NormalizeOpacity(int? raw)
    {
        if (raw is null) return 85; // sensible default if a pre-opacity server omits the field
        return Math.Clamp(raw.Value, 10, 100);
    }

    // ── Field resolution (off the UI thread) ────────────────────────────────

    private static List<OverlayLine> ResolveLines(OverlayConfig config, string? tenantName)
    {
        var fields = (config.Fields ?? [])
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f.Trim().ToLowerInvariant())
            .ToHashSet();

        var lines = new List<OverlayLine>(6);

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
        }
        if (fields.Contains("tenant") && !string.IsNullOrWhiteSpace(tenantName))
            lines.Add(new OverlayLine(null, tenantName!.Trim()));
        if (fields.Contains("customtext") && !string.IsNullOrWhiteSpace(config.CustomText))
            lines.Add(new OverlayLine(null, config.CustomText!.Trim()));

        return lines;
    }

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
                    if (b[0] == 169 && b[1] == 254) continue;
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

    private void RenderOrHide(List<OverlayLine> lines, string position, int opacityPercent)
    {
        if (_disposed) return;
        try
        {
            var firstRender = _form is null;
            _form ??= new OverlayForm();
            if (!_form.Visible) _form.Show();

            var dpiScale = _form.DeviceDpi / 96f;
            using var bmp = RenderBitmap(lines, dpiScale, opacityPercent, out var size);

            var wa     = Screen.PrimaryScreen!.WorkingArea;
            var inset  = (int)Math.Round(24 * dpiScale);
            var (x, y) = position switch
            {
                "bottom-left" => (wa.Left + inset, wa.Bottom - size.Height - inset),
                "top-right"   => (wa.Right - size.Width - inset, wa.Top + inset),
                "top-left"    => (wa.Left + inset, wa.Top + inset),
                _             => (wa.Right - size.Width - inset, wa.Bottom - size.Height - inset),
            };

            PushLayeredBitmap(_form.Handle, bmp, new Point(x, y));
            ReanchorZOrder();
            _reanchorTimer.Change(5000, 5000);

            DiagLog.Write($"DesktopOverlay.RenderOrHide: painted {size.Width}x{size.Height} at ({x},{y}); dpi={dpiScale:F2}; firstRender={firstRender}; hwnd=0x{_form.Handle.ToInt64():X}");
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

    /// <summary>
    /// Reasserts NOTOPMOST z-order on the overlay. NOTOPMOST means: not in the
    /// always-on-top band, but at the highest z-level below the topmost band.
    /// Other ordinary windows that the user opens after us will get focus and
    /// composite above us until they're closed/minimized — that's the
    /// "above wallpaper, below apps" behavior the user signed off on.
    /// Re-issued every 5s in case Windows promoted something else above us.
    /// </summary>
    private void ReanchorZOrder()
    {
        if (_form is not { IsDisposed: false } f || !f.Visible) return;
        Native.SetWindowPos(f.Handle, Native.HWND_NOTOPMOST, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    /// <summary>
    /// Renders the overlay to a premultiplied 32bpp bitmap: a translucent dark
    /// rounded box with white drop-shadowed text. Format32bppPArgb so GDI+
    /// stores premultiplied alpha — exactly what UpdateLayeredWindow's
    /// AC_SRC_ALPHA wants. This is the same renderer that visibly worked in
    /// 0.4.11 on Keith's box (verified screenshots).
    /// </summary>
    /// <summary>
    /// Renders the overlay to a 32bpp ARGB bitmap. Three bugs fixed in 0.4.15
    /// vs 0.4.14:
    ///
    ///   (a) Panel invisible: 0.4.14 drew into Format32bppPArgb and relied on
    ///       GDI+ to premultiply the panel color, which on Win11 26200 produced
    ///       a zero-alpha panel — the dark box never appeared on screen. Fix:
    ///       use plain Format32bppArgb so the alpha bytes go straight into the
    ///       DIB unchanged, then UpdateLayeredWindow does the premultiplication
    ///       at composition time (its documented AC_SRC_ALPHA behavior).
    ///
    ///   (b) "Hostname:WIN-TEST-001" glued: MeasureString with GenericTypographic
    ///       strips trailing whitespace from the measured width. The label
    ///       "Hostname: " (with trailing space) measured as if it were just
    ///       "Hostname:", so the value got drawn flush against the colon. Fix:
    ///       use GenericDefault for the LABEL measurement so the trailing
    ///       space is included in labelW. Keep GenericTypographic for the
    ///       overall content-width measurement (where we WANT tighter bounds).
    ///
    ///   (c) Text reads dim: 0.4.14's drop-shadow draw bled into the AA edges
    ///       of the value glyphs, darkening them. Removed the shadow entirely;
    ///       the now-correctly-rendered dark panel provides all the contrast
    ///       the text needs. Also switched to AntiAliasGridFit for crisper
    ///       glyph edges on the dark panel.
    ///
    /// Panel opacity is now data-driven (admin-controlled, 10–100%). Default
    /// when the field is absent: 85%.
    /// </summary>
    private static Bitmap RenderBitmap(List<OverlayLine> lines, float scale, int opacityPercent, out Size size)
    {
        float fontPx  = 14f * scale;
        float pad     = 12f * scale;
        float lineGap = 4f  * scale;
        float radius  = 6f  * scale;

        using var font = new Font("Segoe UI", fontPx, FontStyle.Regular, GraphicsUnit.Pixel);
        var measureFmt = StringFormat.GenericDefault;       // preserves trailing space in label measurement
        var drawFmt    = StringFormat.GenericTypographic;   // tighter glyph layout for the actual paint

        float contentW = 0, lineH = 0;
        using (var measureBmp = new Bitmap(1, 1))
        using (var scratch = Graphics.FromImage(measureBmp))
        {
            scratch.TextRenderingHint = TextRenderingHint.AntiAlias;
            foreach (var ln in lines)
            {
                var text = ln.Label is null ? ln.Value : $"{ln.Label}: {ln.Value}";
                var sz = scratch.MeasureString(text, font, int.MaxValue, measureFmt);
                contentW = Math.Max(contentW, sz.Width);
                lineH = Math.Max(lineH, sz.Height);
            }
        }

        int boxW = (int)Math.Ceiling(contentW + pad * 2);
        int boxH = (int)Math.Ceiling(lineH * lines.Count + lineGap * (lines.Count - 1) + pad * 2);
        int bmpW = boxW;
        int bmpH = boxH;

        // Format32bppArgb — plain (non-premultiplied) ARGB. UpdateLayeredWindow
        // with AlphaFormat = AC_SRC_ALPHA accepts both formats; using the plain
        // format avoids the GDI+ → premultiplication path that was eating the
        // panel alpha on Win11 26200 in 0.4.14.
        var bmp = new Bitmap(bmpW, bmpH, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode     = SmoothingMode.AntiAlias;
            // AntiAlias (not AntiAliasGridFit): GridFit snaps glyph metrics to
            // the pixel grid assuming an opaque background, which on a layered
            // window with per-pixel alpha bleeds the AA edges into the panel
            // and reads as faintly blocky. Plain AntiAlias on the transparent
            // surface composites cleanly through UpdateLayeredWindow.
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.Clear(Color.Transparent);

            int panelAlpha = (int)Math.Round(opacityPercent * 2.55);  // 0..255
            using (var boxBrush = new SolidBrush(Color.FromArgb(Math.Clamp(panelAlpha, 0, 255), 0, 0, 0)))
            using (var boxPath = RoundedRect(new RectangleF(0, 0, boxW, boxH), radius))
                g.FillPath(boxBrush, boxPath);

            using var labelBrush = new SolidBrush(Color.FromArgb(204, 255, 255, 255));  // ~80% white
            using var valueBrush = new SolidBrush(Color.White);                          // 100% white

            float y = pad;
            foreach (var ln in lines)
            {
                float x = pad;
                if (ln.Label is null)
                {
                    g.DrawString(ln.Value, font, valueBrush, x, y, drawFmt);
                }
                else
                {
                    var label = $"{ln.Label}: ";
                    // Measure with GenericDefault so the trailing space is
                    // included in labelW — that's the gap between label and
                    // value. GenericTypographic on the measurement was the
                    // 0.4.14 "Hostname:WIN-TEST-001" bug.
                    var labelW = g.MeasureString(label, font, int.MaxValue, measureFmt).Width;
                    g.DrawString(label, font, labelBrush, x, y, drawFmt);
                    g.DrawString(ln.Value, font, valueBrush, x + labelW, y, drawFmt);
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

    private static void PushLayeredBitmap(IntPtr hWnd, Bitmap bmp, Point screenPos)
    {
        IntPtr screenDc = Native.GetDC(IntPtr.Zero);
        IntPtr memDc    = Native.CreateCompatibleDC(screenDc);
        IntPtr hBitmap  = IntPtr.Zero;
        IntPtr oldBmp   = IntPtr.Zero;
        try
        {
            hBitmap = bmp.GetHbitmap(Color.FromArgb(0));
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
        _postToUi(() =>
        {
            try { _form?.Close(); _form?.Dispose(); } catch { /* best-effort */ }
            _form = null;
        });
    }

    private readonly record struct OverlayLine(string? Label, string Value);

    /// <summary>
    /// Borderless, layered, click-through, no-taskbar, no-activate tool window.
    /// Top-level (no SetParent) — keeps UpdateLayeredWindow's per-pixel alpha
    /// pipeline working, which is what makes the overlay actually composite
    /// to the desktop on Windows 11 26200+.
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
                cp.ExStyle |= Native.WS_EX_LAYERED
                            | Native.WS_EX_TRANSPARENT
                            | Native.WS_EX_NOACTIVATE
                            | Native.WS_EX_TOOLWINDOW;
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

        public static readonly IntPtr HWND_NOTOPMOST = new(-2);
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
