using System;

namespace BackloggdAutoSync
{
    public class ActiveSessionCheckpoint
    {
        public int AppId { get; set; }
        public string GameTitle { get; set; }
        public string DateStr { get; set; }
        public string StartTimeIso { get; set; }
        public string LastHeartbeatIso { get; set; }
        public int ElapsedSeconds { get; set; }
    }
}
