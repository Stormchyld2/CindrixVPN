using System.Windows;
using System.Windows.Media;

namespace CindrixVPN.Client;

public partial class TrayFlyout : Window
{
    private readonly VpnSession _session;
    private readonly Action _openDashboard;
    private DateTime _lastHidden = DateTime.MinValue;
    private bool _rendering;

    internal TrayFlyout(VpnSession session, Action openDashboard)
    {
        _session = session;
        _openDashboard = openDashboard;
        InitializeComponent();
        ServerBox.ItemsSource = null;
        _session.Changed += Render;
        Deactivated += (_, _) => HideFlyout();
    }

    internal void ToggleVisible()
    {
        if (IsVisible)
        {
            HideFlyout();
            return;
        }

        // Clicking the tray icon deactivates the flyout first; ignore that click so it does not reopen at once.
        if (DateTime.UtcNow - _lastHidden < TimeSpan.FromMilliseconds(300))
        {
            return;
        }

        Render();
        Show();
        UpdateLayout();
        Rect area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - 8;
        Top = area.Bottom - ActualHeight - 8;
        Activate();
    }

    private void HideFlyout()
    {
        if (IsVisible)
        {
            Hide();
            _lastHidden = DateTime.UtcNow;
        }
    }

    private void Render()
    {
        _rendering = true;
        try
        {
            if (!ReferenceEquals(ServerBox.ItemsSource, _session.Servers))
            {
                ServerBox.ItemsSource = _session.Servers;
            }

            ServerBox.SelectedItem = _session.SelectedServer;
            ServerBox.IsEnabled = _session.Ready && !_session.IsOn;
            StartupCheck.IsChecked = _session.StartWithWindows;
            AutoConnectCheck.IsChecked = _session.Settings.AutoConnect;

            StatusText.Text = _session.State switch
            {
                VpnState.Connected => "Connected",
                VpnState.Connecting => "Connecting…",
                _ => "Disconnected",
            };
            Dot.Fill = _session.State == VpnState.Disconnected
                ? (Brush)FindResource("Muted")
                : (Brush)FindResource("Accent");
            StatsText.Text = _session.StatsText;
            ToggleButton.Content = _session.IsOn ? "Disconnect" : "Connect";
            ToggleButton.IsEnabled = _session.Ready && !_session.Busy;
        }
        finally
        {
            _rendering = false;
        }
    }

    private async void ToggleButton_Click(object sender, RoutedEventArgs e) => await _session.ToggleAsync();

    private void ServerBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!_rendering && ServerBox.SelectedItem is CindrixVPN.Core.ServerEntry server)
        {
            _session.SelectServer(server);
        }
    }

    private async void StartupCheck_Click(object sender, RoutedEventArgs e) =>
        await _session.SetStartWithWindowsAsync(StartupCheck.IsChecked == true);

    private void AutoConnectCheck_Click(object sender, RoutedEventArgs e) =>
        _session.SetAutoConnect(AutoConnectCheck.IsChecked == true);

    private void OpenDashboard_Click(object sender, RoutedEventArgs e)
    {
        HideFlyout();
        _openDashboard();
    }
}
