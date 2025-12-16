using System;
using System.Text.Json.Serialization;

namespace SteamPP.Models
{
    public class GameStatus
    {
        [JsonPropertyName("app_id")]
        public string AppId { get; set; } = string.Empty;

        [JsonPropertyName("game_name")]
        public string GameName { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("manifest_file_exists")]
        public bool? ManifestFileExists { get; set; }

        [JsonPropertyName("auto_update_enabled")]
        public bool? AutoUpdateEnabled { get; set; }

        [JsonPropertyName("update_in_progress")]
        public bool? UpdateInProgress { get; set; }

        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }

        [JsonPropertyName("file_size")]
        public long FileSize { get; set; }

        [JsonPropertyName("file_modified")]
        public DateTime? FileModified { get; set; }

        [JsonPropertyName("file_age_days")]
        public double? FileAgeDays { get; set; }

        [JsonPropertyName("needs_update")]
        public bool? NeedsUpdate { get; set; }

        [JsonPropertyName("update_reason")]
        public string UpdateReason { get; set; } = string.Empty;
    }
}
