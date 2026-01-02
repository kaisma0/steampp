using SteamPP.Helpers;
using Microsoft.Extensions.DependencyInjection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamPP.Services;
using SteamPP.Views;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace SteamPP.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly SteamService _steamService;
        private readonly SettingsService _settingsService;
        private readonly UpdateService _updateService;
        private readonly NotificationService _notificationService;
        private readonly Dictionary<string, UserControl> _cachedViews = new Dictionary<string, UserControl>();

        [ObservableProperty]
        private object? _currentPage;

        [ObservableProperty]
        private string _currentPageName = "Home";

        private HomeViewModel? _homeViewModel;
        public HomeViewModel HomeViewModel => _homeViewModel ??= _serviceProvider.GetRequiredService<HomeViewModel>();

        private LuaInstallerViewModel? _luaInstallerViewModel;
        public LuaInstallerViewModel LuaInstallerViewModel => _luaInstallerViewModel ??= _serviceProvider.GetRequiredService<LuaInstallerViewModel>();

        private LibraryViewModel? _libraryViewModel;
        public LibraryViewModel LibraryViewModel => _libraryViewModel ??= _serviceProvider.GetRequiredService<LibraryViewModel>();

        private StoreViewModel? _storeViewModel;
        public StoreViewModel StoreViewModel => _storeViewModel ??= _serviceProvider.GetRequiredService<StoreViewModel>();

        private DownloadsViewModel? _downloadsViewModel;
        public DownloadsViewModel DownloadsViewModel => _downloadsViewModel ??= _serviceProvider.GetRequiredService<DownloadsViewModel>();

        private ToolsViewModel? _toolsViewModel;
        public ToolsViewModel ToolsViewModel => _toolsViewModel ??= _serviceProvider.GetRequiredService<ToolsViewModel>();

        private SettingsViewModel? _settingsViewModel;
        public SettingsViewModel SettingsViewModel => _settingsViewModel ??= _serviceProvider.GetRequiredService<SettingsViewModel>();

        private SupportViewModel? _supportViewModel;
        public SupportViewModel SupportViewModel => _supportViewModel ??= _serviceProvider.GetRequiredService<SupportViewModel>();

        public MainViewModel(
            IServiceProvider serviceProvider,
            SteamService steamService,
            SettingsService settingsService,
            UpdateService updateService,
            NotificationService notificationService)
        {
            _serviceProvider = serviceProvider;
            _steamService = steamService;
            _settingsService = settingsService;
            _updateService = updateService;
            _notificationService = notificationService;

            // Start at Home page
            CurrentPage = GetOrCreateView("Home", () => new HomePage { DataContext = HomeViewModel });
            CurrentPageName = "Home";
            HomeViewModel.RefreshMode();
        }

        private UserControl GetOrCreateView(string key, Func<UserControl> createView)
        {
            if (!_cachedViews.ContainsKey(key))
            {
                _cachedViews[key] = createView();
            }
            return _cachedViews[key];
        }

        private bool CanNavigateAway()
        {
            // Check if we're currently on settings page and have unsaved changes
            if (CurrentPageName == "Settings" && SettingsViewModel.HasUnsavedChanges)
            {
                var result = MessageBoxHelper.Show(
                    "You have unsaved changes. Do you want to leave without saving?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                return result == MessageBoxResult.Yes;
            }
            return true;
        }

        // Public method for navigation from external services (like TrayIcon)
        public void NavigateTo(string pageName)
        {
            switch (pageName.ToLower())
            {
                case "home":
                    NavigateToHome();
                    break;
                case "installer":
                    NavigateToInstaller();
                    break;
                case "library":
                    NavigateToLibrary();
                    break;
                case "store":
                    NavigateToStore();
                    break;
                case "downloads":
                    NavigateToDownloads();
                    break;
                case "tools":
                    NavigateToTools();
                    break;
                case "settings":
                    NavigateToSettings();
                    break;
                case "support":
                    NavigateToSupport();
                    break;
            }
        }

        [RelayCommand]
        private void NavigateToHome()
        {
            if (!CanNavigateAway()) return;

            CurrentPage = GetOrCreateView("Home", () => new HomePage { DataContext = HomeViewModel });
            CurrentPageName = "Home";
            HomeViewModel.RefreshMode();
        }

        [RelayCommand]
        private void NavigateToInstaller()
        {
            if (!CanNavigateAway()) return;

            CurrentPage = GetOrCreateView("Installer", () => new LuaInstallerPage { DataContext = LuaInstallerViewModel });
            CurrentPageName = "Installer";

        }

        [RelayCommand]
        private void NavigateToLibrary()
        {
            if (!CanNavigateAway()) return;

            CurrentPage = GetOrCreateView("Library", () => new LibraryPage { DataContext = LibraryViewModel });
            CurrentPageName = "Library";
            
            _ = LibraryViewModel.RefreshLibrary(true);
        }

        [RelayCommand]
        private void NavigateToStore()
        {
            if (!CanNavigateAway()) return;

            CurrentPage = GetOrCreateView("Store", () => new StorePage { DataContext = StoreViewModel });
            CurrentPageName = "Store";
            // Check API key when navigating to Store
            StoreViewModel.OnNavigatedTo();
        }

        [RelayCommand]
        private void NavigateToDownloads()
        {
            if (!CanNavigateAway()) return;

            CurrentPage = GetOrCreateView("Downloads", () => new DownloadsPage { DataContext = DownloadsViewModel });
            CurrentPageName = "Downloads";
        }

        [RelayCommand]
        private void NavigateToTools()
        {
            if (!CanNavigateAway()) return;

            CurrentPage = GetOrCreateView("Tools", () => new ToolsPage { DataContext = ToolsViewModel });
            CurrentPageName = "Tools";
        }

        [RelayCommand]
        private void NavigateToSettings()
        {
            if (!CanNavigateAway()) return;

            CurrentPage = GetOrCreateView("Settings", () => new SettingsPage { DataContext = SettingsViewModel });
            CurrentPageName = "Settings";
        }

        [RelayCommand]
        private void NavigateToSupport()
        {
            if (!CanNavigateAway()) return;

            CurrentPage = GetOrCreateView("Support", () => new SupportPage { DataContext = SupportViewModel });
            CurrentPageName = "Support";
        }

        [RelayCommand]
        private void MinimizeWindow(Window window)
        {
            window.WindowState = WindowState.Minimized;
        }

        [RelayCommand]
        private void MaximizeWindow(Window window)
        {
            window.WindowState = window.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        [RelayCommand]
        private void CloseWindow(Window window)
        {
            window.Close();
        }

        [RelayCommand]
        private void RestartSteam()
        {
            try
            {
                _steamService.RestartSteam();
                _notificationService.ShowSuccess("Steam is restarting...");
            }
            catch (Exception ex)
            {
                _notificationService.ShowError($"Failed to restart Steam: {ex.Message}");
            }
        }

        public async void CheckForUpdates()
        {
            var (hasUpdate, updateInfo) = await _updateService.CheckForUpdatesAsync();
            if (hasUpdate && updateInfo != null)
            {
                var result = MessageBoxHelper.Show(
                    $"A new version ({updateInfo.TagName}) is available!\n\nWould you like to download and install it now?\n\nCurrent version: {_updateService.GetCurrentVersion()}",
                    "Update Available",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes)
                {
                    await DownloadAndInstallUpdateAsync(updateInfo);
                }
            }
            else
            {
                MessageBoxHelper.Show(
                    "You are running the latest version!",
                    "No Updates Available",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private async Task DownloadAndInstallUpdateAsync(UpdateInfo updateInfo)
        {
            try
            {
                await _updateService.DownloadAndInstallWithDialogAsync(updateInfo);
            }
            catch (Exception ex)
            {
                _notificationService.ShowError($"Failed to download update: {ex.Message}");
            }
        }
    }
}
