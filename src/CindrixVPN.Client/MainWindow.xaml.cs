using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CindrixVPN.Core;

namespace CindrixVPN.Client;

public partial class MainWindow : Window
{
    private readonly TunnelController _tunnel = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private string _privateKey = string.Empty;
    private bool _busy;
    private bool _wantConnected;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnClosing;
        _timer.Tick += async (_, _) => await RefreshStatusAsync();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _privateKey = DeviceKeyStore.LoadOrCreatePrivateKey();
            PublicKeyBox.Text = WireGuardKeys.GetPublicKey(_privateKey);

            ServerCatalog catalog = ServerCatalog.Load(AppPaths.ServersFile);
            ServerBox.ItemsSource = catalog.Servers;
            ServerBox.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            ShowError("Startup problem: " + ex.Message);
            ConnectButton.IsEnabled = false;
            return;
        }

        // Pick up a tunnel that is still running from a previous session.
        if (await _tunnel.IsServiceRunningAsync())
        {
            _wantConnected = true;
        }

        _timer.Start();
        await RefreshStatusAsync();
    }

    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // The tunnel keeps running in its own service after the window closes; Disconnect stops it.
        _timer.Stop();
        await Task.CompletedTask;
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        ConnectButton.IsEnabled = false;
        ErrorText.Visibility = Visibility.Collapsed;

        try
        {
            if (_wantConnected)
            {
                SetState("Disconnecting…", "Please wait", Brushes.Gray);
                await _tunnel.DisconnectAsync();
                _wantConnected = false;
            }
            else
            {
                if (ServerBox.SelectedItem is not ServerEntry server)
                {
                    ShowError("Pick a server first.");
                    return;
                }

                SetState("Connecting…", "Starting the encrypted tunnel", (Brush)FindResource("Accent"));
                _wantConnected = true;
                await _tunnel.ConnectAsync(_privateKey, server);
            }
        }
        catch (Exception ex)
        {
            _wantConnected = false;
            ShowError(ex.Message);
            try
            {
                await _tunnel.DisconnectAsync();
            }
            catch (Exception)
            {
            }
        }
        finally
        {
            _busy = false;
            ConnectButton.IsEnabled = true;
            await RefreshStatusAsync();
        }
    }

    private async Task RefreshStatusAsync()
    {
        if (_busy)
        {
            return;
        }

        if (!_wantConnected)
        {
            SetState("Disconnected", "Your traffic is not protected", (Brush)FindResource("Muted"));
            StatsText.Text = string.Empty;
            ConnectButton.Content = "Connect";
            ServerBox.IsEnabled = true;
            return;
        }

        ConnectButton.Content = "Disconnect";
        ServerBox.IsEnabled = false;

        if (!await _tunnel.IsServiceRunningAsync())
        {
            _wantConnected = false;
            ShowError("The tunnel service stopped unexpectedly.");
            return;
        }

        TunnelStats? stats = await _tunnel.GetStatsAsync();
        bool handshake = stats?.LastHandshake is { } h && DateTimeOffset.UtcNow - h < TimeSpan.FromMinutes(3);
        if (handshake)
        {
            SetState("Connected", "Your traffic is routed through the server", (Brush)FindResource("Accent"));
            StatsText.Text = $"↓ {FormatBytes(stats!.RxBytes)}   ↑ {FormatBytes(stats.TxBytes)}";
        }
        else
        {
            SetState("Connecting…", "Waiting for the server handshake", (Brush)FindResource("Accent"));
            StatsText.Text = string.Empty;
        }
    }

    private void SetState(string title, string detail, Brush ring)
    {
        StatusText.Text = title;
        DetailText.Text = detail;
        StatusDot.Stroke = ring;
        StatusDot.Fill = title == "Connected" ? (Brush)FindResource("AccentDeep") : (Brush)FindResource("PanelLight");
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void CopyKey_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(PublicKeyBox.Text);
        }
        catch (Exception)
        {
            PublicKeyBox.SelectAll();
            PublicKeyBox.Focus();
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }
}
