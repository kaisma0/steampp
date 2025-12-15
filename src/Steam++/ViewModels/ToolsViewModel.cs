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
        private void OpenGreenLuma()
        {
            OpenUrl("https://cs.rin.ru/forum/viewtopic.php?p=2063857#p2063857");
        }

        [RelayCommand]
        private void OpenManifestSite()
        {
            OpenUrl("https://manifest.morrenus.xyz/");
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
