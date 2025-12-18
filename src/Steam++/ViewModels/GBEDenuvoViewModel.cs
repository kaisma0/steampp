using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SteamPP.Services;
using SteamPP.Services.GBE;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace SteamPP.ViewModels
{
    public partial class GBEDenuvoViewModel : ObservableObject
    {
        private readonly SettingsService _settingsService;

        [ObservableProperty]
        private string _appId = string.Empty;

        [ObservableProperty]
        private string _outputPath = string.Empty;

        [ObservableProperty]
        private string _playerName = "Player";

        [ObservableProperty]
        private string _steamId = string.Empty;

        [ObservableProperty]
        private bool _isDenuvo;

        [ObservableProperty]
        private bool _useCustomToken;

        [ObservableProperty]
        private string _customTokenSteamId = string.Empty;

        [ObservableProperty]
        private string _customToken = string.Empty;

        public GBEDenuvoViewModel()
        {
            _settingsService = App.Current.Services.GetRequiredService<SettingsService>();
            var settings = _settingsService.LoadSettings();

            // Load saved path or use Desktop as default
            OutputPath = !string.IsNullOrEmpty(settings.GBETokenOutputPath)
                ? settings.GBETokenOutputPath
                : Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

            // Load saved player name and steam id
            PlayerName = !string.IsNullOrEmpty(settings.GBEPlayerName) ? settings.GBEPlayerName : "Player";
            SteamId = settings.GBESteamId;
        }

        [ObservableProperty]
        private string _logOutput = "Ready to generate GBE configuration.\n";

        [ObservableProperty]
        private bool _isGenerating;

        public bool IsNotGenerating => !IsGenerating;

        partial void OnIsGeneratingChanged(bool value)
        {
            OnPropertyChanged(nameof(IsNotGenerating));
        }

        [RelayCommand]
        private void BrowseOutputPath()
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select output directory for token files",
                SelectedPath = OutputPath
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                OutputPath = dialog.SelectedPath;

                // Save to settings
                var settings = _settingsService.LoadSettings();
                settings.GBETokenOutputPath = OutputPath;
                _settingsService.SaveSettings(settings);
            }
        }

        [RelayCommand]
        private async Task GenerateToken()
        {
            if (!int.TryParse(AppId, out int appIdInt))
            {
                MessageBox.Show("Please enter a valid numeric App ID.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(OutputPath) || !Directory.Exists(OutputPath))
            {
                MessageBox.Show("Please select a valid output directory.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (UseCustomToken)
            {
                if (string.IsNullOrWhiteSpace(CustomTokenSteamId) || string.IsNullOrWhiteSpace(CustomToken))
                {
                    MessageBox.Show("Please enter both Token SteamID and Token.", "Missing Information", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

            }

            // Check for API key
            var settings = _settingsService.LoadSettings();
            if (string.IsNullOrWhiteSpace(settings.GBESteamWebApiKey))
            {
                MessageBox.Show("Please set your Steam Web API key in Settings → GBE Token Generator.\n\nYou can get one at: https://steamcommunity.com/dev/apikey",
                    "API Key Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Save player name and steam id to settings
            settings.GBEPlayerName = PlayerName;
            settings.GBESteamId = SteamId;
            _settingsService.SaveSettings(settings);

            IsGenerating = true;
            LogOutput = string.Empty;

            try
            {
                Log("Starting GBE configuration generation...");
                Log($"App ID: {appIdInt}");
                Log($"Output: {OutputPath}\n");

                string finalZipPath = Path.Combine(OutputPath, $"gbe [{appIdInt}].zip");
                var generator = new GoldbergLogic(
                    appIdInt, 
                    finalZipPath, 
                    settings.GBESteamWebApiKey, 
                    IsDenuvo, 
                    PlayerName, 
                    SteamId, 
                    UseCustomToken ? CustomToken : null,
                    UseCustomToken ? CustomTokenSteamId : null,
                    (message, isError) =>
                {
                    Application.Current.Dispatcher.Invoke(() => Log(message, isError));
                });

                bool success = await generator.GenerateAsync();

                if (success)
                {
                    Log($"\n✓ Archive created successfully at: {finalZipPath}");
                    MessageBox.Show($"GBE configuration generated successfully!\n\nSaved to: {finalZipPath}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("The operation failed. Please check the log for details.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                Log($"\nError: {ex.Message}", isError: true);
                MessageBox.Show($"An error occurred: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsGenerating = false;
            }
        }

        private void Log(string message, bool isError = false)
        {
            var sb = new StringBuilder(LogOutput);
            sb.AppendLine($"[{DateTime.Now:HH:mm:ss}] {message}");
            LogOutput = sb.ToString();
        }
    }
}
