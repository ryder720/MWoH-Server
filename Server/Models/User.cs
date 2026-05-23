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
        
        public string MissionProgressJson { get; set; } = "{\"UnlockedOperationId\":1,\"UnlockedMissionId\":1,\"ActiveMissionId\":1,\"ActiveMissionProgress\":0}";

        public DateTime LastEnergyRecoveryTime { get; set; } = DateTime.UtcNow;
        
        // Navigation properties
        public UserAccount? UserAccount { get; set; }
        public System.Collections.Generic.ICollection<PlayerCard> Cards { get; set; } = new System.Collections.Generic.List<PlayerCard>();
        public System.Collections.Generic.ICollection<PlayerInventoryItem> InventoryItems { get; set; } = new System.Collections.Generic.List<PlayerInventoryItem>();
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
        public int AbilityLevel { get; set; } = 1;
        
        // Deck and Leader representative flags
        public bool IsLeader { get; set; } = false;
        public bool IsInAttackDeck { get; set; } = false;
        public bool IsInDefenseDeck { get; set; } = false;
        
        // Navigation properties
        public PlayerProfile? PlayerProfile { get; set; }
        public CardTemplate? CardTemplate { get; set; }
    }

    // Static item metadata parsed from Wiki listings or preloaded catalog
    public class ItemTemplate
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        public string Name { get; set; } = string.Empty;
        
        [Required]
        public string Description { get; set; } = string.Empty;
        
        [Required]
        public string Type { get; set; } = string.Empty; // "EnergyRestorative", "AttackPowerRestorative", "DefensePowerRestorative", "MasteryIso8", "GachaTicket"
        
        public int EffectValue { get; set; } // Refill percentage (e.g. 50 or 100) or mastery value
        public string ImageFileName { get; set; } = string.Empty;
    }

    // Dynamic instance of an item owned by a specific player profile
    public class PlayerInventoryItem
    {
        [Key]
        public int Id { get; set; }
        
        public int PlayerProfileId { get; set; }
        public int ItemTemplateId { get; set; }
        public int Quantity { get; set; } = 0;
        
        // Navigation properties
        public PlayerProfile? PlayerProfile { get; set; }
        public ItemTemplate? ItemTemplate { get; set; }
    }

    public static class GameplaySettings
    {
        public static int EnergyRecoveryIntervalSeconds { get; set; } = 300; // 5 minutes
        public static int EnergyRecoveryAmount { get; set; } = 1;
        public static int BaseXpRequirement { get; set; } = 1000;
        public static int XpIncrementPerLevel { get; set; } = 500;
        public static int EnergyMaxIncreasePerLevel { get; set; } = 2;

        public static void Load()
        {
            var configPath = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "gameplay_settings.json");
            if (System.IO.File.Exists(configPath))
            {
                try
                {
                    var json = System.IO.File.ReadAllText(configPath);
                    using (var doc = System.Text.Json.JsonDocument.Parse(json))
                    {
                        var root = doc.RootElement;
                        if (root.TryGetProperty("Gameplay", out var gameplayNode))
                        {
                            if (gameplayNode.TryGetProperty("EnergyRecovery", out var energyNode))
                            {
                                if (energyNode.TryGetProperty("IntervalSeconds", out var intervalProp))
                                    EnergyRecoveryIntervalSeconds = intervalProp.GetInt32();
                                if (energyNode.TryGetProperty("AmountPerInterval", out var amountProp))
                                    EnergyRecoveryAmount = amountProp.GetInt32();
                            }
                            if (gameplayNode.TryGetProperty("LevelUp", out var lvlNode))
                            {
                                if (lvlNode.TryGetProperty("BaseXpRequirement", out var baseXpProp))
                                    BaseXpRequirement = baseXpProp.GetInt32();
                                if (lvlNode.TryGetProperty("XpIncrementPerLevel", out var xpIncProp))
                                    XpIncrementPerLevel = xpIncProp.GetInt32();
                                if (lvlNode.TryGetProperty("EnergyMaxIncreasePerLevel", out var energyIncProp))
                                    EnergyMaxIncreasePerLevel = energyIncProp.GetInt32();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Console.WriteLine($"[Warning] Failed to load gameplay_settings.json: {ex.Message}. Using default settings.");
                }
            }
            else
            {
                var defaultJson = @"{
  ""Gameplay"": {
    ""EnergyRecovery"": {
      ""IntervalSeconds"": 300,
      ""AmountPerInterval"": 1
    },
    ""LevelUp"": {
      ""BaseXpRequirement"": 1000,
      ""XpIncrementPerLevel"": 500,
      ""EnergyMaxIncreasePerLevel"": 2
    }
  }
}";
                try
                {
                    System.IO.File.WriteAllText(configPath, defaultJson);
                }
                catch {}
            }
        }
    }
}
