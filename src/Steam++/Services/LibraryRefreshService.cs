using System;

namespace SteamPP.Services
{
    /// <summary>
    /// Singleton service to coordinate library updates across the app
    /// </summary>
    public class LibraryRefreshService
    {
        // Event fired when a game is installed and should be added to the library
        public event EventHandler<GameInstalledEventArgs>? GameInstalled;

        // Event fired when a GreenLuma game is installed
        public event EventHandler<GameInstalledEventArgs>? GreenLumaGameInstalled;

        // Event fired when a game is uninstalled
        public event EventHandler<string>? GameUninstalled;

        public void NotifyGameInstalled(string appId, bool isGreenLuma = false)
        {
            if (isGreenLuma)
            {
                GreenLumaGameInstalled?.Invoke(this, new GameInstalledEventArgs(appId));
            }
            else
            {
                GameInstalled?.Invoke(this, new GameInstalledEventArgs(appId));
            }
        }

        public void NotifyGameUninstalled(string appId)
        {
            GameUninstalled?.Invoke(this, appId);
        }
    }

    public class GameInstalledEventArgs : EventArgs
    {
        public string AppId { get; }

        public GameInstalledEventArgs(string appId)
        {
            AppId = appId;
        }
    }
}
