using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using CindrixVPN.ViewModels;
using CindrixVPN.Views;

namespace CindrixVPN;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly Dictionary<string, UserControl> _pages;
    private readonly BitmapImage _lockGreen = LoadImage("overlay-connected.png");
    private readonly BitmapImage _unlockRed = LoadImage("overlay-disconnected.png");
    private bool _hiddenOnce;

    /// <summary>Raised the first time the window is sent to the tray, so the app can explain where it went.</summary>
    public event Action? FirstHide;

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        _pages = new Dictionary<string, UserControl>
        {
            ["Dashboard"] = new DashboardPage(),
            ["Profiles"] = new ProfilesPage(),
            ["Logs"] = new LogsPage(),
            ["Tools"] = new ToolsPage(),
            ["Settings"] = new SettingsPage(),
            ["About"] = new AboutPage()
        };
        PageHost.Content = _pages["Dashboard"];

        vm.Message += text => Dispatcher.Invoke(() => ShowMessage(text));
        vm.NavigateRequested += page => Dispatcher.Invoke(() => GoTo(page));
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.IsConnected) or nameof(MainViewModel.StatusText))
                UpdateOverlay();
        };
        UpdateOverlay();
    }

    private static BitmapImage LoadImage(string name)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri($"pack://application:,,,/Assets/{name}");
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    }

    /// <summary>Taskbar badge, bottom-right of the icon: green lock when connected, red open lock otherwise.</summary>
    private void UpdateOverlay()
    {
        bool on = _vm.IsConnected;
        TaskbarInfo.Overlay = on ? _lockGreen : _unlockRed;
        TaskbarInfo.Description = on ? "Cindrix VPN — protected" : "Cindrix VPN — not protected";
    }

    public void ShowMessage(string text)
    {
        if (IsVisible) MessageBox.Show(this, text, "Cindrix VPN", MessageBoxButton.OK, MessageBoxImage.Information);
        else MessageBox.Show(text, "Cindrix VPN", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void GoTo(string page)
    {
        if (!_pages.TryGetValue(page, out var control)) return;
        PageHost.Content = control;
        var radio = page switch
        {
            "Dashboard" => NavDashboard,
            "Profiles" => NavProfiles,
            "Logs" => NavLogs,
            "Tools" => NavTools,
            "Settings" => NavSettings,
            _ => NavAbout
        };
        if (radio.IsChecked != true) radio.IsChecked = true;
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Name: { } name } && _pages != null)
            GoTo(name.Replace("Nav", ""));
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        // A borderless maximized window overhangs the screen by a few pixels; pull it back in.
        Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
    }

    /// <summary>The close button hides to the tray; only "Exit VPN" in the tray menu really closes the app.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!App.Exiting)
        {
            e.Cancel = true;
            Hide();
            if (!_hiddenOnce)
            {
                _hiddenOnce = true;
                FirstHide?.Invoke();
            }
        }
        base.OnClosing(e);
    }
}
