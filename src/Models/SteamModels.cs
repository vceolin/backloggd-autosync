using System;

namespace BackloggdAutoSync
{
    public class InstalledSteamGame
    {
        public int AppId { get; set; }
        public string Title { get; set; }
        public bool IsInstalled { get; set; }
        public bool IsHidden { get; set; }

        public InstalledSteamGame()
        {
            IsInstalled = true;
        }

        public override string ToString()
        {
            return IsInstalled
                ? string.Format("{0} (AppID: {1})", Title, AppId)
                : string.Format("{0} (AppID: {1}) [Uninstalled]", Title, AppId);
        }
    }
}
