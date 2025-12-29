using SteamPP.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;

namespace SteamPP.Services
{
    public class DownloadService
    {
        private readonly HttpClient _httpClient;
        private readonly Dictionary<string, CancellationTokenSource> _downloadCancellations;
        private readonly object _collectionLock = new object();
        private readonly ManifestApiService _manifestApiService;
        private readonly LoggerService _logger;

        public ObservableCollection<DownloadItem> ActiveDownloads { get; }
        public ObservableCollection<DownloadItem> QueuedDownloads { get; }
        public ObservableCollection<DownloadItem> CompletedDownloads { get; }
        public ObservableCollection<DownloadItem> FailedDownloads { get; }

        public event EventHandler<DownloadItem>? DownloadCompleted;
        public event EventHandler<DownloadItem>? DownloadFailed;

        private bool _isProcessingQueue = false;

        public DownloadService(ManifestApiService manifestApiService)
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(30)
            };
            _downloadCancellations = new Dictionary<string, CancellationTokenSource>();
            _manifestApiService = manifestApiService;
            _logger = new LoggerService("Downloads");
            ActiveDownloads = new ObservableCollection<DownloadItem>();
            QueuedDownloads = new ObservableCollection<DownloadItem>();
            CompletedDownloads = new ObservableCollection<DownloadItem>();
            FailedDownloads = new ObservableCollection<DownloadItem>();

            // Enable collection synchronization for cross-thread access
            BindingOperations.EnableCollectionSynchronization(ActiveDownloads, _collectionLock);
            BindingOperations.EnableCollectionSynchronization(QueuedDownloads, _collectionLock);
            BindingOperations.EnableCollectionSynchronization(CompletedDownloads, _collectionLock);
            BindingOperations.EnableCollectionSynchronization(FailedDownloads, _collectionLock);
        }

        private async Task WaitForServerReady(string appId, string apiKey, DownloadItem downloadItem, CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await App.Current.Dispatcher.InvokeAsync(() =>
                    downloadItem.StatusMessage = "Checking server status...");

                _logger.Debug($"Checking status for app {appId}...");
                var status = await _manifestApiService.GetGameStatusAsync(appId, apiKey);
                _logger.Debug($"Status result: UpdateInProgress={status?.UpdateInProgress}, Status={status?.Status}");

                if (status == null || status.UpdateInProgress != true)
                {
                    _logger.Debug("Server is ready, continuing with download");
                    // Server is ready (null or false means not updating)
                    return;
                }

                _logger.Debug("Server is updating, waiting 5 seconds before next check...");
                // Server is updating, wait and poll again
                await App.Current.Dispatcher.InvokeAsync(() =>
                    downloadItem.StatusMessage = "Server updating manifest, waiting...");

                await Task.Delay(5000, cancellationToken);
            }
        }

        public async Task<string> DownloadGameAsync(Manifest manifest, string destinationFolder, string apiKey, string steamPath, List<string>? selectedDepotIds = null)
        {
            var downloadItem = new DownloadItem
            {
                AppId = manifest.AppId,
                GameName = manifest.Name,
                DownloadUrl = $"{manifest.DownloadUrl}?api_key={apiKey}",
                StartTime = DateTime.Now,
                Status = DownloadStatus.Queued,
                TotalBytes = manifest.Size
            };

            var fileName = $"{manifest.AppId}.zip";
            var filePath = Path.Combine(destinationFolder, fileName);
            downloadItem.DestinationPath = filePath;

            // Ensure directory exists
            Directory.CreateDirectory(destinationFolder);

            // Delete existing file if it exists to avoid conflicts
            if (File.Exists(filePath))
            {
                try
                {
                    File.Delete(filePath);
                }
                catch
                {
                    // If we can't delete it, it's probably locked
                    throw new Exception($"Cannot download - file {fileName} is locked by another process. Please close any programs using this file.");
                }
            }

            // Add to active downloads
            App.Current.Dispatcher.Invoke(() => ActiveDownloads.Add(downloadItem));

            var cts = new CancellationTokenSource();
            _downloadCancellations[downloadItem.Id] = cts;

            try
            {
                App.Current.Dispatcher.Invoke(() =>
                {
                    downloadItem.Status = DownloadStatus.Downloading;
                });

                // Wait for server to be ready (poll status API)
                await WaitForServerReady(manifest.AppId, apiKey, downloadItem, cts.Token);

                App.Current.Dispatcher.Invoke(() =>
                    downloadItem.StatusMessage = "Download starting...");

                // Retry logic for server timeouts
                HttpResponseMessage? response = null;
                int maxRetries = 3;
                for (int retry = 0; retry <= maxRetries; retry++)
                {
                    try
                    {
                        if (retry > 0)
                        {
                            var delay = TimeSpan.FromSeconds(Math.Pow(2, retry)); // Exponential backoff: 2s, 4s, 8s
                            App.Current.Dispatcher.Invoke(() =>
                                downloadItem.StatusMessage = $"Server timeout, retrying in {delay.TotalSeconds}s... (Attempt {retry + 1}/{maxRetries + 1})");
                            await Task.Delay(delay, cts.Token);
                        }

                        response = await _httpClient.GetAsync(downloadItem.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token);

                        // Check for Cloudflare timeout (524) or Gateway timeout (504)
                        if ((int)response.StatusCode == 524 || response.StatusCode == System.Net.HttpStatusCode.GatewayTimeout)
                        {
                            if (retry < maxRetries)
                            {
                                response?.Dispose();
                                continue; // Retry
                            }
                        }

                        response.EnsureSuccessStatusCode();
                        break; // Success, exit retry loop
                    }
                    catch (HttpRequestException) when (retry < maxRetries)
                    {
                        response?.Dispose();
                        continue; // Retry on connection errors
                    }
                }

                if (response == null)
                {
                    throw new Exception("Failed to connect to server after multiple retries");
                }

                // Download the file - wrap in scope to ensure streams are closed
                {
                    using (response)
                    {
                        var totalBytes = response.Content.Headers.ContentLength ?? manifest.Size;
                        _logger.Debug($"Download started - Total bytes: {totalBytes}");
                        await App.Current.Dispatcher.InvokeAsync(() =>
                        {
                            downloadItem.TotalBytes = totalBytes;
                            downloadItem.Progress = 0;
                            downloadItem.StatusMessage = "Downloading... 0.0%";
                        });

                        using var contentStream = await response.Content.ReadAsStreamAsync();
                        using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                        var buffer = new byte[8192];
                        long totalBytesRead = 0;
                        int bytesRead;
                        var lastUpdate = DateTime.Now;

                        _logger.Debug("Starting download loop...");
                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cts.Token)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead, cts.Token);
                            totalBytesRead += bytesRead;

                            // Throttle UI updates to every 100ms
                            var now = DateTime.Now;
                            if ((now - lastUpdate).TotalMilliseconds >= 100)
                            {
                                var currentBytesRead = totalBytesRead;
                                var progress = (double)currentBytesRead / totalBytes * 100;
                                // Progress logging removed to reduce log spam
                                _ = App.Current.Dispatcher.InvokeAsync(() =>
                                {
                                    downloadItem.DownloadedBytes = currentBytesRead;
                                    downloadItem.Progress = progress;
                                    downloadItem.StatusMessage = $"Downloading... {progress:F1}%";
                                });
                                lastUpdate = now;
                            }
                        }

                        _logger.Debug($"Download complete - Total read: {totalBytesRead} bytes");
                        // Final update to ensure we show 100%
                        await App.Current.Dispatcher.InvokeAsync(() =>
                        {
                            downloadItem.DownloadedBytes = totalBytesRead;
                            downloadItem.Progress = 100;
                            downloadItem.StatusMessage = "Download complete";
                        });
                    }
                } // Streams and response are now closed


                App.Current.Dispatcher.Invoke(() =>
                {
                    downloadItem.Status = DownloadStatus.Completed;
                    downloadItem.EndTime = DateTime.Now;
                    downloadItem.Progress = 100;
                    downloadItem.StatusMessage = "Completed";
                });

                // Move to completed collection
                App.Current.Dispatcher.Invoke(() =>
                {
                    ActiveDownloads.Remove(downloadItem);
                    CompletedDownloads.Add(downloadItem);
                });

                DownloadCompleted?.Invoke(this, downloadItem);

                return filePath;
            }
            catch (OperationCanceledException)
            {
                App.Current.Dispatcher.Invoke(() =>
                {
                    downloadItem.Status = DownloadStatus.Cancelled;
                    downloadItem.StatusMessage = "Cancelled";
                });
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
                throw;
            }
            catch (Exception ex)
            {
                App.Current.Dispatcher.Invoke(() =>
                {
                    downloadItem.Status = DownloadStatus.Failed;
                    downloadItem.StatusMessage = $"Failed: {ex.Message}";
                    downloadItem.EndTime = DateTime.Now;
                });

                // Move to failed collection
                App.Current.Dispatcher.Invoke(() =>
                {
                    ActiveDownloads.Remove(downloadItem);
                    FailedDownloads.Add(downloadItem);
                });

                DownloadFailed?.Invoke(this, downloadItem);

                throw new Exception($"Download failed: {ex.Message}", ex);
            }
            finally
            {
                _downloadCancellations.Remove(downloadItem.Id);
            }
        }

        public void CancelDownload(string downloadId)
        {
            if (_downloadCancellations.TryGetValue(downloadId, out var cts))
            {
                cts.Cancel();
            }
        }

        public void RemoveDownload(DownloadItem item)
        {
            App.Current.Dispatcher.Invoke(() => ActiveDownloads.Remove(item));
        }

        public void TogglePause(DownloadItem item)
        {
            // Only support pause for DepotDownloader for now
            if (item.IsDepotDownloaderMode)
            {
                if (item.Status == DownloadStatus.Downloading)
                {
                    App.Current.Dispatcher.Invoke(() =>
                    {
                        item.Status = DownloadStatus.Paused;
                        item.StatusMessage = "Paused";
                    });
                    DepotDownloader.ContentDownloader.Pause();
                }
                else if (item.Status == DownloadStatus.Paused)
                {
                    App.Current.Dispatcher.Invoke(() =>
                    {
                        item.Status = DownloadStatus.Downloading;
                        item.StatusMessage = "Resuming...";
                    });
                    DepotDownloader.ContentDownloader.Resume();
                }
            }
        }

        public void ClearCompletedDownloads()
        {
            var completed = CompletedDownloads.ToList();
            foreach (var item in completed)
            {
                App.Current.Dispatcher.Invoke(() => CompletedDownloads.Remove(item));
            }
        }

        public void AddToQueue(Manifest manifest, string destinationFolder, string apiKey, string steamPath)
        {
            var downloadItem = new DownloadItem
            {
                AppId = manifest.AppId,
                GameName = manifest.Name,
                DownloadUrl = $"{manifest.DownloadUrl}?api_key={apiKey}",
                StartTime = DateTime.Now,
                Status = DownloadStatus.Queued,
                TotalBytes = manifest.Size
            };

            var fileName = $"{manifest.AppId}.zip";
            downloadItem.DestinationPath = Path.Combine(destinationFolder, fileName);

            App.Current.Dispatcher.Invoke(() => QueuedDownloads.Add(downloadItem));

            // Start processing queue if not already running
            if (!_isProcessingQueue)
            {
                Task.Run(() => ProcessQueue(destinationFolder, apiKey, steamPath));
            }
        }

        private async Task ProcessQueue(string destinationFolder, string apiKey, string steamPath)
        {
            _isProcessingQueue = true;

            while (QueuedDownloads.Count > 0)
            {
                DownloadItem? item = null;
                App.Current.Dispatcher.Invoke(() =>
                {
                    if (QueuedDownloads.Count > 0)
                    {
                        item = QueuedDownloads[0];
                        QueuedDownloads.RemoveAt(0);
                        ActiveDownloads.Add(item);
                    }
                });

                if (item != null)
                {
                    // Extract manifest info from the download item
                    var manifest = new Manifest
                    {
                        AppId = item.AppId,
                        Name = item.GameName,
                        DownloadUrl = item.DownloadUrl.Replace($"?api_key={apiKey}", ""),
                        Size = item.TotalBytes
                    };

                    try
                    {
                        await DownloadGameAsync(manifest, destinationFolder, apiKey, steamPath);
                    }
                    catch
                    {
                        // Error already handled in DownloadGameAsync
                    }
                }
            }

            _isProcessingQueue = false;
        }

        public void RemoveFromQueue(DownloadItem item)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                QueuedDownloads.Remove(item);
                ActiveDownloads.Remove(item);
                CompletedDownloads.Remove(item);
                FailedDownloads.Remove(item);
            });
        }

        /// <summary>
        /// Downloads the game zip file without extracting it
        /// </summary>
        public async Task<string> DownloadGameFileOnlyAsync(Manifest manifest, string destinationFolder, string apiKey)
        {
            var downloadItem = new DownloadItem
            {
                AppId = manifest.AppId,
                GameName = manifest.Name,
                DownloadUrl = $"{manifest.DownloadUrl}?api_key={apiKey}",
                StartTime = DateTime.Now,
                Status = DownloadStatus.Queued,
                TotalBytes = manifest.Size
            };

            var fileName = $"{manifest.AppId}.zip";
            var filePath = Path.Combine(destinationFolder, fileName);
            downloadItem.DestinationPath = filePath;

            // Ensure directory exists
            Directory.CreateDirectory(destinationFolder);

            // Delete existing file if it exists to avoid conflicts
            if (File.Exists(filePath))
            {
                try
                {
                    File.Delete(filePath);
                }
                catch
                {
                    throw new Exception($"Cannot download - file {fileName} is locked by another process. Please close any programs using this file.");
                }
            }

            // Add to active downloads
            App.Current.Dispatcher.Invoke(() => ActiveDownloads.Add(downloadItem));

            var cts = new CancellationTokenSource();
            _downloadCancellations[downloadItem.Id] = cts;

            try
            {
                await App.Current.Dispatcher.InvokeAsync(() =>
                {
                    downloadItem.Status = DownloadStatus.Downloading;
                });

                // Wait for server to be ready (poll status API)
                await WaitForServerReady(manifest.AppId, apiKey, downloadItem, cts.Token);

                await App.Current.Dispatcher.InvokeAsync(() =>
                    downloadItem.StatusMessage = "Download starting...");

                // Download the file with 5-second response timeout
                _logger.Debug($"FileOnly: Requesting download: {downloadItem.DownloadUrl}");

                var downloadCts = new CancellationTokenSource();
                downloadCts.CancelAfter(5000); // 5 second timeout for initial response

                HttpResponseMessage? response = null;
                try
                {
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, downloadCts.Token);
                    response = await _httpClient.GetAsync(downloadItem.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token);
                }
                catch (OperationCanceledException) when (downloadCts.IsCancellationRequested && !cts.IsCancellationRequested)
                {
                    // Download request timed out after 5 seconds, check status again
                    _logger.Debug("FileOnly: Download request timed out, checking status...");

                    try
                    {
                        var status = await _manifestApiService.GetGameStatusAsync(manifest.AppId, apiKey);
                        _logger.Debug($"FileOnly: Status check result: UpdateInProgress={status?.UpdateInProgress}, Status={status?.Status}");

                        if (status?.UpdateInProgress == true)
                        {
                            _logger.Debug("FileOnly: Server is updating, going back to polling...");
                            // Server is still updating, go back to polling
                            await WaitForServerReady(manifest.AppId, apiKey, downloadItem, cts.Token);

                            await App.Current.Dispatcher.InvokeAsync(() =>
                                downloadItem.StatusMessage = "Download starting...");

                            _logger.Debug("FileOnly: Retrying download after waiting...");
                            // Try download again
                            response = await _httpClient.GetAsync(downloadItem.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                        }
                        else
                        {
                            _logger.Debug("FileOnly: Server not updating, but timeout occurred - retrying with longer timeout...");
                            // Server not updating, just retry with 30-second timeout
                            var retryCts = new CancellationTokenSource();
                            retryCts.CancelAfter(30000); // 30 second timeout
                            using var retryLinkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, retryCts.Token);
                            response = await _httpClient.GetAsync(downloadItem.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, retryLinkedCts.Token);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug($"FileOnly: Status check failed: {ex.Message}");
                        throw;
                    }
                }

                using (response)
                {
                    response.EnsureSuccessStatusCode();

                    var totalBytes = response.Content.Headers.ContentLength ?? manifest.Size;
                    _logger.Debug($"FileOnly: Download started - Total bytes: {totalBytes}");
                    await App.Current.Dispatcher.InvokeAsync(() =>
                    {
                        downloadItem.TotalBytes = totalBytes;
                        downloadItem.Progress = 0;
                        downloadItem.StatusMessage = "Downloading... 0.0%";
                    });

                    using var contentStream = await response.Content.ReadAsStreamAsync();
                    using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                    var buffer = new byte[8192];
                    long totalBytesRead = 0;
                    int bytesRead;
                    var lastUpdate = DateTime.Now;

                    _logger.Debug("FileOnly: Starting download loop...");
                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cts.Token)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead, cts.Token);
                        totalBytesRead += bytesRead;

                        // Throttle UI updates to every 100ms
                        var now = DateTime.Now;
                        if ((now - lastUpdate).TotalMilliseconds >= 100)
                        {
                            var currentBytesRead = totalBytesRead;
                            var progress = (double)currentBytesRead / totalBytes * 100;
                            // Progress logging removed to reduce log spam
                            _ = App.Current.Dispatcher.InvokeAsync(() =>
                            {
                                downloadItem.DownloadedBytes = currentBytesRead;
                                downloadItem.Progress = progress;
                                downloadItem.StatusMessage = $"Downloading... {progress:F1}%";
                            });
                            lastUpdate = now;
                        }
                    }

                    _logger.Debug($"FileOnly: Download complete - Total read: {totalBytesRead} bytes");
                    // Final update to ensure we show 100%
                    await App.Current.Dispatcher.InvokeAsync(() =>
                    {
                        downloadItem.DownloadedBytes = totalBytesRead;
                        downloadItem.Progress = 100;
                        downloadItem.StatusMessage = "Download complete";
                    });
                }

                await App.Current.Dispatcher.InvokeAsync(() =>
                {
                    downloadItem.Status = DownloadStatus.Completed;
                    downloadItem.EndTime = DateTime.Now;
                    downloadItem.Progress = 100;
                    downloadItem.StatusMessage = "Download Complete - Ready for depot selection";
                });

                // Move to completed collection
                await App.Current.Dispatcher.InvokeAsync(() =>
                {
                    ActiveDownloads.Remove(downloadItem);
                    CompletedDownloads.Add(downloadItem);
                });

                DownloadCompleted?.Invoke(this, downloadItem);

                return filePath;
            }
            catch (Exception ex)
            {
                App.Current.Dispatcher.Invoke(() =>
                {
                    downloadItem.Status = DownloadStatus.Failed;
                    downloadItem.StatusMessage = $"Failed: {ex.Message}";
                    ActiveDownloads.Remove(downloadItem);
                    FailedDownloads.Add(downloadItem);
                });

                DownloadFailed?.Invoke(this, downloadItem);
                throw;
            }
            finally
            {
                _downloadCancellations.Remove(downloadItem.Id);
            }
        }

        /// <summary>
        /// Extracts and reads the lua file content from a downloaded zip
        /// </summary>
        public string ExtractLuaContentFromZip(string zipFilePath, string appId)
        {
            var luaFileName = $"{appId}.lua";

            using var archive = ZipFile.OpenRead(zipFilePath);
            var luaEntry = archive.Entries.FirstOrDefault(e =>
                e.FullName.Equals(luaFileName, StringComparison.OrdinalIgnoreCase) ||
                e.Name.Equals(luaFileName, StringComparison.OrdinalIgnoreCase));

            if (luaEntry == null)
            {
                throw new Exception($"Lua file '{luaFileName}' not found in zip archive.");
            }

            using var stream = luaEntry.Open();
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        /// <summary>
        /// Extracts manifest files from the zip to a temporary directory
        /// Returns a dictionary mapping depotId to manifest file path
        /// </summary>
        public Dictionary<string, string> ExtractManifestFilesFromZip(string zipFilePath, string appId)
        {
            var manifestFiles = new Dictionary<string, string>();
            var tempDir = Path.Combine(Path.GetTempPath(), $"SteamPPManifests_{appId}_{Guid.NewGuid()}");
            Directory.CreateDirectory(tempDir);

            using var archive = ZipFile.OpenRead(zipFilePath);
            foreach (var entry in archive.Entries)
            {
                // Look for .manifest files
                if (entry.Name.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase))
                {
                    // Extract manifest file to temp directory
                    var destPath = Path.Combine(tempDir, entry.Name);
                    entry.ExtractToFile(destPath, true);

                    // Try to extract depot ID from filename (format: depotId_manifestId.manifest)
                    var fileNameWithoutExt = Path.GetFileNameWithoutExtension(entry.Name);
                    var parts = fileNameWithoutExt.Split('_');
                    if (parts.Length >= 1 && uint.TryParse(parts[0], out var depotId))
                    {
                        manifestFiles[parts[0]] = destPath;
                    }
                }
            }

            return manifestFiles;
        }

        public async Task<bool> DownloadViaDepotDownloaderAsync(
            string appId,
            string gameName,
            List<(uint depotId, string depotKey, string? manifestFile)> depots,
            string outputPath,
            bool verifyFiles = true,
            int maxDownloads = 8)
        {
            _logger.Info($"[DepotDownloader] Starting download for {gameName} (App ID: {appId})");
            _logger.Info($"[DepotDownloader] Depots to download: {depots.Count}");
            _logger.Info($"[DepotDownloader] Output path: {outputPath}");
            _logger.Info($"[DepotDownloader] Verify files: {verifyFiles}");
            _logger.Info($"[DepotDownloader] Max concurrent downloads: {maxDownloads}");

            // Sanitize game name to remove invalid path characters (: < > " / \ | ? *)
            var sanitizedGameName = SanitizeFileName(gameName);
            _logger.Info($"[DepotDownloader] Sanitized game name: '{gameName}' -> '{sanitizedGameName}'");

            // Create folder structure: {GameName} ({AppId})\{GameName}
            var gameFolderName = $"{sanitizedGameName} ({appId})";
            var gameDownloadPath = Path.Combine(outputPath, gameFolderName, sanitizedGameName);

            var downloadItem = new DownloadItem
            {
                AppId = appId,
                GameName = gameName,
                Status = DownloadStatus.Downloading,
                StartTime = DateTime.Now,
                StatusMessage = "Initializing Steam session...",
                DestinationPath = gameDownloadPath,
                IsDepotDownloaderMode = true,
                HeaderImageUrl = $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/header.jpg"
            };

            var cancellationTokenSource = new CancellationTokenSource();
            _downloadCancellations[downloadItem.Id] = cancellationTokenSource;

            _logger.Info($"[DepotDownloader] Adding download item to ActiveDownloads (ID: {downloadItem.Id})");
            lock (_collectionLock)
            {
                ActiveDownloads.Add(downloadItem);
            }
            _logger.Info($"[DepotDownloader] Download item added successfully. ActiveDownloads count: {ActiveDownloads.Count}");

            try
            {
                Directory.CreateDirectory(downloadItem.DestinationPath);

                var depotDownloaderService = DepotDownloaderWrapperService.Instance;

                // Subscribe to events
                EventHandler<DownloadProgressEventArgs>? progressHandler = null;
                EventHandler<DownloadStatusEventArgs>? statusHandler = null;
                EventHandler<LogMessageEventArgs>? logHandler = null;

                progressHandler = (sender, e) =>
                {
                    _ = App.Current.Dispatcher.BeginInvoke(() =>
                    {
                        downloadItem.Progress = e.Progress;
                        downloadItem.DownloadedBytes = e.DownloadedBytes;
                        downloadItem.TotalBytes = e.TotalBytes;
                        downloadItem.NetworkSpeed = e.NetworkSpeed;
                        downloadItem.DiskSpeed = e.DiskSpeed;
                        downloadItem.RecordSpeedSample();

                        downloadItem.StatusMessage = $"Downloading: {e.CurrentFile} ({e.Progress:F1}% - {e.ProcessedFiles}/{e.TotalFiles} files)";
                    });
                };

                statusHandler = (sender, e) =>
                {
                    _ = App.Current.Dispatcher.BeginInvoke(() =>
                    {
                        downloadItem.StatusMessage = e.Message;
                    });
                };

                logHandler = (sender, e) =>
                {
                    _logger.Debug($"[DepotDownloader] {e.Message}");
                };

                depotDownloaderService.ProgressChanged += progressHandler;
                depotDownloaderService.StatusChanged += statusHandler;
                depotDownloaderService.LogMessage += logHandler;

                try
                {
                    // Always use anonymous login
                    _logger.Info($"[DepotDownloader] Initializing Steam session (anonymous)...");
                    await App.Current.Dispatcher.InvokeAsync(() =>
                    {
                        downloadItem.StatusMessage = "Connecting to Steam (anonymous)...";
                    });

                    var initialized = await depotDownloaderService.InitializeAsync("", "");
                    _logger.Info($"[DepotDownloader] Steam initialization result: {initialized}");

                    if (!initialized)
                    {
                        _logger.Error($"[DepotDownloader] Steam initialization failed!");
                        throw new Exception("Failed to initialize Steam session");
                    }

                    _logger.Info($"[DepotDownloader] Steam session initialized successfully");

                    await App.Current.Dispatcher.InvokeAsync(() =>
                    {
                        downloadItem.StatusMessage = $"Downloading {depots.Count} depots...";
                    });

                    _logger.Info($"[DepotDownloader] Starting depot download...");
                    var success = await depotDownloaderService.DownloadDepotsAsync(
                        uint.Parse(appId),
                        depots,
                        downloadItem.DestinationPath,
                        verifyFiles,
                        maxDownloads,
                        cancellationTokenSource.Token
                    );
                    _logger.Info($"[DepotDownloader] Download completed with result: {success}");

                    if (success)
                    {
                        _logger.Info($"[DepotDownloader] Download successful! Moving to CompletedDownloads");
                        await App.Current.Dispatcher.InvokeAsync(() =>
                        {
                            downloadItem.Status = DownloadStatus.Completed;
                            downloadItem.Progress = 100;
                            downloadItem.StatusMessage = "Download completed successfully";
                            downloadItem.EndTime = DateTime.Now;

                            lock (_collectionLock)
                            {
                                ActiveDownloads.Remove(downloadItem);
                                CompletedDownloads.Add(downloadItem);
                            }

                            DownloadCompleted?.Invoke(this, downloadItem);
                        });

                        return true;
                    }
                    else
                    {
                        throw new Exception("Download failed");
                    }
                }
                finally
                {
                    depotDownloaderService.ProgressChanged -= progressHandler;
                    depotDownloaderService.StatusChanged -= statusHandler;
                    depotDownloaderService.LogMessage -= logHandler;
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"[DepotDownloader] Download failed for {gameName} (App ID: {appId})");
                _logger.Error($"[DepotDownloader] Exception: {ex.GetType().Name} - {ex.Message}");
                _logger.Error($"[DepotDownloader] Stack trace: {ex.StackTrace}");

                if (ex.InnerException != null)
                {
                    _logger.Error($"[DepotDownloader] Inner exception: {ex.InnerException.GetType().Name} - {ex.InnerException.Message}");
                }

                await App.Current.Dispatcher.InvokeAsync(() =>
                {
                    downloadItem.Status = DownloadStatus.Failed;
                    downloadItem.StatusMessage = $"Failed: {ex.Message}";
                    downloadItem.EndTime = DateTime.Now;

                    lock (_collectionLock)
                    {
                        ActiveDownloads.Remove(downloadItem);
                        FailedDownloads.Add(downloadItem);
                    }

                    DownloadFailed?.Invoke(this, downloadItem);
                });

                return false;
            }
            finally
            {
                _downloadCancellations.Remove(downloadItem.Id);
            }
        }

        /// <summary>
        /// Sanitizes a file/folder name by removing invalid characters
        /// </summary>
        private string SanitizeFileName(string fileName)
        {
            // Windows invalid path characters: < > : " / \ | ? *
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = fileName;

            foreach (var c in invalidChars)
            {
                sanitized = sanitized.Replace(c.ToString(), "");
            }

            return sanitized.Trim();
        }
    }
}
