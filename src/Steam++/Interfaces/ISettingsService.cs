using SteamPP.Models;

namespace SteamPP.Interfaces
{
    public interface ISettingsService
    {
        event System.EventHandler<AppSettings>? SettingsSaved;
        AppSettings LoadSettings();
        void SaveSettings(AppSettings settings);
        void AddApiKeyToHistory(string apiKey);
    }
}
