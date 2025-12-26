using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SteamPP.Models
{
    public enum DownloadStatus
    {
        Queued,
        Downloading,
        Completed,
        Failed,
        Cancelled
    }

    public class DownloadItem : INotifyPropertyChanged
    {
        private double _progress;
        private DownloadStatus _status;
        private string _statusMessage = string.Empty;
        private long _downloadedBytes;
        private long _totalBytes;
        private long _networkSpeed; // Bytes/s
        private long _diskSpeed; // Bytes/s
        private long _peakNetworkSpeed; // Bytes/s

        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string AppId { get; set; } = string.Empty;
        public string GameName { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public string DestinationPath { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public bool IsDepotDownloaderMode { get; set; } = false; // If true, skip auto-install (files are downloaded directly, not as zip)

        public double Progress
        {
            get => _progress;
            set
            {
                _progress = value;
                OnPropertyChanged();
            }
        }

        public DownloadStatus Status
        {
            get => _status;
            set
            {
                _status = value;
                OnPropertyChanged();
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                _statusMessage = value;
                OnPropertyChanged();
            }
        }

        public long DownloadedBytes
        {
            get => _downloadedBytes;
            set
            {
                _downloadedBytes = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DownloadedFormatted));
            }
        }

        public long TotalBytes
        {
            get => _totalBytes;
            set
            {
                _totalBytes = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TotalFormatted));
            }
        }

        public long NetworkSpeed
        {
            get => _networkSpeed;
            set
            {
                _networkSpeed = value;
                if (_networkSpeed > _peakNetworkSpeed)
                {
                    PeakNetworkSpeed = _networkSpeed;
                }
                OnPropertyChanged();
                OnPropertyChanged(nameof(NetworkSpeedFormatted));
            }
        }

        public long PeakNetworkSpeed
        {
            get => _peakNetworkSpeed;
            set
            {
                _peakNetworkSpeed = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PeakNetworkSpeedFormatted));
            }
        }

        public long DiskSpeed
        {
            get => _diskSpeed;
            set
            {
                _diskSpeed = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DiskSpeedFormatted));
                OnPropertyChanged(nameof(DiskSpeedFormattedMatchesSteam));
            }
        }

        public string NetworkSpeedFormatted => FormatSpeed(NetworkSpeed);
        public string PeakNetworkSpeedFormatted => FormatSpeed(PeakNetworkSpeed);
        public string DiskSpeedFormatted => FormatSpeed(DiskSpeed); // Disk usage usually shown in MB/s, but user asked for Mbps? Assuming Mbps only for network. Re-reading request: "speed should be in Mbps". Implies network. Steam shows Disk in MB/s. I'll stick to Mbps for Network, MB/s for Disk to match Steam UI.

        private string FormatSpeed(long bytesPerSecond)
        {
            // Network speed in Mbps
            double mbps = (bytesPerSecond * 8.0) / 1000000.0;
            return $"{mbps:0.0} Mbps";
        }
        
        // Overload for disk speed which should technically be MB/s? User asked "speed should be in Mbps", vague if applied to disk.
        // Official Steam uses MB/s for disk. I will use MB/s for Disk to avoid confusion, and Mbps for Network.
        public string DiskSpeedFormattedMatchesSteam => FormatBytes(DiskSpeed) + "/s";

        public string DownloadedFormatted => FormatBytes(DownloadedBytes);
        public string TotalFormatted => FormatBytes(TotalBytes);

        private static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
