using System.IO;
using System.Windows;
using System.Windows.Controls;
using CindrixVPN.ViewModels;
using Microsoft.Win32;

namespace CindrixVPN.Views;

public partial class ProfilesPage : UserControl
{
    public ProfilesPage() => InitializeComponent();

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var dialog = new OpenFileDialog
        {
            Title = "Choose the WireGuard config made for this device",
            Filter = "WireGuard config (*.conf)|*.conf|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            vm.AddProfile(Path.GetFileNameWithoutExtension(dialog.FileName), File.ReadAllText(dialog.FileName));
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), "That file could not be used. " + ex.Message, "Cindrix VPN",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { SelectedProfile: { } profile } vm) return;
        var answer = MessageBox.Show(Window.GetWindow(this),
            $"Remove the profile '{profile.Name}'? Its saved config is deleted from this PC.", "Cindrix VPN",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes) await vm.RemoveProfileAsync(profile);
    }
}
