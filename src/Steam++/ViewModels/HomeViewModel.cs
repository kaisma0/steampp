using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamPP.Models;
using SteamPP.Services;
using System;
using System.IO;

namespace SteamPP.ViewModels
{
    public partial class HomeViewModel : ObservableObject
    {
        private readonly SettingsService _settingsService;
        private readonly SteamService _steamService;
        private readonly NotificationService _notificationService;

        [ObservableProperty]
        private bool _showLaunchSteam;

        [ObservableProperty]
        private bool _showLaunchGreenLuma;

        [ObservableProperty]
        private string _currentModeText = string.Empty;

        [ObservableProperty]
        private string _currentModeDescription = string.Empty;

        public HomeViewModel(
            SettingsService settingsService,
            SteamService steamService,
            NotificationService notificationService)
        {
            _settingsService = settingsService;
            _steamService = steamService;
            _notificationService = notificationService;

            RefreshMode();
        }

        public void RefreshMode()
        {
            var settings = _settingsService.LoadSettings();

            // Determine which launch button to show based on mode
            // SteamTools OR GreenLuma Stealth (User32) mode = Launch Steam
            // GreenLuma Normal OR Stealth (Any Folder) mode = Launch GreenLuma (DLLInjector)
            ShowLaunchSteam = settings.Mode == ToolMode.SteamTools ||
                              (settings.Mode == ToolMode.GreenLuma && settings.GreenLumaSubMode == GreenLumaMode.StealthUser32);
            ShowLaunchGreenLuma = settings.Mode == ToolMode.GreenLuma &&
                                  (settings.GreenLumaSubMode == GreenLumaMode.Normal || settings.GreenLumaSubMode == GreenLumaMode.StealthAnyFolder);

            // Set current mode text
            if (settings.Mode == ToolMode.SteamTools)
            {
                CurrentModeText = "Current Mode: SteamTools";
            }
            else if (settings.Mode == ToolMode.GreenLuma)
            {
                CurrentModeText = settings.GreenLumaSubMode switch
                {
                    GreenLumaMode.Normal => "Current Mode: GreenLuma (Normal)",
                    GreenLumaMode.StealthAnyFolder => "Current Mode: GreenLuma (Stealth - Any Folder)",
                    GreenLumaMode.StealthUser32 => "Current Mode: GreenLuma (Stealth)",
                    _ => "Current Mode: GreenLuma"
                };
            }
            else
            {
                CurrentModeText = "Current Mode: Unknown";
            }

            // Set current mode description
            if (settings.Mode == ToolMode.SteamTools)
            {
                CurrentModeDescription = "SteamTools mode: Standard download mode with .lua files installed to stplug-in folder. Use this mode for Steam game and depot management.";
            }
            else if (settings.Mode == ToolMode.GreenLuma)
            {
                CurrentModeDescription = settings.GreenLumaSubMode switch
                {
                    GreenLumaMode.Normal => "GreenLuma (Normal): DLLInjector at {SteamPath}/DLLInjector.exe, AppList at {SteamPath}/AppList. Advanced depot control with language selection and DLLInjector launch.",
                    GreenLumaMode.StealthAnyFolder => "GreenLuma (Stealth - Any Folder): For best security - DLLInjector in custom location, AppList in same folder. Advanced depot control with custom paths and DLLInjector launch.",
                    GreenLumaMode.StealthUser32 => "GreenLuma (Stealth): Launches Steam normally. AppList entries are generated and depot keys stored in config.vdf.",
                    _ => "GreenLuma mode: Advanced depot control with language selection."
                };
            }
            else
            {
                CurrentModeDescription = "No mode selected. Please configure your tool mode in Settings.";
            }
        }

        [RelayCommand]
        private void LaunchSteam()
        {
            try
            {
                var steamPath = _steamService.GetSteamPath();

                if (string.IsNullOrEmpty(steamPath))
                {
                    _notificationService.ShowError("Steam path is not configured. Please set it in Settings.");
                    return;
                }

                var steamExePath = Path.Combine(steamPath, "steam.exe");

                if (!File.Exists(steamExePath))
                {
                    _notificationService.ShowError($"Steam.exe not found at: {steamExePath}\n\nPlease check your Steam path in Settings.");
                    return;
                }

                // Launch Steam
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = steamExePath,
                    UseShellExecute = true,
                    WorkingDirectory = steamPath
                };

                System.Diagnostics.Process.Start(processInfo);
                _notificationService.ShowSuccess("Steam launched successfully!");
            }
            catch (Exception ex)
            {
                _notificationService.ShowError($"Failed to launch Steam: {ex.Message}");
            }
        }

        [RelayCommand]
        private void LaunchGreenLuma()
        {
            try
            {
                var settings = _settingsService.LoadSettings();

                // Check if DLLInjector path is set
                if (string.IsNullOrEmpty(settings.DLLInjectorPath))
                {
                    _notificationService.ShowError("DLLInjector path is not set. Please configure it in Settings.");
                    return;
                }

                // Check if DLLInjector exists
                if (!File.Exists(settings.DLLInjectorPath))
                {
                    _notificationService.ShowError($"DLLInjector.exe not found at: {settings.DLLInjectorPath}\n\nPlease check the path in Settings.");
                    return;
                }

                // Launch DLLInjector
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = settings.DLLInjectorPath,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(settings.DLLInjectorPath)
                };

                System.Diagnostics.Process.Start(processInfo);
                _notificationService.ShowSuccess("GreenLuma launched successfully!");
            }
            catch (Exception ex)
            {
                _notificationService.ShowError($"Failed to launch GreenLuma: {ex.Message}");
            }
        }
    }
}
