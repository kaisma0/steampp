using System.Text.Json;
using System.Text.Json.Serialization;
using SteamPP.Helpers;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace SteamPP.Services
{
    public class UpdateInfo
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("body")]
        public string Body { get; set; } = string.Empty;

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; } = string.Empty;

        [JsonPropertyName("published_at")]
        public DateTime PublishedAt { get; set; }

        [JsonPropertyName("assets")]
        public UpdateAsset[] Assets { get; set; } = Array.Empty<UpdateAsset>();
    }

    public class UpdateAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = string.Empty;

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }

    public class UpdateService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly NotificationService _notificationService;
        private const string GitHubApiUrl = "https://api.github.com/repos/{owner}/{repo}/releases/latest";
        private const string Owner = "kaisma0";
        private const string Repo = "steampp";

        public UpdateService(IHttpClientFactory httpClientFactory, NotificationService notificationService)
        {
            _httpClientFactory = httpClientFactory;
            _notificationService = notificationService;
        }

        private HttpClient CreateClient()
        {
            var client = _httpClientFactory.CreateClient("Default");
            if (!client.DefaultRequestHeaders.Contains("User-Agent"))
            {
                client.DefaultRequestHeaders.Add("User-Agent", "SteamPP");
            }
            return client;
        }


        public string GetCurrentVersion()
        {
            // Use InformationalVersion to preserve string formatting (e.g. "01" vs "1")
            var version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;

            return version?.Split('+')[0] ?? "1.0.0";
        }

        public async Task<(bool hasUpdate, UpdateInfo? updateInfo)> CheckForUpdatesAsync()
        {
            try
            {
                var client = CreateClient();
                var url = GitHubApiUrl.Replace("{owner}", Owner).Replace("{repo}", Repo);
                var response = await client.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    return (false, null);
                }

                var json = await response.Content.ReadAsStringAsync();
                var updateInfo = JsonSerializer.Deserialize<UpdateInfo>(json, JsonHelper.Options);

                if (updateInfo == null)
                {
                    return (false, null);
                }

                var currentVersion = GetCurrentVersion();
                var latestVersion = updateInfo.TagName.TrimStart('v');

                var hasUpdate = CompareVersions(currentVersion, latestVersion) < 0;

                return (hasUpdate, updateInfo);
            }
            catch
            {
                return (false, null);
            }
        }

        public async Task<string?> DownloadUpdateAsync(UpdateInfo updateInfo, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            try
            {
                // Find the Steam++.exe asset (no longer zipped)
                var exeAsset = updateInfo.Assets
                    .FirstOrDefault(a => a.Name.Equals("Steam++.exe", StringComparison.OrdinalIgnoreCase));

                if (exeAsset == null)
                {
                    // Fallback: try to find any zip file for backward compatibility
                    var zipAsset = updateInfo.Assets
                        .FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

                    if (zipAsset != null)
                    {
                        return await DownloadUpdateFromZipAsync(zipAsset, progress, cancellationToken);
                    }

                    return null;
                }

                var tempExePath = Path.Combine(Path.GetTempPath(), "Steam++_Update.exe");

                // Download EXE directly
                var client = CreateClient();
                using (var response = await client.GetAsync(exeAsset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                {
                    response.EnsureSuccessStatusCode();

                    var totalBytes = response.Content.Headers.ContentLength ?? 0;
                    var downloadedBytes = 0L;

                    using var fileStream = new FileStream(tempExePath, FileMode.Create, FileAccess.Write, FileShare.None);
                    using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);

                    var buffer = new byte[8192];
                    int bytesRead;

                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                        downloadedBytes += bytesRead;

                        if (totalBytes > 0 && progress != null)
                        {
                            var progressPercent = (double)downloadedBytes / totalBytes * 100;
                            progress.Report(progressPercent);
                        }
                    }
                }

                return tempExePath;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch
            {
                return null;
            }
        }

        // Fallback method for backward compatibility with old zip releases
        private async Task<string?> DownloadUpdateFromZipAsync(UpdateAsset zipAsset, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            try
            {
                var tempZipPath = Path.Combine(Path.GetTempPath(), "SteamPP_Update.zip");
                var tempExtractPath = Path.Combine(Path.GetTempPath(), "SteamPP_Update_Extract");

                // Download ZIP
                var client = CreateClient();
                using (var response = await client.GetAsync(zipAsset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                {
                    response.EnsureSuccessStatusCode();

                    var totalBytes = response.Content.Headers.ContentLength ?? 0;
                    var downloadedBytes = 0L;

                    using var fileStream = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);

                    var buffer = new byte[8192];
                    int bytesRead;

                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                        downloadedBytes += bytesRead;

                        if (totalBytes > 0 && progress != null)
                        {
                            var progressPercent = (double)downloadedBytes / totalBytes * 100;
                            progress.Report(progressPercent);
                        }
                    }
                }

                // Extract ZIP
                if (Directory.Exists(tempExtractPath))
                {
                    Directory.Delete(tempExtractPath, true);
                }
                Directory.CreateDirectory(tempExtractPath);

                ZipFile.ExtractToDirectory(tempZipPath, tempExtractPath);

                // Find the exe in extracted files
                var exePath = Directory.GetFiles(tempExtractPath, "Steam++.exe", SearchOption.AllDirectories).FirstOrDefault();

                if (string.IsNullOrEmpty(exePath))
                {
                    return null;
                }

                // Move exe to final temp location
                var finalExePath = Path.Combine(Path.GetTempPath(), "Steam++_Update.exe");
                if (File.Exists(finalExePath))
                {
                    File.Delete(finalExePath);
                }
                File.Move(exePath, finalExePath);

                // Cleanup
                File.Delete(tempZipPath);
                Directory.Delete(tempExtractPath, true);

                return finalExePath;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch
            {
                return null;
            }
        }

        public void InstallUpdate(string updatePath)
        {
            try
            {
                var currentExePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(currentExePath))
                    return;

                // Create a batch script to replace the exe after the app closes
                var batchPath = Path.Combine(Path.GetTempPath(), "update_steampp.bat");
                var batchContent = $@"
@echo off
timeout /t 2 /nobreak > nul
del ""{currentExePath}""
move /y ""{updatePath}"" ""{currentExePath}""
start """" ""{currentExePath}""
del ""{batchPath}""
";

                File.WriteAllText(batchPath, batchContent);

                // Start the batch file and exit
                Process.Start(new ProcessStartInfo
                {
                    FileName = batchPath,
                    CreateNoWindow = true,
                    UseShellExecute = false
                });

                System.Windows.Application.Current.Shutdown();
            }
            catch
            {
                // Failed to install update
            }
        }

        public enum UpdateResult
        {
            Success,
            Cancelled,
            Failed
        }

        public async Task<UpdateResult> DownloadAndInstallWithDialogAsync(UpdateInfo updateInfo, System.Windows.Window? owner = null)
        {
            var progressDialog = new Views.Dialogs.UpdateProgressDialog();
            progressDialog.Owner = owner ?? System.Windows.Application.Current.MainWindow;
            progressDialog.SetVersion(updateInfo.TagName);

            var cts = new CancellationTokenSource();
            progressDialog.SetCancellationTokenSource(cts);

            var progress = new Progress<double>(percent =>
            {
                progressDialog.UpdateProgress(percent);
            });

            progressDialog.Show();

            try
            {
                var updatePath = await DownloadUpdateAsync(updateInfo, progress, cts.Token);

                if (!string.IsNullOrEmpty(updatePath))
                {
                    progressDialog.SetCompleted();
                    progressDialog.Close();

                    var result = MessageBoxHelper.Show(
                        "Update downloaded successfully!\n\nThe app will now restart to install the update.",
                        "Update Ready",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information,
                        forceShow: true);

                    InstallUpdate(updatePath);
                    return UpdateResult.Success;
                }
                else if (progressDialog.WasCancelled)
                {
                    progressDialog.Close();
                    _notificationService.ShowNotification("Update Cancelled", "The update download was cancelled.", NotificationType.Info);
                    return UpdateResult.Cancelled;
                }
                else
                {
                    progressDialog.SetError("Download failed");
                    await Task.Delay(1500);
                    progressDialog.Close();
                    _notificationService.ShowError("Failed to download update. Please try again later.", "Update Failed");
                    return UpdateResult.Failed;
                }
            }
            catch (OperationCanceledException)
            {
                progressDialog.Close();
                _notificationService.ShowNotification("Update Cancelled", "The update download was cancelled.", NotificationType.Info);
                return UpdateResult.Cancelled;
            }
            catch
            {
                progressDialog.Close();
                _notificationService.ShowError("An error occurred while updating. Please try again later.", "Update Error");
                return UpdateResult.Failed;
            }
        }

        private int CompareVersions(string current, string latest)
        {
            try
            {
                var currentParts = current.Split('.').Select(int.Parse).ToArray();
                var latestParts = latest.Split('.').Select(int.Parse).ToArray();

                var maxLength = Math.Max(currentParts.Length, latestParts.Length);

                for (int i = 0; i < maxLength; i++)
                {
                    var currentPart = i < currentParts.Length ? currentParts[i] : 0;
                    var latestPart = i < latestParts.Length ? latestParts[i] : 0;

                    if (currentPart < latestPart)
                        return -1;
                    if (currentPart > latestPart)
                        return 1;
                }

                return 0;
            }
            catch
            {
                return 0;
            }
        }
    }
}
