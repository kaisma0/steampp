using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace SteamPP.Views.Dialogs
{
    public partial class LanguageSelectionDialog : Window
    {
        public string SelectedLanguage { get; private set; } = "english";

        public LanguageSelectionDialog(List<string> availableLanguages)
        {
            InitializeComponent();

            // Populate list
            LanguageListBox.ItemsSource = availableLanguages;

            // Select English by default
            if (availableLanguages.Contains("english"))
            {
                LanguageListBox.SelectedItem = "english";
            }
            else if (availableLanguages.Any())
            {
                LanguageListBox.SelectedIndex = 0;
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1)
            {
                DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Continue_Click(object sender, RoutedEventArgs e)
        {
            if (LanguageListBox.SelectedItem != null)
            {
                SelectedLanguage = LanguageListBox.SelectedItem.ToString() ?? "english";
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show("Please select a language.", "No Selection",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
