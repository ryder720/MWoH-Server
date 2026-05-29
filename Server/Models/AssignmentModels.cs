using System;
using System.ComponentModel.DataAnnotations;

namespace MwohServer.Models
{
    public enum GoalType
    {
        DrawRallyPack,
        EnhanceCard,
        PvpBattle,
        CompleteOperation,
        ShieldRequest,
        LoginTomorrow,
        LevelUp,
        WinStreak,
        PvpWin,
        SkillsActivated,
        MoraleWin,
        StartMission,
        FuseCard
    }

    public class AssignmentTemplate
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string GoalType { get; set; } = string.Empty; // Matches GoalType enum as string
        public int GoalTarget { get; set; }
        public string RewardType { get; set; } = string.Empty; // Silver, RallyPoints, MobaCoin, Item, Card, CardStock
        public int RewardValue { get; set; } // Amount for currencies, or ItemTemplateId / CardTemplateId
        public int RewardQuantity { get; set; } = 1; // Quantity for Item / Card drops
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public int Batch { get; set; } = 1;
        public string GroupName { get; set; } = "Initial"; // e.g. "Initial", "Level", "Special Assignment 1"
        public bool IsCompletionBonus { get; set; } = false;
    }

    public class PlayerAssignmentProgress
    {
        [Key]
        public int Id { get; set; }
        
        public int PlayerProfileId { get; set; }
        
        [Required]
        [MaxLength(100)]
        public string AssignmentId { get; set; } = string.Empty;
        
        public int CurrentProgress { get; set; } = 0;
        
        public bool IsCompleted { get; set; } = false;
        
        public bool IsClaimed { get; set; } = false;
        
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
        
        // Navigation properties
        public PlayerProfile? PlayerProfile { get; set; }
    }
}
