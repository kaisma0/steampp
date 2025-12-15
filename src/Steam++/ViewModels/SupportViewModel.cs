using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamPP.Services;
using System.Diagnostics;

namespace SteamPP.ViewModels
{
    public partial class SupportViewModel : ObservableObject
    {
        private readonly LoggerService _logger;

        public SupportViewModel(LoggerService logger)
        {
            _logger = logger;
        }

        [RelayCommand]
        private void OpenLogsFolder()
        {
            _logger.Info("User opened logs folder from Support tab");
            _logger.OpenLogsFolder();
        }

        [RelayCommand]
        private void OpenGitHub()
        {
            _logger.Info("User opened GitHub link from Support tab");
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/kaisma0/steampp",
                UseShellExecute = true
            });
        }
    }
}
