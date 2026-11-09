using System;

namespace BackloggdAutoSync
{
    public class GameMapping
    {
        public int AppId { get; set; }
        public string SwitchApplicationId { get; set; }
        public string BackloggdGameId { get; set; }
        public string SteamTitle { get; set; }
        public string BackloggdTitle { get; set; }
        public string ReleaseYear { get; set; }
        public bool ManualOverride { get; set; }
        public string LastUpdated { get; set; }
    }
}
