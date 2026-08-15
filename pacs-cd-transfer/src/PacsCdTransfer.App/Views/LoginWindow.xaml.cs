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
    }

    private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) LoginButton_Click(sender, e);
    }

    private void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        var user = _auth.TryLogin(App.Settings, UsernameBox.Text.Trim(), PasswordBox.Password);
        if (user is null)
        {
            ErrorText.Text = "Kullanıcı adı veya şifre hatalı.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        App.CurrentUser = user;
        DialogResult = true;
        Close();
    }
}
