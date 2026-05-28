using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace ToastRevival.Agent;

/// <summary>
/// M12.B — the BgInfo-style desktop info overlay. Pinned to the desktop
/// (above wallpaper + icons, below every app window) by parenting our window
/// to explorer's WorkerW, exactly like Wallpaper Engine and Lively Wallpaper
/// do. The user can never move, resize, focus, or click it: mouse events fall
/// straight through.
///
/// Why WorkerW: the original M12 (0.4.9–0.4.11) used a top-level layered
/// window with z-order tricks (HWND_BOTTOM, then HWND_NOTOPMOST). Both lost
/// to the desktop's SysListView32 icon container — on a populated desktop
/// the icons painted over us, and on a bare desktop the per-pixel alpha
/// composited against nothing produced washed-out text without the dark
/// panel showing. The fix is to become a CHILD of explorer's secondary
/// WorkerW (the one Progman spawns when it gets the magic message 0x052C),
/// which sits between the wallpaper bitmap and the icon ListView. As a
/// child window we paint via WM_PAINT against an opaque dark panel — no
/// UpdateLayeredWindow, no per-pixel alpha compositing edge cases.
///
/// Click-through is preserved via WS_EX_TRANSPARENT. Re-anchor timer fires
/// every 5s to recover from explorer.exe restarts (which destroy the
/// WorkerW we parented to). All window operations are marshalled onto the
/// tray's STA thread via the postToUi delegate handed in by the caller.
/// Primary monitor only for M12.B.
/// </summary>
internal sealed class DesktopOverlayService : IDisposable
{
    private readonly Action<Action> _postToUi;
    private readonly System.Threading.Timer _reanchorTimer;
    private OverlayForm? _form;
    private List<OverlayLine>? _lastLines;
    private string _lastPosition = "bottom-right";
    // Cached parent HWND from the last successful EnsureParentedToWorkerW. Used
    // by ReanchorParent to detect explorer.exe restart (parent handle no longer
    // a window). GetParent() can't be used as the liveness check because it
    // returns 0 for desktop-owned popups even when SetParent succeeded — that
    // was the false-positive re-anchor loop in 0.4.12.
    private IntPtr _parentHwnd;
    private bool _disposed;

    public DesktopOverlayService(Action<Action> postToUi)
    {
        _postToUi = postToUi;
        // Explorer.exe restarts (shell crash, Settings → Personalization changes)
        // destroy our WorkerW parent. Every 5s, verify the parent is still alive
        // and re-attach if not. Harmless no-op while hidden.
        _reanchorTimer = new System.Threading.Timer(
            _ => _postToUi(ReanchorParent), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// Resolves the configured field values on the calling thread (cheap, thread-
    /// agnostic), then marshals the paint (or hide) onto the UI thread. Safe to
    /// call repeatedly; re-renders if the content changed.
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
        DiagLog.Write($"DesktopOverlay.Apply: lines={lines.Count}; position={position} — posting RenderOrHide.");
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
            _lastLines    = lines;
            _lastPosition = position;

            var firstRender = _form is null;
            _form ??= new OverlayForm(lines);
            _form.SetLines(lines);

            // Realize the handle so DeviceDpi is correct.
            _ = _form.Handle;
            var dpiScale = _form.DeviceDpi / 96f;
            var size     = _form.MeasureContent(dpiScale);

            var wa     = Screen.PrimaryScreen!.WorkingArea;
            var inset  = (int)Math.Round(24 * dpiScale);
            var (x, y) = position switch
            {
                "bottom-left" => (wa.Left + inset, wa.Bottom - size.Height - inset),
                "top-right"   => (wa.Right - size.Width - inset, wa.Top + inset),
                "top-left"    => (wa.Left + inset, wa.Top + inset),
                _             => (wa.Right - size.Width - inset, wa.Bottom - size.Height - inset),
            };

            // Parent FIRST. WinForms borderless forms are WS_POPUP by default; on
            // a popup, SetParent gives "owned popup" semantics — the window
            // belongs to the parent for Z-order but doesn't paint inside the
            // parent's client area. We need real child semantics: flip
            // WS_POPUP → WS_CHILD AFTER SetParent, then SetWindowPos to commit
            // the style change and position the window in parent-client
            // coordinates. Wallpaper Engine + Lively Wallpaper both do this
            // exact sequence — getting the order wrong leaves an invisible
            // window owned by Progman, which is exactly what 0.4.12 shipped.
            EnsureParentedToWorkerW(_form.Handle);

            // WorkerW / Progman client origin is at (0,0) of the virtual screen
            // on every Windows 11 build we've seen, so the screen coords
            // computed above become parent-client coords 1:1. If a future build
            // changes that, the regression will be "overlay paints in the
            // wrong corner" (visible) rather than "overlay invisible" (silent).
            var style = Native.GetWindowLong(_form.Handle, Native.GWL_STYLE);
            var newStyle = (style & ~Native.WS_POPUP) | Native.WS_CHILD;
            if (newStyle != style)
            {
                Native.SetWindowLong(_form.Handle, Native.GWL_STYLE, newStyle);
            }

            Native.SetWindowPos(_form.Handle, IntPtr.Zero, x, y, size.Width, size.Height,
                Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW
                | Native.SWP_FRAMECHANGED); // FRAMECHANGED tells Windows to apply the new style

            if (!_form.Visible) _form.Show();
            _form.Invalidate();

            _reanchorTimer.Change(5000, 5000);

            DiagLog.Write($"DesktopOverlay.RenderOrHide: painted {size.Width}x{size.Height} at ({x},{y}); dpi={dpiScale:F2}; firstRender={firstRender}; hwnd=0x{_form.Handle.ToInt64():X}; parent=0x{Native.GetParent(_form.Handle).ToInt64():X}");
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
    /// Verifies our window is still parented to a live parent; re-parents if not.
    /// Fires every 5s. We track the parent HWND we set in _parentHwnd because
    /// GetParent() returns 0 for desktop-owned popups even when SetParent
    /// succeeded — that misfire produced the 5s re-anchor spam loop in 0.4.12.
    /// IsWindow on the cached handle is the real liveness check: it goes false
    /// only when explorer.exe restarts and tears down WorkerW/Progman.
    /// </summary>
    private void ReanchorParent()
    {
        if (_form is not { IsDisposed: false } f || !f.Visible) return;
        try
        {
            if (_parentHwnd != IntPtr.Zero && Native.IsWindow(_parentHwnd))
            {
                // Parent still alive. Nothing to do.
                return;
            }

            DiagLog.Write($"DesktopOverlay.ReanchorParent: parent=0x{_parentHwnd.ToInt64():X} (gone) — re-attaching.");
            EnsureParentedToWorkerW(f.Handle);

            // After re-parenting we must re-apply the WS_CHILD style and reposition.
            // SetParent on a freshly-orphaned window often leaves it at (0,0) with
            // the wrong style word — same setup logic as the initial render path.
            if (_lastLines is not null)
            {
                var dpi  = f.DeviceDpi / 96f;
                var size = f.MeasureContent(dpi);
                var wa   = Screen.PrimaryScreen!.WorkingArea;
                var inset = (int)Math.Round(24 * dpi);
                var (x, y) = _lastPosition switch
                {
                    "bottom-left" => (wa.Left + inset, wa.Bottom - size.Height - inset),
                    "top-right"   => (wa.Right - size.Width - inset, wa.Top + inset),
                    "top-left"    => (wa.Left + inset, wa.Top + inset),
                    _             => (wa.Right - size.Width - inset, wa.Bottom - size.Height - inset),
                };
                var style = Native.GetWindowLong(f.Handle, Native.GWL_STYLE);
                var newStyle = (style & ~Native.WS_POPUP) | Native.WS_CHILD;
                if (newStyle != style) Native.SetWindowLong(f.Handle, Native.GWL_STYLE, newStyle);
                Native.SetWindowPos(f.Handle, IntPtr.Zero, x, y, size.Width, size.Height,
                    Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW
                    | Native.SWP_FRAMECHANGED);
                f.Invalidate();
            }
        }
        catch (Exception ex)
        {
            DiagLog.Write($"DesktopOverlay.ReanchorParent: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ── WorkerW plumbing ────────────────────────────────────────────────────

    /// <summary>
    /// Finds the desktop's secondary WorkerW (the one between the wallpaper bitmap
    /// and the icon ListView) and re-parents our window to it. If anything in this
    /// path fails — shell replacement, locked-down system, message timeout — the
    /// window stays top-level and we paint click-through on top of the desktop.
    /// Logs each step so support can triage shell-variance issues from agent.log.
    /// Updates <see cref="_parentHwnd"/> on success so ReanchorParent can check
    /// liveness without relying on GetParent (which lies for desktop-owned popups).
    /// </summary>
    private void EnsureParentedToWorkerW(IntPtr hWnd)
    {
        var workerW = FindWorkerW();
        if (workerW == IntPtr.Zero)
        {
            DiagLog.Write("DesktopOverlay.EnsureParentedToWorkerW: WorkerW not found — staying top-level (overlay will sit above wallpaper but may be covered by icons/apps).");
            _parentHwnd = IntPtr.Zero;
            return;
        }
        var prev = Native.SetParent(hWnd, workerW);
        if (prev == IntPtr.Zero)
        {
            var err = Marshal.GetLastWin32Error();
            DiagLog.Write($"DesktopOverlay.EnsureParentedToWorkerW: SetParent failed errno={err}");
            return;
        }
        _parentHwnd = workerW;
        DiagLog.Write($"DesktopOverlay.EnsureParentedToWorkerW: parented hwnd=0x{hWnd.ToInt64():X} to WorkerW=0x{workerW.ToInt64():X} (was 0x{prev.ToInt64():X})");
    }

    /// <summary>
    /// Sends Progman the magic 0x052C message to spawn (if not already present)
    /// the secondary WorkerW window between the wallpaper renderer and the icon
    /// ListView. Then walks top-level windows looking for the WorkerW whose
    /// sibling is NOT the SHELLDLL_DefView host — that's the one we want.
    /// </summary>
    private static IntPtr FindWorkerW()
    {
        var progman = Native.FindWindow("Progman", null);
        if (progman == IntPtr.Zero)
        {
            DiagLog.Write("DesktopOverlay.FindWorkerW: Progman not found.");
            return IntPtr.Zero;
        }

        // Spawn-or-no-op: tell Progman to ensure WorkerW exists. The exact wParam
        // values 0xD/0x1 are the documented Wallpaper Engine recipe; SendMessageTimeout
        // returns quickly because Progman handles this synchronously.
        Native.SendMessageTimeout(progman, 0x052C, new IntPtr(0xD), new IntPtr(0x1),
            Native.SMTO_NORMAL, 1000, out _);

        IntPtr foundWorkerW = IntPtr.Zero;
        Native.EnumWindows((tophandle, _) =>
        {
            // We're looking for a WorkerW that has SHELLDLL_DefView as a child.
            // That WorkerW is the one BEHIND the wallpaper bitmap — wrong target.
            // Then take its NEXT sibling WorkerW — that's our destination.
            var defView = Native.FindWindowEx(tophandle, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView != IntPtr.Zero)
            {
                var sibling = Native.FindWindowEx(IntPtr.Zero, tophandle, "WorkerW", null);
                if (sibling != IntPtr.Zero)
                {
                    foundWorkerW = sibling;
                    return false; // stop enumeration
                }
            }
            return true;
        }, IntPtr.Zero);

        if (foundWorkerW == IntPtr.Zero)
        {
            // Some Windows 11 builds host the icon ListView directly under Progman
            // instead of spawning a secondary WorkerW. In that case parenting
            // directly to Progman gives us the same Z-layer (above wallpaper,
            // below the icon view that Progman renders on top). Fall back.
            var progmanDefView = Native.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (progmanDefView != IntPtr.Zero)
            {
                DiagLog.Write("DesktopOverlay.FindWorkerW: no secondary WorkerW; falling back to Progman as parent.");
                return progman;
            }
        }

        return foundWorkerW;
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

    // ── The window ──────────────────────────────────────────────────────────

    /// <summary>
    /// Borderless, click-through, no-taskbar, no-activate child window. Painted
    /// opaquely via WM_PAINT against a solid dark rounded panel (no per-pixel
    /// alpha — that pipeline doesn't work for child windows of WorkerW). The
    /// rounded corners use a window region so the corners of the rectangle are
    /// genuinely cut out, not painted with alpha. Click-through is preserved via
    /// WS_EX_TRANSPARENT on the ext-style.
    /// </summary>
    private sealed class OverlayForm : Form
    {
        // Visual constants — kept in lock-step with the prior layered-window
        // bitmap so the spec design stays intact: rgba(0,0,0,0.85) panel, 6px
        // radius, 12px padding, 4px line gap, 14px Segoe UI, white values with
        // dim white labels.
        private static readonly Color PanelColor = Color.FromArgb(217, 12, 14, 22); // ~rgba(12,14,22,0.85)
        private const int   PanelRadius = 6;
        private const int   PadPx       = 12;
        private const int   LineGapPx   = 4;
        private const float FontSizePx  = 14f;

        private List<OverlayLine> _lines;

        public OverlayForm(List<OverlayLine> initialLines)
        {
            _lines          = initialLines;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar   = false;
            StartPosition   = FormStartPosition.Manual;
            Text            = string.Empty;
            DoubleBuffered  = true;
            BackColor       = Color.FromArgb(PanelColor.R, PanelColor.G, PanelColor.B);

            // The panel is opaque; we render text in a Graphics created from
            // the WM_PAINT DC. ClearType is fine because the background is
            // solid.
            SetStyle(ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.UserPaint, true);
        }

        public void SetLines(List<OverlayLine> lines) => _lines = lines;

        public Size MeasureContent(float scale)
        {
            float fontPx  = FontSizePx * scale;
            float pad     = PadPx      * scale;
            float lineGap = LineGapPx  * scale;

            using var font = new Font("Segoe UI", fontPx, FontStyle.Regular, GraphicsUnit.Pixel);
            using var scratch = Graphics.FromImage(new Bitmap(1, 1));
            scratch.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            float contentW = 0, lineH = 0;
            foreach (var ln in _lines)
            {
                var text = ln.Label is null ? ln.Value : $"{ln.Label}: {ln.Value}";
                var sz = scratch.MeasureString(text, font, int.MaxValue, StringFormat.GenericTypographic);
                contentW = Math.Max(contentW, sz.Width);
                lineH = Math.Max(lineH, sz.Height);
            }

            int boxW = (int)Math.Ceiling(contentW + pad * 2);
            int boxH = (int)Math.Ceiling(lineH * _lines.Count + lineGap * (_lines.Count - 1) + pad * 2);
            return new Size(boxW, boxH);
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TRANSPARENT // click-through: mouse falls to desktop behind
                            | Native.WS_EX_NOACTIVATE  // never take focus
                            | Native.WS_EX_TOOLWINDOW; // no Alt+Tab / taskbar presence
                // Note: WS_EX_LAYERED is intentionally NOT set. Child windows
                // of WorkerW cannot use UpdateLayeredWindow's per-pixel alpha;
                // the panel is rendered opaquely instead and the rounded corners
                // are cut out via SetWindowRgn (see OnResize).
                return cp;
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ApplyRoundedRegion();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyRoundedRegion();
        }

        private void ApplyRoundedRegion()
        {
            if (!IsHandleCreated || Width <= 0 || Height <= 0) return;
            // CreateRoundRectRgn rounds the literal window rect. Replace existing
            // region (SetWindowRgn deletes the previous one when bRedraw = true).
            var rgn = Native.CreateRoundRectRgn(0, 0, Width + 1, Height + 1,
                PanelRadius * 2, PanelRadius * 2);
            Native.SetWindowRgn(Handle, rgn, true);
            // Don't DeleteObject — Windows owns the region after SetWindowRgn.
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode     = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(Color.FromArgb(PanelColor.R, PanelColor.G, PanelColor.B));

            float scale   = DeviceDpi / 96f;
            float fontPx  = FontSizePx * scale;
            float pad     = PadPx      * scale;
            float lineGap = LineGapPx  * scale;

            using var font        = new Font("Segoe UI", fontPx, FontStyle.Regular, GraphicsUnit.Pixel);
            using var labelBrush  = new SolidBrush(Color.FromArgb(178, 255, 255, 255));
            using var valueBrush  = new SolidBrush(Color.White);
            var fmt = StringFormat.GenericTypographic;

            float y = pad;
            float lineH = 0;
            foreach (var ln in _lines)
            {
                var text = ln.Label is null ? ln.Value : $"{ln.Label}: {ln.Value}";
                var sz = g.MeasureString(text, font, int.MaxValue, fmt);
                lineH = Math.Max(lineH, sz.Height);
            }

            foreach (var ln in _lines)
            {
                float x = pad;
                if (ln.Label is null)
                {
                    g.DrawString(ln.Value, font, valueBrush, x, y, fmt);
                }
                else
                {
                    var label = $"{ln.Label}: ";
                    var labelW = g.MeasureString(label, font, int.MaxValue, fmt).Width;
                    g.DrawString(label, font, labelBrush, x, y, fmt);
                    g.DrawString(ln.Value, font, valueBrush, x + labelW, y, fmt);
                }
                y += lineH + lineGap;
            }
        }
    }

    private static class Native
    {
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_NOACTIVATE  = 0x08000000;
        public const int WS_EX_TOOLWINDOW  = 0x00000080;

        // WS_POPUP and WS_CHILD are mutually exclusive in the window style word.
        // Borderless WinForms forms are WS_POPUP by default; SetParent on a
        // popup yields owned-popup semantics, not real child semantics — the
        // window doesn't paint inside the parent's client area. Flipping to
        // WS_CHILD after SetParent is required to make the parent relationship
        // actually take effect for compositing.
        public const int  WS_POPUP = unchecked((int)0x80000000);
        public const int  WS_CHILD = 0x40000000;
        public const int  GWL_STYLE = -16;

        public const uint SWP_NOSIZE       = 0x0001;
        public const uint SWP_NOMOVE       = 0x0002;
        public const uint SWP_NOZORDER     = 0x0004;
        public const uint SWP_NOACTIVATE   = 0x0010;
        public const uint SWP_FRAMECHANGED = 0x0020; // applies pending GWL_STYLE change
        public const uint SWP_SHOWWINDOW   = 0x0040;

        public const uint SMTO_NORMAL = 0x0000;

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(
            IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetWindowLongW")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLongW")]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr FindWindowEx(IntPtr hWndParent, IntPtr hWndChildAfter,
            string? lpszClass, string? lpszWindow);

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam,
            IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect,
            int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn,
            [MarshalAs(UnmanagedType.Bool)] bool bRedraw);
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
