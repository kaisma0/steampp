using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;

namespace SteamPP.ViewModels
{
    public partial class ToolsViewModel : ObservableObject
    {
        [ObservableProperty]
        private int _selectedTabIndex = 0;

        [RelayCommand]
        private void OpenSteamTools()
        {
            OpenUrl("https://www.steamtools.net/download.html");
        }



        [RelayCommand]
        private void OpenFixes()
        {
            OpenUrl("https://github.com/kaisma0/fixes");
        }



        private void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch
            {
                // Handle error silently
            }
        }
    }
}
