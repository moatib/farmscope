using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;
using RASLogAggregator.Services;

namespace RASLogAggregator.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        Title = Loc.T("about_title");
        TaglineText.Text = Loc.T("app_subtitle");
        DevLabel.Text = Loc.T("about_dev");
        AiText.Text = Loc.T("about_ai");
        CloseBtn.Content = Loc.T("about_close");

        var v = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = v != null ? $"v{v.Major}.{v.Minor}.{v.Build}" : "";
    }

    private void Link_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch { /* navigateur indisponible : best effort */ }
        e.Handled = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
