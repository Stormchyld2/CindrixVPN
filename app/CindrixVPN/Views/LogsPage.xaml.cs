using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using CindrixVPN.Services;
using CindrixVPN.ViewModels;

namespace CindrixVPN.Views;

public partial class LogsPage : UserControl
{
    public LogsPage()
    {
        InitializeComponent();
        // Keep the newest line in view.
        LogList.Loaded += (_, _) => ScrollToEnd();
        LayoutUpdated += (_, _) => ScrollToEnd();
    }

    private void ScrollToEnd()
    {
        if (LogList.Items.Count > 0) LogList.ScrollIntoView(LogList.Items[^1]);
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm) Clipboard.SetText(string.Join(Environment.NewLine, vm.Logs));
    }

    private void Folder_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Paths.Logs}\"") { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error("Could not open the log folder: " + ex.Message); }
    }
}
