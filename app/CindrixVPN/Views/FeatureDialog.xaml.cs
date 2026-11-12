using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using CindrixVPN.ViewModels;

namespace CindrixVPN.Views;

/// <summary>One small settings window used by the NetShield, Kill Switch and Split Tunneling tiles.</summary>
public partial class FeatureDialog : Window
{
    public FeatureDialog(string kind, MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        ProfileText.Text = "Profile: " + vm.ProfileName;
        SplitBox.Visibility = Visibility.Collapsed;

        switch (kind)
        {
            case "NetShield":
                Title = "NetShield";
                IconText.Text = "";
                TitleText.Text = "NetShield";
                DescriptionText.Text = "Blocks ads, trackers and known malicious websites for this profile by using the server's filtering DNS.";
                SwitchLabel.Text = "Block ads, trackers and threats";
                FeatureSwitch.SetBinding(ToggleButton.IsCheckedProperty, new Binding("SelectedProfile.NetShield") { Mode = BindingMode.TwoWay });
                NoteText.Text = "Needs the NetShield service on the server (run: sudo ./cindrixvpn-server.sh setup-netshield). "
                              + "Turning it on or off while connected reconnects the tunnel to apply it.";
                break;

            case "KillSwitch":
                Title = "Kill Switch";
                IconText.Text = "";
                TitleText.Text = "Kill Switch";
                DescriptionText.Text = "If the VPN drops, all internet traffic is blocked until it is back, so nothing leaks onto your normal connection.";
                SwitchLabel.Text = "Block the internet if the VPN drops";
                FeatureSwitch.SetBinding(ToggleButton.IsCheckedProperty, new Binding("SelectedProfile.KillSwitch") { Mode = BindingMode.TwoWay });
                NoteText.Text = "Uses WireGuard's own firewall rules while the tunnel runs. It pauses while split tunneling rules exist, "
                              + "and it also blocks local network devices (printers, TVs) while connected.";
                break;

            default:
                Title = "Split Tunneling";
                IconText.Text = "";
                TitleText.Text = "Split Tunneling";
                DescriptionText.Text = "Choose addresses that skip the VPN and use your normal internet connection.";
                SwitchRow.Visibility = Visibility.Collapsed;
                SplitBox.Visibility = Visibility.Visible;
                ExcludesBox.SetBinding(System.Windows.Controls.TextBox.TextProperty,
                    new Binding("SelectedProfile.ExcludesText") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus });
                NoteText.Text = "Examples: 192.168.1.0/24 for your home network, or 203.0.113.5 for one server. IPv4 only. "
                              + "This version splits by address, not by app. The kill switch pauses while any rule exists.";
                break;
        }
    }

    private void Done_Click(object sender, RoutedEventArgs e)
    {
        // Move focus off the text box so its edit is committed before the window closes.
        FeatureSwitch.Focus();
        Close();
    }
}
