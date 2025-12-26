namespace SteamPP.Models
{
    public struct SpeedSample
    {
        public long NetworkSpeed { get; set; }
        public long DiskSpeed { get; set; }

        public SpeedSample(long networkSpeed, long diskSpeed)
        {
            NetworkSpeed = networkSpeed;
            DiskSpeed = diskSpeed;
        }
    }
}
