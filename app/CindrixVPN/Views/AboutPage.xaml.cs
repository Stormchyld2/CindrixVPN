using System.Reflection;
using System.Windows.Controls;

namespace CindrixVPN.Views;

public partial class AboutPage : UserControl
{
    public AboutPage()
    {
        InitializeComponent();
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = "Version " + (v?.ToString(3) ?? "1.0.0");
    }
}
