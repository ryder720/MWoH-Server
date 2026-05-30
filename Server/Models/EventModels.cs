using System;
using System.ComponentModel.DataAnnotations;

namespace MwohServer.Models
{
    public class EventTemplate
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string BannerImage { get; set; } = string.Empty;
        public string EventType { get; set; } = string.Empty; // Raid, CompanyRaid, War, Training, Survival
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public DateTime ResultDate { get; set; }
        public bool IsActiveOverride { get; set; } = false;
        public string CustomConfigJson { get; set; } = "{}"; // Milestone thresholds, boss definitions, reward brackets
    }

    public class PlayerEventProgress
    {
        [Key]
        public int Id { get; set; }

        public int PlayerProfileId { get; set; }

        [Required]
        [MaxLength(100)]
        public string EventId { get; set; } = string.Empty;

        public int Points { get; set; } = 0;

        public int TierClaimed { get; set; } = 0; // Bitmask or index tracking claimed milestone tiers

        public bool RankRewardsClaimed { get; set; } = false; // Tracks if calculation rewards were dispatched

        [Required]
        public string CustomProgressJson { get; set; } = "{}"; // Event-specific progress details (floor, streak, boss status)

        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public PlayerProfile? PlayerProfile { get; set; }
    }

    public class RaidProgressState
    {
        public RaidTargetState EasyTarget { get; set; } = new();
        public RaidTargetState MediumTarget { get; set; } = new();
        public RaidTargetState HardTarget { get; set; } = new();
        public int? HelperProfileId { get; set; }
    }

    public class RaidTargetState
    {
        public int Level { get; set; } = 1;
        public string BodyPartName { get; set; } = string.Empty;
        public long MainHpMax { get; set; }
        public long MainHpCurrent { get; set; }
        public long BodyPartHpMax { get; set; }
        public long BodyPartHpCurrent { get; set; }
        public bool IsInitialized { get; set; } = false;
    }
}
