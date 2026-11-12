using System.Net.Http;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using CindrixVPN.Services;

namespace CindrixVPN.Views;

public partial class ToolsPage : UserControl
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    public ToolsPage() => InitializeComponent();

    private void Say(string text) => Output.Text += text + Environment.NewLine;

    private async void Ip_Click(object sender, RoutedEventArgs e)
    {
        try { Say("Public IP: " + (await Http.GetStringAsync("https://api.ipify.org")).Trim()); }
        catch (Exception ex) { Say("Could not check the IP: " + ex.Message); }
    }

    private async void Dns_Click(object sender, RoutedEventArgs e)
    {
        foreach (var host in new[] { "example.com", "cloudflare.com" })
        {
            try
            {
                var started = DateTime.UtcNow;
                var addresses = await Dns.GetHostAddressesAsync(host);
                Say($"{host} -> {addresses[0]} ({(DateTime.UtcNow - started).TotalMilliseconds:0} ms)");
            }
            catch (Exception ex) { Say($"{host} failed: {ex.Message}"); }
        }
    }

    private async void Flush_Click(object sender, RoutedEventArgs e)
    {
        var (code, _) = await Task.Run(() => TunnelService.Run("ipconfig.exe", "/flushdns", 10000));
        Say(code == 0 ? "DNS cache flushed." : "Could not flush the DNS cache.");
    }
}
