using System.Text.Json;
using System.Text.Json.Serialization;
using SteamPP.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace SteamPP.Services
{
    public class SteamCmdDepotData
    {
        [JsonPropertyName("data")]
        public Dictionary<string, AppData> Data { get; set; } = new();

        [JsonPropertyName("status")]
        public string Status { get; set; } = "";
    }

    public class AppData
    {
        [JsonPropertyName("depots")]
        public Dictionary<string, DepotData> Depots { get; set; } = new();

        [JsonPropertyName("common")]
        public CommonData Common { get; set; } = new();
    }

    public class CommonData
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";
    }

    public class DepotData
    {
        [JsonPropertyName("config")]
        public DepotConfig? Config { get; set; }

        [JsonPropertyName("manifests")]
        public Dictionary<string, ManifestData>? Manifests { get; set; }

        [JsonPropertyName("dlcappid")]
        public string? DlcAppId { get; set; }

        [JsonPropertyName("depotfromapp")]
        public string? DepotFromApp { get; set; }

        [JsonPropertyName("sharedinstall")]
        public string? SharedInstall { get; set; }
    }

    public class DepotConfig
    {
        [JsonPropertyName("oslist")]
        public string? OsList { get; set; }

        [JsonPropertyName("language")]
        public string? Language { get; set; }

        [JsonPropertyName("lowviolence")]
        public string? LowViolence { get; set; }

        [JsonPropertyName("realm")]
        public string? Realm { get; set; }
    }

    public class ManifestData
    {
        [JsonPropertyName("gid")]
        [JsonConverter(typeof(SteamPP.Helpers.NumberToStringConverter))]
        public string? Gid { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("download")]
        public long Download { get; set; }
    }

    public class SteamCmdApiService
    {
        private readonly HttpClient _httpClient;

        public SteamCmdApiService()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
        }

        public async Task<SteamCmdDepotData?> GetDepotInfoAsync(string appId)
        {
            try
            {
                var url = $"https://api.steamcmd.net/v1/info/{appId}";
                var response = await _httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<SteamCmdDepotData>(json, JsonHelper.Options);

                return data;
            }
            catch
            {
                // Failed to fetch depot info - return null to allow fallback
                return null;
            }
        }

        public string? GetGameName(SteamCmdDepotData? data, string appId)
        {
            if (data?.Data == null || !data.Data.ContainsKey(appId))
                return null;

            return data.Data[appId]?.Common?.Name;
        }
    }
}
