using System;
using System.Threading;
using System.Windows;
using System.Windows.Input;

namespace SteamPP.Views.Dialogs
{
    public partial class UpdateProgressDialog : Window
    {
        private CancellationTokenSource? _cancellationTokenSource;
        private bool _downloadCompleted;

        public bool WasCancelled { get; private set; }

        public UpdateProgressDialog()
        {
            InitializeComponent();
        }

        public void SetVersion(string version)
        {
            VersionText.Text = $"Downloading version {version}";
        }

        public void SetCancellationTokenSource(CancellationTokenSource cts)
        {
            _cancellationTokenSource = cts;
        }

        public void UpdateProgress(double percentage, string? statusText = null)
        {
            Dispatcher.Invoke(() =>
            {
                DownloadProgressBar.Value = percentage;
                PercentageText.Text = $"{percentage:F0}%";

                if (!string.IsNullOrEmpty(statusText))
                {
                    StatusText.Text = statusText;
                }
                else if (percentage < 100)
                {
                    StatusText.Text = "Downloading...";
                }
                else
                {
                    StatusText.Text = "Download complete!";
                }
            });
        }

        public void SetCompleted()
        {
            _downloadCompleted = true;
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = "Download complete! Installing...";
                CancelButton.IsEnabled = false;
                CancelButton.Content = "Please wait...";
            });
        }

        public void SetError(string message)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = message;
                CancelButton.Content = "Close";
                CancelButton.IsEnabled = true;
            });
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1)
            {
                DragMove();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (_downloadCompleted)
            {
                DialogResult = true;
                Close();
                return;
            }

            WasCancelled = true;
            _cancellationTokenSource?.Cancel();
            StatusText.Text = "Cancelling...";
            CancelButton.IsEnabled = false;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_downloadCompleted && !WasCancelled)
            {
                WasCancelled = true;
                _cancellationTokenSource?.Cancel();
            }
        }
    }
}
