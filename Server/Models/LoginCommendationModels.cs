using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace MwohServer.Models
{
    public class LoginRewardTemplate
    {
        public int Day { get; set; }
        public string RewardType { get; set; } = string.Empty; // Silver, RallyPoints, MobaCoin, Item, Card, CardStock
        public int RewardValue { get; set; }
        public int RewardQuantity { get; set; } = 1;
    }

    public class LoginCampaignTemplate
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<LoginRewardTemplate> Rewards { get; set; } = new();
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public bool IsActive { get; set; } = true;
        public string FallbackRewardType { get; set; } = "Silver";
        public int FallbackRewardValue { get; set; } = 10000;
        public int FallbackRewardQuantity { get; set; } = 1;
    }

    public class PlayerLoginCommendationProgress
    {
        [Key]
        public int Id { get; set; }

        public int PlayerProfileId { get; set; }

        [Required]
        [MaxLength(100)]
        public string CampaignId { get; set; } = string.Empty;

        public int TotalLogins { get; set; } = 0;

        public DateTime LastLoginDate { get; set; } = DateTime.MinValue;

        [Required]
        public string ClaimedDaysJson { get; set; } = "[]";

        // Navigation properties
        public PlayerProfile? PlayerProfile { get; set; }
    }
}
