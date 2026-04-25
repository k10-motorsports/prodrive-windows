using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RaceCorProDrive.Auth;

namespace RaceCorProDrive.Pages
{
    public sealed partial class LoginPage : Page
    {
        private readonly AuthService _authService = new();

        public LoginPage()
        {
            this.InitializeComponent();
        }

        private async void OnSignIn(object sender, RoutedEventArgs e)
        {
            SignInButton.IsEnabled = false;
            ErrorMessage.Text = "";

            try
            {
                await _authService.SignInAsync();
                // Navigation handled by AuthService callback
            }
            catch (Exception ex)
            {
                ErrorMessage.Text = $"Sign-in failed: {ex.Message}";
                SignInButton.IsEnabled = true;
            }
        }
    }
}
