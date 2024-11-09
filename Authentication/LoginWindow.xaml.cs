using Newtonsoft.Json;
using Owcounter.Authentication;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace Owcounter
{
    public partial class LoginWindow : Window
    {
        private readonly KeycloakAuth? keycloakAuth;

        public LoginWindow(KeycloakAuth keycloakAuth)
        {
            InitializeComponent();
            this.keycloakAuth = keycloakAuth;
        }

        private async void btnLogin_Click(object sender, RoutedEventArgs e)
        {
            if (keycloakAuth == null) return;

            string username = txtUsername.Text;
            string password = chkShowPassword.IsChecked == true ?
                            txtPasswordUnmasked.Text :
                            txtPassword.Password;

            try
            {
                var tokenResponse = await keycloakAuth.Authenticate(username, password);
                File.WriteAllText("owcounter_oauth_token.json", JsonConvert.SerializeObject(tokenResponse));

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Login failed: {ex.Message}\n\nPlease make sure you're using the correct credentials for your OWCOUNTER account.",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        private void btnSignUp_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://owcounter.com/signup")
            {
                UseShellExecute = true
            });
        }

        private void chkShowPassword_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (chkShowPassword.IsChecked == true)
            {
                txtPasswordUnmasked.Text = txtPassword.Password;
                txtPassword.Visibility = Visibility.Collapsed;
                txtPasswordUnmasked.Visibility = Visibility.Visible;
                txtPasswordUnmasked.Focus();
            }
            else
            {
                txtPassword.Password = txtPasswordUnmasked.Text;
                txtPasswordUnmasked.Visibility = Visibility.Collapsed;
                txtPassword.Visibility = Visibility.Visible;
                txtPassword.Focus();
            }
        }
    }
}