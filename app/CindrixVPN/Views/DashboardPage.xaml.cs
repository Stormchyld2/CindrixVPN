using System.Windows;
using System.Windows.Controls;
using CindrixVPN.ViewModels;

namespace CindrixVPN.Views;

public partial class DashboardPage : UserControl
{
    public DashboardPage() => InitializeComponent();

    private void Tile_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || sender is not Button { Tag: string kind }) return;

        if (vm.SelectedProfile == null)
        {
            MessageBox.Show(Window.GetWindow(this), "Add a profile first (Profiles page), then these settings apply to it.",
                "Cindrix VPN", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        new FeatureDialog(kind, vm) { Owner = Window.GetWindow(this) }.ShowDialog();
    }
}
