using System;

namespace BackloggdAutoSync
{
    public class CollectionEntry
    {
        public string Id { get; set; }
        public string Source { get; set; } // "Steam" or "Nintendo Switch"
        public int AppId { get; set; }
        public string SwitchApplicationId { get; set; }
        public string LocalTitle { get; set; }
        public bool IsInstalled { get; set; }
        public bool IsHiddenInSteam { get; set; }

        public string BackloggdGameId { get; set; }
        public string BackloggdTitle { get; set; }
        public string ReleaseYear { get; set; }
        public string BackloggdLibraryEntryId { get; set; }

        // Backloggd Library Entry fields:
        // PlatformId: 6 = Windows PC, 130 = Nintendo Switch, 508 = Nintendo Switch 2
        public int PlatformId { get; set; }
        // OwnershipId: 0 = Physical, 1 = Digital, 3 = Lost / Sold
        public int OwnershipId { get; set; }
        // StorefrontId: 1 = Steam, 8 = Nintendo eShop, null = None (e.g. Physical)
        public int? StorefrontId { get; set; }
        public string EditionId { get; set; }
        public string EditionTitle { get; set; }

        public bool Synced { get; set; }
        public string SyncError { get; set; }
        public bool Excluded { get; set; }
        public string LastUpdated { get; set; }

        public CollectionEntry()
        {
            Id = "";
            Source = "Steam";
            SwitchApplicationId = "";
            LocalTitle = "";
            BackloggdGameId = "";
            BackloggdTitle = "";
            ReleaseYear = "";
            BackloggdLibraryEntryId = "";
            PlatformId = 6;
            OwnershipId = 1; // Digital
            StorefrontId = 1; // Steam
            EditionId = "";
            EditionTitle = "";
            SyncError = "";
            LastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        public string GetPlatformDisplayName()
        {
            switch (PlatformId)
            {
                case 6: return "Windows PC";
                case 130: return "Nintendo Switch";
                case 508: return "Nintendo Switch 2";
                case 41: return "Wii U";
                case 37: return "Nintendo 3DS";
                default: return "Platform #" + PlatformId;
            }
        }

        public string GetOwnershipDisplayName()
        {
            switch (OwnershipId)
            {
                case 0: return "Physical";
                case 1: return "Digital";
                case 3: return "Lost / Sold";
                default: return "Owned";
            }
        }

        public string GetStorefrontDisplayName()
        {
            if (OwnershipId != 1 || !StorefrontId.HasValue) return "—";
            switch (StorefrontId.Value)
            {
                case 1: return "Steam";
                case 8: return "Nintendo eShop";
                case 4: return "Epic Games";
                case 5: return "GOG";
                case 2: return "Battle.net";
                case 3: return "EA App";
                case 6: return "Ubisoft Connect";
                case 7: return "Rockstar Games";
                case 9: return "PlayStation Store";
                case 10: return "Xbox Store";
                case 11: return "Microsoft Store";
                case 24: return "Itch.io";
                default: return "Storefront #" + StorefrontId.Value;
            }
        }
    }
}
