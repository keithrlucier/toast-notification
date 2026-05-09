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
/// Diana's M2.C spec:
///   Connected       #00C9A7 teal, static
///   Reconnecting    #F59E0B amber, 700ms pulse between 100% and 55% brightness
///   Disconnected    #F59E0B amber, static
///   Error           #DC2626 red, static
///   Connecting      #7A7A92 dim, static
///
/// INFO-M2C-001: HICONs created via Bitmap.GetHicon() are not explicitly freed.
/// Acceptable for process-lifetime tray icons — handles are released on process exit.
/// Before M9 GA: swap placeholder GDI+ circles for SVG-rasterized production assets.
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

        _connectingIcon    = CreateCircleIcon(16, Color.FromArgb(0x7A, 0x7A, 0x92));
        _connectedIcon     = CreateCircleIcon(16, Color.FromArgb(0x00, 0xC9, 0xA7));
        _reconnectingIcon  = CreateCircleIcon(16, Color.FromArgb(0xF5, 0x9E, 0x0B));
        _reconnectingDimIcon = CreateCircleIcon(16, Color.FromArgb(0x86, 0x57, 0x06)); // ~55% brightness of amber
        _disconnectedIcon  = CreateCircleIcon(16, Color.FromArgb(0xF5, 0x9E, 0x0B));
        _errorIcon         = CreateCircleIcon(16, Color.FromArgb(0xDC, 0x26, 0x26));

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

    private static Icon CreateCircleIcon(int size, Color fill)
    {
        using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(fill);
            var m = Math.Max(2, size / 6);
            g.FillEllipse(brush, m, m, size - m * 2 - 1, size - m * 2 - 1);
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
