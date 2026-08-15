using System.Windows;
using System.Windows.Input;
using PacsCdTransfer.Core.Services;

namespace PacsCdTransfer.App.Views;

public partial class LoginWindow : Window
{
    private readonly AuthService _auth = new();

    public LoginWindow()
    {
        InitializeComponent();
        DiagLog.Write("LoginWindow: constructed");
    }

    private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            DiagLog.Write("LoginWindow: Enter pressed in password box");
            LoginButton_Click(sender, e);
        }
    }

    private void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        DiagLog.Write($"LoginWindow: Giriş Yap clicked, username='{UsernameBox.Text.Trim()}', password length={PasswordBox.Password.Length}");
        var user = _auth.TryLogin(App.Settings, UsernameBox.Text.Trim(), PasswordBox.Password);
        DiagLog.Write("LoginWindow: TryLogin result = " + (user is null ? "null (rejected)" : $"OK ({user.Username})"));
        if (user is null)
        {
            ErrorText.Text = "Kullanıcı adı veya şifre hatalı.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        App.CurrentUser = user;
        DialogResult = true;
        DiagLog.Write("LoginWindow: DialogResult set to true, closing");
        Close();
    }
}
