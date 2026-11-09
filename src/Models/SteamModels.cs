using System;

namespace BackloggdAutoSync
{
    public class InstalledSteamGame
    {
        public int AppId { get; set; }
        public string Title { get; set; }

        public override string ToString()
        {
            return string.Format("{0} (AppID: {1})", Title, AppId);
        }
    }
}
