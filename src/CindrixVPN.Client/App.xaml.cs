using System.Windows;

namespace CindrixVPN.Client;

public partial class App : Application
{
    private const string MutexName = @"Local\CindrixVPN.SingleInstance";
    private const string ShowEventName = @"Local\CindrixVPN.ShowDashboard";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private VpnSession? _session;
    private MainWindow? _dashboard;
    private TrayController? _tray;
    private bool _exiting;

    internal bool IsExiting => _exiting;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _mutex = new Mutex(true, MutexName, out bool isFirst);
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        if (!isFirst)
        {
            // Another copy is already running (usually in the tray): ask it to show its dashboard.
            _showEvent.Set();
            Shutdown();
            return;
        }

        Thread listener = new(() =>
        {
            while (_showEvent.WaitOne())
            {
                Dispatcher.BeginInvoke(ShowDashboard);
            }
        })
        { IsBackground = true };
        listener.Start();

        bool startMinimized = e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase);

        _session = new VpnSession();
        _dashboard = new MainWindow(_session);
        _tray = new TrayController(_session, ShowDashboard, ExitAppAsync);

        await _session.InitializeAsync();

        if (!startMinimized)
        {
            ShowDashboard();
        }
    }

    internal void ShowDashboard()
    {
        if (_dashboard is null)
        {
            return;
        }

        _dashboard.Show();
        if (_dashboard.WindowState == WindowState.Minimized)
        {
            _dashboard.WindowState = WindowState.Normal;
        }

        _dashboard.Activate();
    }

    private async Task ExitAppAsync()
    {
        _exiting = true;
        if (_session is not null)
        {
            await _session.ShutdownAsync();
        }

        _tray?.Dispose();
        _dashboard?.Close();
        Shutdown();
    }
}
