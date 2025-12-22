using System.Text.Json.Serialization;
using System;
using System.Collections.Generic;

namespace SteamPP.Models
{
    public class LibraryResponse
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("total_count")]
        public int TotalCount { get; set; }

        [JsonPropertyName("limit")]
        public int Limit { get; set; }

        [JsonPropertyName("offset")]
        public int Offset { get; set; }

        [JsonPropertyName("search")]
        public string? Search { get; set; }

        [JsonPropertyName("sort_by")]
        public string SortBy { get; set; } = "updated";

        [JsonPropertyName("games")]
        public List<LibraryGame> Games { get; set; } = new();

        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }
    }

    public class LibraryGame : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
    {
        [JsonPropertyName("game_id")]
        public string GameId { get; set; } = string.Empty;

        [JsonPropertyName("game_name")]
        public string GameName { get; set; } = string.Empty;

        [JsonPropertyName("header_image")]
        public string HeaderImage { get; set; } = string.Empty;

        [JsonPropertyName("uploaded_date")]
        public DateTime UploadedDate { get; set; }

        [JsonPropertyName("manifest_available")]
        public bool ManifestAvailable { get; set; }

        [JsonPropertyName("manifest_size")]
        public long? ManifestSize { get; set; }

        [JsonPropertyName("manifest_updated")]
        public DateTime? ManifestUpdated { get; set; }

        // For UI
        private string? _cachedIconPath;
        public string? CachedIconPath
        {
            get => _cachedIconPath;
            set => SetProperty(ref _cachedIconPath, value);
        }

        private bool _isInstalled;
        public bool IsInstalled
        {
            get => _isInstalled;
            set => SetProperty(ref _isInstalled, value);
        }

        private bool _hasUpdate;
        public bool HasUpdate
        {
            get => _hasUpdate;
            set => SetProperty(ref _hasUpdate, value);
        }
    }

    public class SearchResponse
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("query")]
        public string Query { get; set; } = string.Empty;

        [JsonPropertyName("total_matches")]
        public int TotalMatches { get; set; }

        [JsonPropertyName("returned_count")]
        public int ReturnedCount { get; set; }

        [JsonPropertyName("limit")]
        public int Limit { get; set; }

        [JsonPropertyName("results")]
        public List<LibraryGame> Results { get; set; } = new();

        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }
    }
}
