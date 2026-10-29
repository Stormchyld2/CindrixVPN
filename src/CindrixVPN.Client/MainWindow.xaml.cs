using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace CindrixVPN.Client;

public partial class MainWindow : Window
{
    private readonly VpnSession _session;
    private bool _rendering;

    internal MainWindow(VpnSession session)
    {
        _session = session;
        InitializeComponent();
        _session.Changed += Render;
        Render();
    }

    /// <summary>The X button hides the window to the tray. Only the tray menu's Exit really quits.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!((App)Application.Current).IsExiting)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
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
            PublicKeyBox.Text = _session.PublicKey;
            StartupCheck.IsChecked = _session.StartWithWindows;
            AutoConnectCheck.IsChecked = _session.Settings.AutoConnect;

            Brush ring = _session.State == VpnState.Disconnected
                ? (Brush)FindResource("Muted")
                : (Brush)FindResource("Accent");
            StatusDot.Stroke = ring;
            StatusDot.Fill = _session.State == VpnState.Connected
                ? (Brush)FindResource("AccentDeep")
                : (Brush)FindResource("PanelLight");
            StatusText.Text = _session.State switch
            {
                VpnState.Connected => "Connected",
                VpnState.Connecting => "Connecting…",
                _ => "Disconnected",
            };
            DetailText.Text = _session.Detail;
            StatsText.Text = _session.StatsText;
            ConnectButton.Content = _session.IsOn ? "Disconnect" : "Connect";
            ConnectButton.IsEnabled = _session.Ready && !_session.Busy;

            if (string.IsNullOrEmpty(_session.Error))
            {
                ErrorText.Visibility = Visibility.Collapsed;
            }
            else
            {
                ErrorText.Text = _session.Error;
                ErrorText.Visibility = Visibility.Visible;
            }
        }
        finally
        {
            _rendering = false;
        }
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e) => await _session.ToggleAsync();

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
}
