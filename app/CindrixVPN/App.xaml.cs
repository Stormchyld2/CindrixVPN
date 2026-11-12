using System.Windows;
using CindrixVPN.Services;
using CindrixVPN.ViewModels;

namespace CindrixVPN;

public partial class App : Application
{
    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private TrayService? _tray;
    private MainWindow? _window;
    private MainViewModel? _vm;

    /// <summary>True only when the user chose "Exit VPN" (closing the window just hides it to the tray).</summary>
    public static bool Exiting { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // One copy only: starting it again just brings the existing window back.
        _mutex = new Mutex(true, @"Local\CindrixVPN.SingleInstance", out bool first);
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\CindrixVPN.Show");
        if (!first)
        {
            _showEvent.Set();
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Unexpected error: " + args.Exception);
            args.Handled = true;
        };
        SessionEnding += (_, _) => Exiting = true;

        Paths.EnsureSecure();
        Log.Info("Cindrix VPN started.");

        _vm = new MainViewModel();
        _window = new MainWindow(_vm);
        _tray = new TrayService();
        _tray.OpenRequested += () => Dispatcher.Invoke(ShowWindow);
        _tray.ExitRequested += () => Dispatcher.Invoke(ExitApp);
        _vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.StatusText))
                _tray?.SetTooltip(_vm.TooltipText);
        };
        _window.FirstHide += () => _tray?.Balloon("Cindrix VPN", "Still running in the tray. Click the icon to open it, or right-click and choose Exit VPN.");

        ThreadPool.RegisterWaitForSingleObject(_showEvent, (_, _) => Dispatcher.Invoke(ShowWindow), null, -1, false);

        _window.Show();
        _vm.Start();
    }

    private void ShowWindow()
    {
        if (_window == null) return;
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private async void ExitApp()
    {
        if (Exiting) return;
        Exiting = true;
        try
        {
            if (_vm != null) await _vm.DisconnectAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Error while exiting: " + ex.Message);
        }
        Log.Info("Cindrix VPN exited.");
        _tray?.Dispose();
        _window?.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
