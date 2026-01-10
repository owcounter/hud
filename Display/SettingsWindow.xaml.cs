using Owmeta.Services;
using System.Windows;

namespace Owmeta.Display
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
            LoadSettings();
        }

        private void LoadSettings()
        {
            MinScoreF2Box.Text = AppSettings.Instance.MinScoreF2.ToString();
            MinScoreF3Box.Text = AppSettings.Instance.MinScoreF3.ToString();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(MinScoreF2Box.Text, out int minF2))
            {
                AppSettings.Instance.MinScoreF2 = minF2;
            }

            if (int.TryParse(MinScoreF3Box.Text, out int minF3))
            {
                AppSettings.Instance.MinScoreF3 = minF3;
            }

            AppSettings.Instance.Save();
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
