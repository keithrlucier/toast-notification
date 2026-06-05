using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
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
            var ip = NetworkUtils.GetLocalIPv4();
            if (!string.IsNullOrEmpty(ip))
                lines.Add(new OverlayLine("IP Address", ip));
        }
        if (fields.Contains("tenant") && !string.IsNullOrWhiteSpace(tenantName))
            lines.Add(new OverlayLine(null, tenantName!.Trim()));
        if (fields.Contains("customtext") && !string.IsNullOrWhiteSpace(config.CustomText))
            lines.Add(new OverlayLine(null, config.CustomText!.Trim()));

        return lines;
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
    /// Renders the overlay to a 32bpp ARGB bitmap composed in three phases:
    ///
    ///   1. Panel: fully-opaque dark rounded rect (24,24,28 RGB, alpha 255)
    ///      drawn with GDI+ <c>FillPath</c> into a <c>Format32bppArgb</c>
    ///      bitmap. The bitmap is plain (not premultiplied) ARGB — the alpha
    ///      bytes go into the DIB unchanged and UpdateLayeredWindow handles
    ///      premultiplication at composition time.
    ///
    ///   2. Text: labels and values drawn with <c>TextRenderer.DrawText</c>
    ///      (GDI DrawTextEx). GDI honors the user's ClearType settings and
    ///      produces the crisp, sub-pixel-AA "BgInfo look" — softer GDI+
    ///      <c>Graphics.DrawString</c> rendering was what made text look
    ///      grainy in 0.4.15–0.4.17. GDI writes fully-opaque pixels.
    ///
    ///   3. Alpha: <see cref="ApplyAlphaMask"/> walks the bitmap and writes
    ///      the alpha channel from scratch — 0 outside the rounded-rect
    ///      corners; <paramref name="opacityPercent"/>·255/100 for panel-
    ///      colored pixels; 255 for white text pixels; a luminance-driven
    ///      gradient across glyph AA edges. The opacity slider thus dims
    ///      ONLY the card; text glyphs stay fully opaque.
    ///
    /// <see cref="PushLayeredBitmap"/> then calls UpdateLayeredWindow with
    /// <c>BLENDFUNCTION.AlphaFormat = AC_SRC_ALPHA</c> and
    /// <c>SourceConstantAlpha = 255</c>, so per-pixel alpha from the bitmap
    /// is the only thing driving translucency — no constant-alpha multiply.
    ///
    /// History: 0.4.11 used Format32bppPArgb (broke on Win11 26200, zero-alpha
    /// panel); 0.4.15 switched to plain ARGB + GDI+ DrawString (grainy text);
    /// 0.4.18 switched text to GDI ClearType and moved opacity into the alpha
    /// channel via ApplyAlphaMask (current).
    /// </summary>
    private static Bitmap RenderBitmap(List<OverlayLine> lines, float scale, int opacityPercent, out Size size)
    {
        int fontPx  = (int)Math.Round(14 * scale);
        int pad     = (int)Math.Round(12 * scale);
        int lineGap = (int)Math.Round(4  * scale);
        int radius  = (int)Math.Round(6  * scale);

        using var font = new Font("Segoe UI", fontPx, FontStyle.Regular, GraphicsUnit.Pixel);

        // GDI flags: NoPadding for tight measurement; NoPrefix so '&' is literal.
        // Measurement uses the same flags as draw so widths line up exactly.
        const TextFormatFlags TextFlags =
            TextFormatFlags.NoPadding   |
            TextFormatFlags.NoPrefix    |
            TextFormatFlags.SingleLine  |
            TextFormatFlags.Left        |
            TextFormatFlags.Top;

        int contentW = 0, lineH = 0;
        using (var measureBmp = new Bitmap(1, 1))
        using (var scratch = Graphics.FromImage(measureBmp))
        {
            foreach (var ln in lines)
            {
                var text = ln.Label is null ? ln.Value : $"{ln.Label}: {ln.Value}";
                var sz = TextRenderer.MeasureText(scratch, text, font, new Size(int.MaxValue, int.MaxValue), TextFlags);
                contentW = Math.Max(contentW, sz.Width);
                lineH = Math.Max(lineH, sz.Height);
            }
        }

        int boxW = contentW + pad * 2;
        int boxH = lineH * lines.Count + lineGap * (lines.Count - 1) + pad * 2;

        // 32bpp ARGB: alpha channel carries the rounded-rect mask (transparent
        // outside the panel, opaque inside). Inside the panel the pixels are
        // fully opaque RGB — that's what lets GDI ClearType write correct
        // sub-pixel colored glyphs without fighting per-pixel alpha.
        var bmp = new Bitmap(boxW, boxH, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            // Panel: fully opaque dark fill inside the rounded rect. Overall
            // panel translucency is applied later by ApplyAlphaMask writing
            // the per-pixel alpha channel from luminance.
            using (var boxBrush = new SolidBrush(Color.FromArgb(255, 24, 24, 28)))
            using (var boxPath = RoundedRect(new RectangleF(0, 0, boxW, boxH), radius))
                g.FillPath(boxBrush, boxPath);
        }

        // Phase 2 — GDI ClearType text on top of the now-opaque panel.
        // TextRenderer.DrawText goes through DrawTextEx, which honors the
        // user's ClearType settings and renders glyphs at native quality.
        using (var g = Graphics.FromImage(bmp))
        {
            // Pure white for both labels and values — labels were previously
            // dimmed to a cool gray; Keith wants matching contrast across the
            // panel so "Hostname:" reads at the same brightness as the value.
            var textColor = Color.White;

            int y = pad;
            foreach (var ln in lines)
            {
                int x = pad;
                if (ln.Label is null)
                {
                    TextRenderer.DrawText(g, ln.Value, font, new Point(x, y), textColor, TextFlags);
                }
                else
                {
                    var label = $"{ln.Label}: ";
                    var labelSz = TextRenderer.MeasureText(g, label, font, new Size(int.MaxValue, int.MaxValue), TextFlags);
                    TextRenderer.DrawText(g, label,    font, new Point(x,                y), textColor, TextFlags);
                    TextRenderer.DrawText(g, ln.Value, font, new Point(x + labelSz.Width, y), textColor, TextFlags);
                }
                y += lineH + lineGap;
            }
        }

        // Build the alpha channel post-render so the opacity slider dims ONLY
        // the dark panel — text glyphs stay fully opaque. Walk every pixel:
        // outside the rounded-rect → alpha 0; otherwise alpha is interpolated
        // by luminance between panelAlpha (dark panel base) and 255 (white text).
        // AA edges interpolate smoothly between the two.
        ApplyAlphaMask(bmp, radius, opacityPercent);

        size = new Size(boxW, boxH);
        return bmp;
    }

    /// <summary>
    /// Builds the bitmap's alpha channel in a single pass so the layered
    /// window composites correctly:
    ///
    ///   • Outside the rounded-rect corners → alpha = 0 (click-through, shape).
    ///   • Inside the panel → alpha is interpolated by luminance between
    ///     <paramref name="opacityPercent"/>·255/100 (panel base) and 255
    ///     (white text). Dark panel pixels get the user-controlled translucency;
    ///     bright text pixels stay fully opaque; AA-edge pixels interpolate
    ///     smoothly across the gradient so the glyph silhouettes don't band.
    ///
    /// This lets the admin opacity slider dim ONLY the card without bleeding
    /// the dim into the text. GDI ClearType requires an opaque background, so
    /// we render the panel + text fully opaque and reconstruct the alpha
    /// channel here from luminance.
    /// </summary>
    private static unsafe void ApplyAlphaMask(Bitmap bmp, int radius, int opacityPercent)
    {
        int w = bmp.Width, h = bmp.Height;
        byte panelAlpha = (byte)Math.Clamp((int)Math.Round(opacityPercent * 2.55), 0, 255);

        // Panel base color is (24, 24, 28) — Rec.601 luminance ≈ 25. Anything
        // above this is text or AA edge bleeding into the panel.
        const int panelLum = 25;
        const int textLum  = 255;
        const int range    = textLum - panelLum;

        var rect = new Rectangle(0, 0, w, h);
        var data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            byte* scan0 = (byte*)data.Scan0;
            int stride = data.Stride;
            for (int y = 0; y < h; y++)
            {
                byte* row = scan0 + y * stride;
                for (int x = 0; x < w; x++)
                {
                    byte* px = row + x * 4;

                    // Corner check — only the four corner squares can fall
                    // outside the rounded rect.
                    if (radius > 0)
                    {
                        int cx = -1, cy = -1;
                        if      (x <  radius     && y <  radius)     { cx = radius - 1; cy = radius - 1; }
                        else if (x >= w - radius && y <  radius)     { cx = w - radius; cy = radius - 1; }
                        else if (x <  radius     && y >= h - radius) { cx = radius - 1; cy = h - radius; }
                        else if (x >= w - radius && y >= h - radius) { cx = w - radius; cy = h - radius; }

                        if (cx >= 0)
                        {
                            int dx = x - cx, dy = y - cy;
                            // Sub-pixel coverage across the corner curve:
                            // 1.0 inside, 0.0 outside, linearly interpolated
                            // over the ~1px AA band. Preserves the smooth arc
                            // that FillPath drew under SmoothingMode.AntiAlias
                            // instead of stepping it into a pixel staircase.
                            double d = Math.Sqrt(dx * dx + dy * dy);
                            double coverage = radius + 0.5 - d;
                            if (coverage <= 0.0)
                            {
                                px[3] = 0;
                                continue;
                            }
                            if (coverage < 1.0)
                            {
                                // Corner-edge pixel — guaranteed panel-colored
                                // (text starts pad=12*scale in, corner radius
                                // is 6*scale), so skip the luminance interp
                                // and just scale panel alpha by coverage.
                                px[3] = (byte)(panelAlpha * coverage);
                                continue;
                            }
                            // coverage >= 1.0 — fully inside the curve; fall
                            // through to luminance-based alpha below.
                        }
                    }

                    // Luminance-driven alpha (Rec.601 weights).
                    int lum = (px[2] * 299 + px[1] * 587 + px[0] * 114) / 1000;
                    int t   = lum - panelLum;
                    if (t <= 0)        px[3] = panelAlpha;
                    else if (t >= range) px[3] = 255;
                    else                 px[3] = (byte)(panelAlpha + (255 - panelAlpha) * t / range);
                }
            }
        }
        finally { bmp.UnlockBits(data); }
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
