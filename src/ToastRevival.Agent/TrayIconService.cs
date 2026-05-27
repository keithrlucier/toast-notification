using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace ToastRevival.Agent;

/// <summary>
/// System tray icon. Lives on a dedicated WinForms STA background thread.
/// Reflects AgentHubClient connection state via UpdateState(). Context menu
/// provides dashboard shortcut, test notification, log viewer, manual reconnect,
/// and quit.
///
/// Visual state palette:
///   Connected       #F59E0B amber, static
///   Reconnecting    #F59E0B amber, 700ms pulse between 100% and 55% brightness
///   Disconnected    #F59E0B amber, static
///   Error           #DC2626 red, static
///   Connecting      #7A7A92 dim, static
///
/// Production bell glyphs are rendered via GraphicsPath at icon construction
/// time, tinted by state color. Single shape across all five states — only the
/// fill color tells the user which state. Renders crisply at 16×16 native and
/// at high-DPI resampling.
///
/// Note: HICONs created via Bitmap.GetHicon() are not explicitly freed.
/// Acceptable for process-lifetime tray icons — handles are released on process exit.
/// </summary>
internal sealed class TrayIconService : IDisposable
{
    public event Action? QuitRequested;
    public event Action? ReconnectRequested;
    public event Action? SendTestRequested;
    public event Action? ApplyUpdateRequested;

    private readonly string _serverUrl;
    private AgentConnectionState _currentState = AgentConnectionState.Connecting;
    private string? _stateDetail;
    private bool _disposed;

    private readonly Thread _uiThread;
    private readonly ManualResetEventSlim _uiReady = new();
    private SynchronizationContext? _uiContext;
    private NotifyIcon? _notifyIcon;

    private Icon? _connectingIcon;
    private Icon? _connectedIcon;
    private Icon? _reconnectingIcon;
    private Icon? _reconnectingDimIcon;
    private Icon? _disconnectedIcon;
    private Icon? _errorIcon;

    private System.Threading.Timer? _animTimer;
    private bool _animPhase;

    private ToolStripMenuItem? _reconnectItem;
    private ToolStripMenuItem? _updateItem;

    public TrayIconService(string serverUrl)
    {
        _serverUrl = serverUrl;
        _uiThread = new Thread(RunMessageLoop) { IsBackground = true, Name = "TrayIcon-STA" };
        _uiThread.SetApartmentState(ApartmentState.STA);
        _uiThread.Start();
        _uiReady.Wait(TimeSpan.FromSeconds(3));
    }

    public void UpdateState(AgentConnectionState state, string? detail = null)
    {
        _currentState = state;
        _stateDetail = detail;
        _uiContext?.Post(_ => ApplyState(), null);
    }

    /// <summary>
    /// Marshals <paramref name="action"/> onto this service's STA thread — the
    /// thread that owns the WinForms message pump. Used by the M12 desktop
    /// overlay to create and paint its layered window on the same thread,
    /// avoiding a second STA thread. Exceptions are logged, never thrown back
    /// onto the pump. No-op after dispose.
    /// </summary>
    public void Post(Action action)
    {
        if (_disposed) return;
        _uiContext?.Post(_ =>
        {
            try { action(); }
            catch (Exception ex) { DiagLog.Write($"TrayIcon.Post: {ex.GetType().Name}: {ex.Message}"); }
        }, null);
    }

    /// <summary>
    /// Shows the "Update Available (vX.X.X)" menu item. Thread-safe — posts to STA thread.
    /// </summary>
    public void ShowUpdateAvailable(string version)
    {
        _uiContext?.Post(_ =>
        {
            if (_updateItem is null) return;
            _updateItem.Text    = $"Update Available (v{version})";
            _updateItem.Visible = true;
        }, null);
    }

    private void RunMessageLoop()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        _uiContext = SynchronizationContext.Current;

        _connectingIcon      = CreateBellIcon(16, Color.FromArgb(0x7A, 0x7A, 0x92));
        _connectedIcon       = CreateBellIcon(16, Color.FromArgb(0xF5, 0x9E, 0x0B));
        _reconnectingIcon    = CreateBellIcon(16, Color.FromArgb(0xF5, 0x9E, 0x0B));
        _reconnectingDimIcon = CreateBellIcon(16, Color.FromArgb(0x86, 0x57, 0x06)); // ~55% brightness of amber
        _disconnectedIcon    = CreateBellIcon(16, Color.FromArgb(0xF5, 0x9E, 0x0B), strikethrough: true);
        _errorIcon           = CreateBellIcon(16, Color.FromArgb(0xDC, 0x26, 0x26), strikethrough: true);

        var menu = new ContextMenuStrip { Renderer = new ToolStripProfessionalRenderer() };
        menu.Items.Add(new ToolStripMenuItem("Open Dashboard",          null, (_, _) => OpenDashboard()));
        menu.Items.Add(new ToolStripMenuItem("Send Test Notification",  null, (_, _) => SendTestRequested?.Invoke()));
        menu.Items.Add(new ToolStripMenuItem("View Log",                null, (_, _) => ViewLog()));
        _reconnectItem = new ToolStripMenuItem("Reconnect Now",         null, (_, _) => ReconnectRequested?.Invoke());
        menu.Items.Add(_reconnectItem);
        _updateItem = new ToolStripMenuItem("Update Available",         null, (_, _) => ApplyUpdateRequested?.Invoke())
        {
            Visible  = false,
            Font     = new System.Drawing.Font(SystemFonts.MenuFont!, System.Drawing.FontStyle.Bold),
        };
        menu.Items.Add(_updateItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Quit",                    null, (_, _) => QuitRequested?.Invoke()));

        _notifyIcon = new NotifyIcon
        {
            Text = "Toast Notification — Starting...",
            Icon = _connectingIcon,
            ContextMenuStrip = menu,
            Visible = true,
        };

        _uiReady.Set();
        ApplyState();

        Application.Run();

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        menu.Dispose();
    }

    private void ApplyState()
    {
        if (_notifyIcon == null) return;

        _animTimer?.Change(Timeout.Infinite, Timeout.Infinite);

        switch (_currentState)
        {
            case AgentConnectionState.Connected:
                _notifyIcon.Icon = _connectedIcon;
                _notifyIcon.Text = Truncate($"Connected to {ExtractHost(_stateDetail ?? _serverUrl)}");
                SetReconnectEnabled(true);
                break;

            case AgentConnectionState.Reconnecting:
                _notifyIcon.Icon = _reconnectingIcon;
                _notifyIcon.Text = Truncate($"Reconnecting since {_stateDetail ?? DateTime.Now.ToString("HH:mm:ss")}");
                SetReconnectEnabled(false);
                StartAnimation();
                break;

            case AgentConnectionState.Disconnected:
                _notifyIcon.Icon = _disconnectedIcon;
                _notifyIcon.Text = Truncate($"Lost connection at {_stateDetail ?? DateTime.Now.ToString("HH:mm:ss")}");
                SetReconnectEnabled(true);
                break;

            case AgentConnectionState.Error:
                _notifyIcon.Icon = _errorIcon;
                _notifyIcon.Text = Truncate(_stateDetail ?? "Not configured — run installer");
                SetReconnectEnabled(false);
                break;

            default: // Connecting
                _notifyIcon.Icon = _connectingIcon;
                _notifyIcon.Text = "Toast Notification — Starting...";
                SetReconnectEnabled(false);
                break;
        }
    }

    private void SetReconnectEnabled(bool enabled)
    {
        if (_reconnectItem != null) _reconnectItem.Enabled = enabled;
    }

    private void StartAnimation()
    {
        _animPhase = false;
        if (_animTimer == null)
            _animTimer = new System.Threading.Timer(OnAnimTick);
        _animTimer.Change(700, 700);
    }

    private void OnAnimTick(object? _)
    {
        _animPhase = !_animPhase;
        var icon = _animPhase ? _reconnectingDimIcon : _reconnectingIcon;
        _uiContext?.Post(_ => { if (_notifyIcon != null) _notifyIcon.Icon = icon; }, null);
    }

    private void OpenDashboard()
    {
        try
        {
            var url = _serverUrl.TrimEnd('/') + "/dashboard";
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            DiagLog.Write($"TrayIcon: OpenDashboard failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void ViewLog()
    {
        var path = DiagLog.LogFilePath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = "notepad.exe", Arguments = $"\"{path}\"", UseShellExecute = true });
        }
        catch (Exception ex)
        {
            DiagLog.Write($"TrayIcon: ViewLog failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Bell glyph rendered as a filled GraphicsPath. Uses normalized
    /// coordinates [0..1] scaled to <paramref name="size"/> so the same path
    /// data renders cleanly at 16×16 (system tray native), 32×32 (high-DPI
    /// scaling), or any other size Windows asks for. The bell silhouette is
    /// composed of (a) the bell body — a downward-rounded cup with a flared
    /// rim, (b) the clapper — a small disc beneath the rim. Strikethrough
    /// states (Disconnected / Error) overlay a single diagonal slash to
    /// communicate "not currently delivering" at a glance in the tray.
    /// </summary>
    private static Icon CreateBellIcon(int size, Color fill, bool strikethrough = false)
    {
        using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var brush = new SolidBrush(fill);
            using var path  = new GraphicsPath();

            // Bell body — symmetric profile drawn from the top stem down through
            // the dome, into the flared rim. Coordinates are in [0..1] of icon
            // size; the float multiplications scale to the requested resolution.
            var s = (float)size;
            float Sx(float x) => x * s;
            float Sy(float y) => y * s;

            // Stem on top (small rectangle hint of the bell crown).
            path.AddRectangle(new RectangleF(Sx(0.45f), Sy(0.10f), Sx(0.10f), Sy(0.06f)));

            // Body — a closed polygon that approximates a bell silhouette.
            // Top of dome at 0.16y, widening to the rim at 0.74y.
            var body = new[]
            {
                new PointF(Sx(0.32f), Sy(0.16f)),
                new PointF(Sx(0.32f), Sy(0.50f)),
                new PointF(Sx(0.22f), Sy(0.66f)),
                new PointF(Sx(0.18f), Sy(0.74f)),
                new PointF(Sx(0.82f), Sy(0.74f)),
                new PointF(Sx(0.78f), Sy(0.66f)),
                new PointF(Sx(0.68f), Sy(0.50f)),
                new PointF(Sx(0.68f), Sy(0.16f)),
            };
            // Smooth top of dome with a tiny arc by adding an ellipse there.
            path.AddPolygon(body);
            path.AddEllipse(Sx(0.28f), Sy(0.10f), Sx(0.44f), Sy(0.20f));

            // Clapper — a small disc just below the rim.
            path.AddEllipse(Sx(0.43f), Sy(0.78f), Sx(0.14f), Sy(0.14f));

            g.FillPath(brush, path);

            if (strikethrough)
            {
                // Diagonal slash from upper-right to lower-left, in the same fill
                // color, stroked thick enough to read at 16×16. We round the cap
                // so anti-aliasing doesn't leave a pixel-jagged tip.
                using var pen = new Pen(fill, Math.Max(2f, s * 0.14f))
                {
                    StartCap = LineCap.Round,
                    EndCap   = LineCap.Round,
                };
                g.DrawLine(pen, Sx(0.84f), Sy(0.16f), Sx(0.16f), Sy(0.84f));
            }
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    private static string ExtractHost(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return uri.Host;
        return url;
    }

    private static string Truncate(string text, int max = 127) =>
        text.Length <= max ? text : text[..max];

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _animTimer?.Dispose();
        _uiContext?.Post(_ => Application.Exit(), null);
        _uiThread.Join(TimeSpan.FromSeconds(2));
        _uiReady.Dispose();
    }
}
