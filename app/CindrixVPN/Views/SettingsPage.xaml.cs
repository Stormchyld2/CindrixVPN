using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using CindrixVPN.Services;

namespace CindrixVPN.Views;

public partial class SettingsPage : UserControl
{
    private readonly TunnelService _tunnels = new();

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += (_, _) => WgStatus.Text = _tunnels.IsInstalled
            ? "WireGuard for Windows: installed ✓"
            : "WireGuard for Windows: NOT installed. Cindrix VPN needs it to make the connection.";
    }

    private void Get_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://www.wireguard.com/install/") { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error("Could not open the browser: " + ex.Message); }
    }

    private async void Reset_Click(object sender, RoutedEventArgs e)
    {
        var count = await Task.Run(() => _tunnels.DisconnectAll());
        MessageBox.Show(Window.GetWindow(this), $"Removed {count} tunnel(s).", "Cindrix VPN", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
