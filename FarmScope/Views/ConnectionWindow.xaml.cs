using System.Security;
using System.Windows;
using RASLogAggregator.Services;

namespace RASLogAggregator.Views;

public partial class ConnectionWindow : Window
{
    public string Server { get; private set; } = "";
    public string User { get; private set; } = "";
    public SecureString Password { get; private set; } = new();

    public bool UseAltShareCreds { get; private set; }
    public string ShareUser { get; private set; } = "";
    public string SharePassword { get; private set; } = "";

    public ConnectionWindow()
    {
        InitializeComponent();
        ApplyLocalization();
        Loaded += (_, _) => ServerBox.Focus();
    }

    private void ApplyLocalization()
    {
        Title = Loc.T("cw_title");
        HeadingText.Text = Loc.T("cw_heading");
        SubText.Text = Loc.T("cw_sub");
        ServerLbl.Content = Loc.T("cw_server");
        UserLbl.Content = Loc.T("cw_user");
        PassLbl.Content = Loc.T("cw_pass");
        AltCredsCheck.Content = Loc.T("cw_altcreds");
        ShareUserLbl.Content = Loc.T("cw_share_user");
        SharePassLbl.Content = Loc.T("cw_share_pass");
        CancelBtn.Content = Loc.T("cw_cancel");
        ConnectBtn2.Content = Loc.T("cw_connect");
    }

    private void AltCreds_Changed(object sender, RoutedEventArgs e)
    {
        AltCredsPanel.Visibility = AltCredsCheck.IsChecked == true
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ServerBox.Text) ||
            string.IsNullOrWhiteSpace(UserBox.Text) ||
            PassBox.SecurePassword.Length == 0)
        {
            MessageBox.Show(Loc.T("mb_fields_body"), Loc.T("mb_fields_title"),
                            MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Server = ServerBox.Text.Trim();
        User = UserBox.Text.Trim();
        Password = PassBox.SecurePassword;

        UseAltShareCreds = AltCredsCheck.IsChecked == true;
        if (UseAltShareCreds)
        {
            ShareUser = ShareUserBox.Text.Trim();
            SharePassword = SharePassBox.Password;
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
