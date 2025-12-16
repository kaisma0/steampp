using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace SteamPP.Models
{
    public class Manifest : INotifyPropertyChanged
    {
        [JsonPropertyName("appid")]
        public string AppId { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0";

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("icon_url")]
        public string IconUrl { get; set; } = string.Empty;

        [JsonPropertyName("last_updated")]
        public DateTime? LastUpdated { get; set; }

        [JsonPropertyName("download_url")]
        public string DownloadUrl { get; set; } = string.Empty;

        // Non-serialized property for cached icon path
        private string? _cachedIconPath;
        public string? CachedIconPath
        {
            get => _cachedIconPath;
            set
            {
                if (_cachedIconPath != value)
                {
                    _cachedIconPath = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
