using SteamPP.Helpers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamPP.Models;
using SteamPP.Services;
using SteamPP.Views.Dialogs;
using DepotDownloader;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace SteamPP.ViewModels
{
    public partial class DownloadsViewModel : ObservableObject, IDisposable
    {
        private bool _disposed;
        private readonly DownloadService _downloadService;
        private readonly FileInstallService _fileInstallService;
        private readonly SettingsService _settingsService;
        private readonly DepotDownloadService _depotDownloadService;
        private readonly SteamService _steamService;

        private readonly NotificationService _notificationService;
        private readonly LibraryRefreshService _libraryRefreshService;
        private readonly LoggerService _logger;

        private readonly ManifestStorageService _manifestStorageService;

        [ObservableProperty]
        private ObservableCollection<DownloadItem> _activeDownloads;

        [ObservableProperty]
        private ObservableCollection<string> _downloadedFiles = new();

        [ObservableProperty]
        private string _statusMessage = "No downloads";

        [ObservableProperty]
        private bool _isInstalling;

        public DownloadsViewModel(
            DownloadService downloadService,
            FileInstallService fileInstallService,
            SettingsService settingsService,
            DepotDownloadService depotDownloadService,
            SteamService steamService,
            NotificationService notificationService,
            LibraryRefreshService libraryRefreshService,
            ManifestStorageService manifestStorageService)
        {
            _downloadService = downloadService;
            _fileInstallService = fileInstallService;
            _settingsService = settingsService;
            _depotDownloadService = depotDownloadService;
            _steamService = steamService;
            _notificationService = notificationService;
            _libraryRefreshService = libraryRefreshService;
            _manifestStorageService = manifestStorageService;
            _logger = new LoggerService("DownloadsView");

            ActiveDownloads = _downloadService.ActiveDownloads;

            RefreshDownloadedFiles();

            // Subscribe to download completed event for auto-refresh
            _downloadService.DownloadCompleted += OnDownloadCompleted;
        }

        private void OnDownloadCompleted(object? sender, DownloadItem downloadItem)
        {
            Application.Current.Dispatcher.Invoke(async () =>
            {
                // Auto-refresh the downloaded files list when a download completes
                RefreshDownloadedFiles();

                // Skip auto-install for DepotDownloader mode (files are downloaded directly, not as zip)
                if (downloadItem.IsDepotDownloaderMode)
                {
                    return;
                }

                // Always run install flow after API download completes (if setting enabled)
                var settings = _settingsService.LoadSettings();
                var destPath = downloadItem.DestinationPath;
                var exists = !string.IsNullOrEmpty(destPath) && File.Exists(destPath);

                if (settings.AutoInstallAfterDownload && exists)
                {
                    await InstallFileInternal(destPath, isAutoInstall: true);
                }
            });
        }

        [RelayCommand]
        private void RefreshDownloadedFiles()
        {
            var settings = _settingsService.LoadSettings();

            if (string.IsNullOrEmpty(settings.DownloadsPath) || !Directory.Exists(settings.DownloadsPath))
            {
                DownloadedFiles.Clear();
                StatusMessage = "No downloads folder configured";
                return;
            }

            try
            {
                var files = Directory.GetFiles(settings.DownloadsPath, "*.zip")
                    .OrderByDescending(f => File.GetCreationTime(f))
                    .ToList();

                DownloadedFiles = new ObservableCollection<string>(files);
                StatusMessage = files.Count > 0 ? $"{files.Count} file(s) ready to install" : "No downloaded files";
            }
            catch (System.Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
        }

        [RelayCommand]
        private async Task InstallFile(string filePath)
        {
            await InstallFileInternal(filePath, isAutoInstall: false);
        }

        private async Task InstallFileInternal(string filePath, bool isAutoInstall)
        {
            if (IsInstalling)
            {
                if (!isAutoInstall)
                {
                    MessageBoxHelper.Show(
                        "Another installation is in progress",
                        "Please Wait",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    _logger.Warning($"[AutoInstall] Skiped installation for {Path.GetFileName(filePath)} because another installation is in progress");
                }
                return;
            }

            IsInstalling = true;
            var fileName = Path.GetFileName(filePath);
            StatusMessage = $"Installing {fileName}...";

            try
            {
                var settings = _settingsService.LoadSettings();
                var appId = Path.GetFileNameWithoutExtension(filePath);

                // Handle DepotDownloader mode
                if (settings.Mode == ToolMode.DepotDownloader)
                {
                    _logger.Info("=== Starting DepotDownloader Info Gathering Phase ===");
                    _logger.Info($"App ID: {appId}");
                    _logger.Info($"Zip file: {fileName}");

                    // DepotDownloader flow: extract depot keys, filter by language, and start download
                    StatusMessage = "Extracting depot information from lua file...";
                    _logger.Info("Step 1: Extracting lua content from zip file...");
                    var luaContent = _downloadService.ExtractLuaContentFromZip(filePath, appId);
                    _logger.Info($"Lua content extracted successfully ({luaContent.Length} characters)");

                    // Extract PICS tokens from lua content and set in TokenCFG for DepotDownloader
                    var luaParserForTokens = new LuaParser();
                    var picsTokens = luaParserForTokens.ParseTokens(luaContent);
                    if (picsTokens.Count > 0)
                    {
                        var mainToken = picsTokens.FirstOrDefault(t => t.AppId == appId);
                        if (mainToken != default && ulong.TryParse(mainToken.Token, out ulong tokenValue))
                        {
                            DepotDownloader.TokenCFG.useAppToken = true;
                            DepotDownloader.TokenCFG.appToken = tokenValue;
                            _logger.Info($"Set PICS token for app {appId}: {tokenValue}");
                        }
                    }

                    // Parse depot keys from lua content
                    _logger.Info("Step 2: Parsing depot keys from lua content...");
                    var depotFilterService = new DepotFilterService(new LoggerService("DepotFilter"));
                    var parsedDepotKeys = depotFilterService.ExtractDepotKeysFromLua(luaContent);

                    if (parsedDepotKeys.Count == 0)
                    {
                        _logger.Error("No depot keys found in lua file!");
                        MessageBoxHelper.Show(
                            "No depot keys found in the lua file. Cannot proceed with download.",
                            "Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        StatusMessage = "Installation cancelled - No depot keys found";
                        IsInstalling = false;
                        return;
                    }

                    _logger.Info($"Found {parsedDepotKeys.Count} depot keys:");
                    foreach (var kvp in parsedDepotKeys)
                    {
                        _logger.Info($"  Depot {kvp.Key}: {kvp.Value}");
                    }

                    StatusMessage = $"Found {parsedDepotKeys.Count} depot keys. Fetching depot metadata...";

                    // Fetch depot metadata directly from Steam using SteamKit2
                    _logger.Info("Step 3: Fetching depot metadata directly from Steam...");
                    var steamKitService = new SteamKitAppInfoService();

                    StatusMessage = "Connecting to Steam...";
                    var initResult = await steamKitService.InitializeAsync();
                    if (!initResult)
                    {
                        _logger.Error("Failed to initialize Steam connection!");
                        MessageBoxHelper.Show(
                            "Failed to connect to Steam. Please check your internet connection and try again.\n\nNote: This requires a connection to Steam's servers.",
                            "Steam Connection Failed",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        StatusMessage = "Installation cancelled - Steam connection failed";
                        IsInstalling = false;
                        return;
                    }

                    StatusMessage = "Fetching depot metadata from Steam...";
                    var steamCmdData = await steamKitService.GetDepotInfoAsync(appId);

                    if (steamCmdData == null)
                    {
                        _logger.Error("Failed to fetch depot information from Steam!");
                        MessageBoxHelper.Show(
                            $"Failed to fetch depot information for app {appId} from Steam.\n\nThis could mean:\n• The app doesn't exist\n• Steam's servers are having issues\n• The app info is restricted\n\nPlease try again later.",
                            "Failed to Fetch App Info",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        StatusMessage = "Installation cancelled - App info fetch failed";
                        IsInstalling = false;
                        steamKitService.Disconnect();
                        return;
                    }

                    // Disconnect when done
                    steamKitService.Disconnect();

                    _logger.Info("Steam depot data fetched successfully");
                    if (steamCmdData.Data != null && steamCmdData.Data.ContainsKey(appId))
                    {
                        var appData = steamCmdData.Data[appId];
                        _logger.Info($"App Name: {appData.Common?.Name ?? "Unknown"}");
                        _logger.Info($"Total Depots in API data: {appData.Depots?.Count ?? 0}");
                    }

                    // Get available languages (only from depots with keys)
                    _logger.Info("Step 4: Getting available languages from SteamCMD data...");
                    var availableLanguages = depotFilterService.GetAvailableLanguages(steamCmdData, appId, parsedDepotKeys);
                    _logger.Info($"Available languages: {string.Join(", ", availableLanguages)}");

                    if (availableLanguages.Count == 0)
                    {
                        _logger.Warning("No languages found in depot metadata. Using 'all' as fallback.");
                        _notificationService.ShowWarning("No languages found in depot metadata. Using all depots.");
                        availableLanguages = new List<string> { "all" };
                    }

                    // Show language selection dialog
                    StatusMessage = "Waiting for language selection...";
                    _logger.Info("Step 5: Showing language selection dialog to user...");
                    var languageDialog = new LanguageSelectionDialog(availableLanguages);
                    var languageResult = languageDialog.ShowDialog();

                    if (languageResult != true || string.IsNullOrEmpty(languageDialog.SelectedLanguage))
                    {
                        _logger.Info("User cancelled language selection");
                        StatusMessage = "Installation cancelled";
                        IsInstalling = false;
                        return;
                    }

                    _logger.Info($"User selected language: {languageDialog.SelectedLanguage}");

                    List<string> filteredDepotIds;

                     if (languageDialog.SelectedLanguage == "All (Skip Filter)")
                    {
                        _logger.Info("User selected 'All (Skip Filter)' - using all depots from Lua file");
                        filteredDepotIds = parsedDepotKeys.Keys.ToList();
                        StatusMessage = $"Using all {filteredDepotIds.Count} depots from Lua file...";
                    }
                    else
                    {
                        StatusMessage = $"Filtering depots for language: {languageDialog.SelectedLanguage}...";
                        _logger.Info($"Step 6: Filtering depots for language '{languageDialog.SelectedLanguage}' using Python-style logic...");
                        filteredDepotIds = depotFilterService.GetDepotsForLanguage(
                            steamCmdData,
                            parsedDepotKeys,
                            languageDialog.SelectedLanguage,
                            appId);

                        if (filteredDepotIds.Count == 0)
                        {
                            _logger.Warning("No depots matched the selected language - falling back to all depots from Lua file");
                            _notificationService.ShowWarning("Language filter returned no depots. Showing all available depots from the Lua file.");
                            filteredDepotIds = parsedDepotKeys.Keys.ToList();
                        }
                    }

                    _logger.Info($"Depot list contains {filteredDepotIds.Count} depots: {string.Join(", ", filteredDepotIds)}");
                    StatusMessage = $"Found {filteredDepotIds.Count} depots. Preparing depot selection...";

                    // Parse depot names from lua content for friendly display
                    var luaParser = new LuaParser();
                    var luaDepots = luaParser.ParseDepotsFromLua(luaContent, appId);
                    var depotNameMap = luaDepots.ToDictionary(d => d.DepotId, d => d.Name);
                    _logger.Info($"Parsed {depotNameMap.Count} depot names from lua file");

                    // Convert filtered depot IDs to depot info list for selection dialog
                    _logger.Info("Step 7: Converting filtered depot IDs to depot info for selection dialog...");
                    var depotsForSelection = new List<DepotInfo>();
                    foreach (var depotIdStr in filteredDepotIds)
                    {
                        if (uint.TryParse(depotIdStr, out var depotId) && parsedDepotKeys.ContainsKey(depotIdStr))
                        {
                            // Get friendly depot name from lua, or fallback to generic name
                            string depotName = depotNameMap.TryGetValue(depotIdStr, out var name) ? name : $"Depot {depotIdStr}";
                            string depotLanguage = "";
                            long depotSize = 0;

                            if (steamCmdData.Data != null && steamCmdData.Data.TryGetValue(appId, out var appData) &&
                                appData.Depots?.TryGetValue(depotIdStr, out var depotData) == true)
                            {
                                depotLanguage = depotData.Config?.Language ?? "";

                                // Get size from manifest if available
                                if (depotData.Manifests?.TryGetValue("public", out var manifestData) == true)
                                {
                                    depotSize = manifestData.Size;
                                }
                            }

                            _logger.Debug($"  Added depot {depotId} - Language: {(string.IsNullOrEmpty(depotLanguage) ? "none (base depot)" : depotLanguage)}, Size: {depotSize} bytes");

                            depotsForSelection.Add(new DepotInfo
                            {
                                DepotId = depotIdStr,
                                Name = depotName,
                                Size = depotSize,
                                Language = depotLanguage
                            });
                        }
                    }

                    // Show depot selection dialog
                    StatusMessage = "Waiting for depot selection...";
                    _logger.Info($"Step 8: Showing depot selection dialog ({depotsForSelection.Count} depots)...");
                    var depotDialog = new DepotSelectionDialog(depotsForSelection);
                    var depotResult = depotDialog.ShowDialog();

                    if (depotResult != true || depotDialog.SelectedDepotIds.Count == 0)
                    {
                        _logger.Info("User cancelled depot selection");
                        StatusMessage = "Installation cancelled";
                        IsInstalling = false;
                        return;
                    }

                    _logger.Info($"User selected {depotDialog.SelectedDepotIds.Count} depots: {string.Join(", ", depotDialog.SelectedDepotIds)}");

                    // Prepare download path
                    var outputPath = settings.DepotDownloaderOutputPath;
                    if (string.IsNullOrEmpty(outputPath))
                    {
                        _logger.Error("DepotDownloader output path not configured!");
                        MessageBoxHelper.Show(
                            "DepotDownloader output path not configured. Please set it in Settings.",
                            "Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        StatusMessage = "Installation cancelled - Output path not set";
                        IsInstalling = false;
                        return;
                    }

                    _logger.Info($"Output path: {outputPath}");

                    // Extract manifest files from zip
                    StatusMessage = "Extracting manifest files...";
                    _logger.Info("Step 9: Extracting manifest files from zip...");
                    var manifestFiles = _downloadService.ExtractManifestFilesFromZip(filePath, appId);
                    _logger.Info($"Extracted {manifestFiles.Count} manifest files");

                    // Prepare depot list with keys and manifest files
                    _logger.Info("Step 10: Preparing depot download list with keys and manifest files...");
                    var depotsToDownload = new List<(uint depotId, string depotKey, string? manifestFile)>();
                    foreach (var selectedDepotId in depotDialog.SelectedDepotIds)
                    {
                        if (uint.TryParse(selectedDepotId, out var depotId) && parsedDepotKeys.TryGetValue(selectedDepotId, out var depotKey))
                        {
                            // Try to get the manifest file path for this depot
                            string? manifestFilePath = null;
                            if (manifestFiles.TryGetValue(selectedDepotId, out var manifestPath))
                            {
                                manifestFilePath = manifestPath;
                                _logger.Info($"  Depot {depotId}: Using manifest file {Path.GetFileName(manifestPath)}");
                            }
                            else
                            {
                                _logger.Info($"  Depot {depotId}: No manifest file (will download latest)");
                            }

                            depotsToDownload.Add((depotId, depotKey, manifestFilePath));
                        }
                    }

                    // Get game name from SteamCMD data
                    string gameName = appId;
                    if (steamCmdData.Data != null && steamCmdData.Data.TryGetValue(appId, out var gameData))
                    {
                        gameName = gameData.Common?.Name ?? appId;
                    }
                    _logger.Info($"Game name: {gameName}");

                    // Start download via DownloadService (shows in Downloads tab with progress)
                    StatusMessage = "Starting download...";
                    _logger.Info("=== Info Gathering Phase Complete ===");
                    _logger.Info($"Step 11: Starting download for {depotsToDownload.Count} depots...");
                    _logger.Info($"  App ID: {appId}");
                    _logger.Info($"  Game Name: {gameName}");
                    _logger.Info($"  Output Path: {outputPath}");
                    _logger.Info($"  Verify Files: {settings.VerifyFilesAfterDownload}");
                    _logger.Info($"  Max Concurrent Downloads: {settings.MaxConcurrentDownloads}");

                    // Start the download asynchronously
                    _ = _downloadService.DownloadViaDepotDownloaderAsync(
                        appId,
                        gameName,
                        depotsToDownload,
                        outputPath,
                        settings.VerifyFilesAfterDownload,
                        settings.MaxConcurrentDownloads
                    );

                    var gameFolderName = $"{gameName} ({appId})";
                    var gameDownloadPath = Path.Combine(outputPath, gameFolderName, gameName);
                    _logger.Info($"Download initiated successfully. Files will be downloaded to: {gameDownloadPath}");
                    _notificationService.ShowSuccess($"Download started for {gameName}!\n\nCheck the Downloads tab to monitor progress.\nFiles will be downloaded to: {gameDownloadPath}", "Download Started");

                    StatusMessage = "Download started - check progress below";

                    // Auto-delete the ZIP file after starting download (if enabled)
                    if (settings.DeleteZipAfterInstall)
                    {
                        _logger.Info($"Deleting zip file: {fileName}");
                        File.Delete(filePath);
                        RefreshDownloadedFiles();
                    }

                    IsInstalling = false;
                    return; // Skip the regular file installation flow
                }


                StatusMessage = $"Installing files...";
                await _fileInstallService.InstallFromZipAsync(filePath, message => StatusMessage = message);

                var luaContentForStorage = _downloadService.ExtractLuaContentFromZip(filePath, appId);
                var luaParserForStorage = new LuaParser();
                var manifestId = luaParserForStorage.GetPrimaryManifestId(luaContentForStorage, appId);
                var manifestIds = luaParserForStorage.ParseManifestIds(luaContentForStorage);
                var depotIdList = manifestIds.Keys.Select(k => uint.TryParse(k, out var id) ? id : 0).Where(id => id > 0).ToList();

                var installPath = _steamService.GetStPluginPath() ?? "";
                _manifestStorageService.StoreManifest(appId, appId, manifestId, installPath, depotIdList);
                _logger.Info($"Stored manifest info for {appId} with manifestId {manifestId}");

                _notificationService.ShowSuccess($"{fileName} has been installed successfully! Restart Steam for changes to take effect.", "Installation Complete");
                StatusMessage = $"{fileName} installed successfully";

                _libraryRefreshService.NotifyGameInstalled(appId);

                // Auto-delete the ZIP file (if enabled)
                if (settings.DeleteZipAfterInstall)
                {
                    File.Delete(filePath);
                    RefreshDownloadedFiles();
                }
            }
            catch (System.Exception ex)
            {
                if (!isAutoInstall)
                {
                    StatusMessage = $"Installation failed: {ex.Message}";
                    MessageBoxHelper.Show(
                        $"Failed to install {fileName}: {ex.Message}",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                else
                {
                    StatusMessage = $"Auto-install failed: {ex.Message}";
                    _logger.Error($"Auto-install failed for {fileName}: {ex.Message}");
                }
            }
            finally
            {
                IsInstalling = false;
            }
        }

        [RelayCommand]
        private void CancelDownload(DownloadItem item)
        {
            _downloadService.CancelDownload(item.Id);
            StatusMessage = $"Cancelled: {item.GameName}";
        }

        [RelayCommand]
        private void TogglePause(DownloadItem item)
        {
            _downloadService.TogglePause(item);
        }

        [RelayCommand]
        private void ClearCompleted()
        {
            _downloadService.ClearCompletedDownloads();
        }

        [RelayCommand]
        private void DeleteFile(string filePath)
        {
            var result = MessageBoxHelper.Show(
                $"Are you sure you want to delete {Path.GetFileName(filePath)}?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    File.Delete(filePath);
                    RefreshDownloadedFiles();
                    StatusMessage = "File deleted";
                }
                catch (System.Exception ex)
                {
                    MessageBoxHelper.Show(
                        $"Failed to delete file: {ex.Message}",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }

        [RelayCommand]
        private void OpenDownloadsFolder()
        {
            var settings = _settingsService.LoadSettings();

            if (!string.IsNullOrEmpty(settings.DownloadsPath) && Directory.Exists(settings.DownloadsPath))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = settings.DownloadsPath,
                    UseShellExecute = true
                });
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                _downloadService.DownloadCompleted -= OnDownloadCompleted;
            }

            _disposed = true;
        }
    }
}
