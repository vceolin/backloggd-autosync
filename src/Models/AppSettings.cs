using System;
using System.Collections.Generic;

namespace BackloggdAutoSync
{
    public class AppSettings
    {
        public string SessionCookie { get; set; }
        public string CsrfToken { get; set; }
        public string UserId { get; set; }
        public string Username { get; set; }
        public int MinSessionSeconds { get; set; }
        public bool ShowNotifications { get; set; }
        public bool AutoSyncOnExit { get; set; }
        public bool SplitAtMidnight { get; set; }
        public bool StartMinimized { get; set; }
        public List<int> IgnoredAppIds { get; set; }

        // Nintendo Switch Integration Settings
        public string NintendoSessionToken { get; set; }
        public string NintendoStoreSessionToken { get; set; }
        public string NintendoAccountNickname { get; set; }
        public string NintendoAccountId { get; set; }
        public bool NintendoAutoSync { get; set; }
        public int NintendoSyncDaysBack { get; set; }

        public AppSettings()
        {
            SessionCookie = "";
            CsrfToken = "";
            UserId = "";
            Username = "";
            MinSessionSeconds = 60;
            ShowNotifications = true;
            AutoSyncOnExit = true;
            SplitAtMidnight = true;
            StartMinimized = false;
            NintendoSessionToken = "";
            NintendoStoreSessionToken = "";
            NintendoAccountNickname = "";
            NintendoAccountId = "";
            NintendoAutoSync = true;
            NintendoSyncDaysBack = 14;
            IgnoredAppIds = new List<int>
            {
                228980,  // Steamworks Common Redistributables
                250820,  // SteamVR
                1070560, // Steam Linux Runtime
                1391110, // Steam Linux Runtime - Soldier
                1628350, // Steam Linux Runtime - Sniper
                431960   // Wallpaper Engine
            };
        }
    }
}
