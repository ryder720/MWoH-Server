using System;
using System.ComponentModel.DataAnnotations;

namespace MwohServer.Models
{
    public class UserAccount
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        [MaxLength(100)]
        public string Username { get; set; } = string.Empty;
        
        [Required]
        public string PasswordHash { get; set; } = string.Empty;
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        public string? ActiveToken { get; set; }
        
        // Relationship
        public PlayerProfile? Profile { get; set; }
    }

    public class PlayerProfile
    {
        [Key]
        public int Id { get; set; }
        
        public int UserAccountId { get; set; }
        
        public string Nickname { get; set; } = "Agent";
        
        public int Level { get; set; } = 1;
        
        public int Experience { get; set; } = 0;
        
        public int EnergyMax { get; set; } = 100;
        
        public int EnergyCurrent { get; set; } = 100;
        
        public int AttackPower { get; set; } = 10;
        
        public int DefensePower { get; set; } = 10;
        
        public long MobaCoinBalance { get; set; } = 10000;
        
        public long SilverBalance { get; set; } = 50000;
        
        public string PlayerIdString { get; set; } = "100001";
        
        public string SessionId { get; set; } = "";
        
        // Navigation properties
        public UserAccount? UserAccount { get; set; }
        public System.Collections.Generic.ICollection<PlayerCard> Cards { get; set; } = new System.Collections.Generic.List<PlayerCard>();
    }

    // Static card metadata parsed from Wiki scraping JSON
    public class CardTemplate
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        public string Title { get; set; } = string.Empty;
        
        [Required]
        public string VisualTitle { get; set; } = string.Empty;
        
        [Required]
        public string Alignment { get; set; } = string.Empty; // Speed, Bruiser, Tactics
        
        public string Rarity { get; set; } = "Normal";
        public string Faction { get; set; } = "None"; // Super Hero, Villain, etc.
        public string Gender { get; set; } = "None";
        public int PowerRequirement { get; set; }
        
        // Stat Arrays
        public int BaseAtk { get; set; }
        public int BaseDef { get; set; }
        public int MaxAtk { get; set; }
        public int MaxDef { get; set; }
        public int MasteryBonusAtk { get; set; }
        public int MasteryBonusDef { get; set; }
        
        public string AbilityName { get; set; } = string.Empty;
        public string AbilityEffect { get; set; } = string.Empty;
        public string Quote { get; set; } = string.Empty;
        
        // Artwork filename references
        public string ImageFileName { get; set; } = string.Empty;
        
        // Variant tracker
        public string VariantName { get; set; } = string.Empty;
    }

    // Dynamic instance of a card owned by a specific player profile
    public class PlayerCard
    {
        [Key]
        public int Id { get; set; }
        
        public int PlayerProfileId { get; set; }
        public int CardTemplateId { get; set; }
        
        public int CurrentLevel { get; set; } = 1;
        public int CurrentMastery { get; set; } = 0;
        public int CurrentAtk { get; set; }
        public int CurrentDef { get; set; }
        
        // Navigation properties
        public PlayerProfile? PlayerProfile { get; set; }
        public CardTemplate? CardTemplate { get; set; }
    }
}
