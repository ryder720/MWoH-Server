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
        public int AttackPowerCurrent { get; set; } = 10;
        
        public int DefensePower { get; set; } = 10;
        public int DefensePowerCurrent { get; set; } = 10;
        
        public long MobaCoinBalance { get; set; } = 10000;
        
        public long SilverBalance { get; set; } = 50000;
        
        public string PlayerIdString { get; set; } = "100001";
        
        public string SessionId { get; set; } = "";
        
        public int StatPoints { get; set; } = 0;
        
        public int MaxCardCapacity { get; set; } = 250;
        
        public string MissionProgressJson { get; set; } = "{\"UnlockedOperationId\":1,\"UnlockedMissionId\":1,\"ActiveMissionId\":1,\"ActiveMissionProgress\":0}";
        
        public string ResourceRedemptionsJson { get; set; } = "{}";

        public DateTime LastEnergyRecoveryTime { get; set; } = DateTime.UtcNow;
        public DateTime LastBattlePowerRecoveryTime { get; set; } = DateTime.UtcNow;

        public DateTime? LastRemovalTime { get; set; }
        public int RemovalsInLast24Hours { get; set; } = 0;
        public int RallyPoints { get; set; } = 0;
        
        public int? AllianceId { get; set; }
        public string? AllianceRole { get; set; } // "Leader", "Vice-Leader", "Offense-Leader", "Defense-Leader", "Member"
        public long AllianceDonatedSilver { get; set; } = 0;
        public DateTime? AllianceJoinedAt { get; set; }
        
        // Navigation properties
        public UserAccount? UserAccount { get; set; }
        public Alliance? Alliance { get; set; }
        public System.Collections.Generic.ICollection<PlayerCard> Cards { get; set; } = new System.Collections.Generic.List<PlayerCard>();
        public System.Collections.Generic.ICollection<PlayerInventoryItem> InventoryItems { get; set; } = new System.Collections.Generic.List<PlayerInventoryItem>();
        public System.Collections.Generic.ICollection<PlayerAssignmentProgress> AssignmentProgresses { get; set; } = new System.Collections.Generic.List<PlayerAssignmentProgress>();
        public System.Collections.Generic.ICollection<PlayerLoginCommendationProgress> LoginCommendations { get; set; } = new System.Collections.Generic.List<PlayerLoginCommendationProgress>();
        public System.Collections.Generic.ICollection<PlayerEventProgress> EventProgresses { get; set; } = new System.Collections.Generic.List<PlayerEventProgress>();
    }

    public class ShieldTeamMember
    {
        [Key]
        public int Id { get; set; }
        public int ProfileId { get; set; }
        public int MemberProfileId { get; set; }
        public string Status { get; set; } = "Pending"; // Pending, Accepted
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public PlayerProfile? Profile { get; set; }
        public PlayerProfile? MemberProfile { get; set; }
    }

    public class RallyLog
    {
        [Key]
        public int Id { get; set; }
        public int SenderProfileId { get; set; }
        public int ReceiverProfileId { get; set; }
        public DateTime RalliedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public PlayerProfile? SenderProfile { get; set; }
        public PlayerProfile? ReceiverProfile { get; set; }
    }

    public class BattleRecord
    {
        [Key]
        public int Id { get; set; }
        public int AttackerProfileId { get; set; }
        public int DefenderProfileId { get; set; }
        public int WinnerProfileId { get; set; }
        public int AttackerFinalPower { get; set; }
        public int DefenderFinalPower { get; set; }
        public long SilverExchanged { get; set; }
        public int MasteryEarned { get; set; }
        public DateTime BattleTime { get; set; } = DateTime.UtcNow;
        public bool IsSparring { get; set; } = false;
        public string DetailsJson { get; set; } = "[]"; // Complete transcripts list

        // Navigation properties
        public PlayerProfile? Attacker { get; set; }
        public PlayerProfile? Defender { get; set; }
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
        public int MaxMastery { get; set; } = 100;
        
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
        public int FusionBonusAtk { get; set; } = 0;
        public int FusionBonusDef { get; set; } = 0;
        
        // Deck and Leader representative flags
        public bool IsLeader { get; set; } = false;
        public bool IsInAttackDeck { get; set; } = false;
        public bool IsInDefenseDeck { get; set; } = false;
        public bool IsInTrade { get; set; } = false;
        
        // Navigation properties
        public PlayerProfile? PlayerProfile { get; set; }
        public CardTemplate? CardTemplate { get; set; }

        public void InitializeStats(CardTemplate template, int defaultMasteryPercentage)
        {
            CardTemplate = template;
            CardTemplateId = template.Id;
            CurrentLevel = 1;
            FusionBonusAtk = 0;
            FusionBonusDef = 0;
            
            var maxMastery = template.MaxMastery;
            if (maxMastery <= 0) maxMastery = 100;
            
            CurrentMastery = (maxMastery * defaultMasteryPercentage) / 100;
            
            var activeMasteryAtk = maxMastery > 0 ? (template.MasteryBonusAtk * CurrentMastery) / maxMastery : 0;
            var activeMasteryDef = maxMastery > 0 ? (template.MasteryBonusDef * CurrentMastery) / maxMastery : 0;
            
            CurrentAtk = template.BaseAtk + activeMasteryAtk;
            CurrentDef = template.BaseDef + activeMasteryDef;
        }

        /// <summary>
        /// Retrieves the maximum level limit for this card's rarity.
        /// </summary>
        public int GetMaxLevel()
        {
            if (CardTemplate == null) return 50;
            return CardTemplate.Rarity switch
            {
                "Common" or "Normal"          => 30,
                "High Normal" or "Uncommon"   => 40,
                "Rare"                        => 50,
                "High Rare"                   => 60,
                "Super Rare"                  => 70,
                "Ultra Rare"                  => 80,
                "Legend" or "Legendary"       => 90,
                "Special Legend"              => 100,
                _                             => 50
            };
        }

        /// <summary>
        /// Re-interpolates CurrentAtk / CurrentDef from level progress, current mastery, and
        /// any fusion bonus already stored on this card. Call this after incrementing CurrentMastery.
        /// </summary>
        public void RecalculateStats()
        {
            if (CardTemplate == null) return;

            int maxLevel = GetMaxLevel();
            var progress = maxLevel > 1 ? (double)(CurrentLevel - 1) / (maxLevel - 1) : 0.0;
            var newBaseAtk = (int)Math.Round(CardTemplate.BaseAtk + (CardTemplate.MaxAtk - CardTemplate.BaseAtk) * progress);
            var newBaseDef = (int)Math.Round(CardTemplate.BaseDef + (CardTemplate.MaxDef - CardTemplate.BaseDef) * progress);

            var maxMastery = CardTemplate.MaxMastery;
            if (maxMastery <= 0) maxMastery = 100;

            var activeMasteryAtk = maxMastery > 0 ? (CardTemplate.MasteryBonusAtk * CurrentMastery) / maxMastery : 0;
            var activeMasteryDef = maxMastery > 0 ? (CardTemplate.MasteryBonusDef * CurrentMastery) / maxMastery : 0;

            CurrentAtk = newBaseAtk + activeMasteryAtk + FusionBonusAtk;
            CurrentDef = newBaseDef + activeMasteryDef + FusionBonusDef;
        }
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
        public static int AttackRecoveryIntervalSeconds { get; set; } = 180; // 3 minutes
        public static int AttackRecoveryAmount { get; set; } = 1;
        public static int DefenseRecoveryIntervalSeconds { get; set; } = 180; // 3 minutes
        public static int DefenseRecoveryAmount { get; set; } = 1;
        public static int BaseXpRequirement { get; set; } = 1000;
        public static int XpIncrementPerLevel { get; set; } = 500;
        public static int EnergyMaxIncreasePerLevel { get; set; } = 2;
        public static int DefaultMasteryPercentage { get; set; } = 100;
        public static int MasteryGainPerMissionClick { get; set; } = 1;
        public static int MasteryGainPerPvPBattle { get; set; } = 5;
        public static int DefaultAttackPower { get; set; } = 100;
        public static int DefaultDefensePower { get; set; } = 100;
        public static string CommunityUrl { get; set; } = "https://github.com/ryder720/MWoH-Server";
        public static int ResourceDropRatePercentage { get; set; } = 100;
        public static bool EnableFriendRemoval24HourPenalty { get; set; } = true;
        public static bool IgnoreAssignmentDates { get; set; } = false;
        public static bool IgnoreLoginCommendationDates { get; set; } = false;
        public static int TradeCooldownDays { get; set; } = 14;
        public static int TradeMinLevel { get; set; } = 10;
        public static int TradeMaxCards { get; set; } = 3;

        public static void Load()
        {
            var configPath = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Config", "gameplay_settings.json");
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
                            if (gameplayNode.TryGetProperty("AttackRecovery", out var attackNode))
                            {
                                if (attackNode.TryGetProperty("IntervalSeconds", out var intervalProp))
                                    AttackRecoveryIntervalSeconds = intervalProp.GetInt32();
                                if (attackNode.TryGetProperty("AmountPerInterval", out var amountProp))
                                    AttackRecoveryAmount = amountProp.GetInt32();
                            }
                            if (gameplayNode.TryGetProperty("DefenseRecovery", out var defenseNode))
                            {
                                if (defenseNode.TryGetProperty("IntervalSeconds", out var intervalProp))
                                    DefenseRecoveryIntervalSeconds = intervalProp.GetInt32();
                                if (defenseNode.TryGetProperty("AmountPerInterval", out var amountProp))
                                    DefenseRecoveryAmount = amountProp.GetInt32();
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
                            if (gameplayNode.TryGetProperty("DefaultDeckCapacity", out var deckCapNode))
                            {
                                if (deckCapNode.TryGetProperty("AttackPower", out var atkProp))
                                    DefaultAttackPower = atkProp.GetInt32();
                                if (deckCapNode.TryGetProperty("DefensePower", out var defProp))
                                    DefaultDefensePower = defProp.GetInt32();
                            }
                            if (gameplayNode.TryGetProperty("CardGrowth", out var growthNode))
                            {
                                if (growthNode.TryGetProperty("DefaultMasteryPercentage", out var masteryProp))
                                    DefaultMasteryPercentage = masteryProp.GetInt32();
                                if (growthNode.TryGetProperty("MasteryGainPerMissionClick", out var gainClickProp))
                                    MasteryGainPerMissionClick = gainClickProp.GetInt32();
                                if (growthNode.TryGetProperty("MasteryGainPerPvPBattle", out var gainPvpProp))
                                    MasteryGainPerPvPBattle = gainPvpProp.GetInt32();
                            }
                            if (gameplayNode.TryGetProperty("CommunityUrl", out var communityProp))
                            {
                                CommunityUrl = communityProp.GetString() ?? "https://github.com/ryder720/MWoH-Server";
                            }
                            if (gameplayNode.TryGetProperty("ResourceDropRatePercentage", out var resDropProp))
                            {
                                ResourceDropRatePercentage = resDropProp.GetInt32();
                            }
                            if (gameplayNode.TryGetProperty("EnableFriendRemoval24HourPenalty", out var fPenaltyProp))
                            {
                                EnableFriendRemoval24HourPenalty = fPenaltyProp.GetBoolean();
                            }
                            if (gameplayNode.TryGetProperty("IgnoreAssignmentDates", out var ignoreProp))
                            {
                                IgnoreAssignmentDates = ignoreProp.GetBoolean();
                            }
                            if (gameplayNode.TryGetProperty("IgnoreLoginCommendationDates", out var ignoreLoginDatesProp))
                            {
                                IgnoreLoginCommendationDates = ignoreLoginDatesProp.GetBoolean();
                            }
                            if (gameplayNode.TryGetProperty("TradeCooldownDays", out var tCooldownProp))
                            {
                                TradeCooldownDays = tCooldownProp.GetInt32();
                            }
                            if (gameplayNode.TryGetProperty("TradeMinLevel", out var tMinLvlProp))
                            {
                                TradeMinLevel = tMinLvlProp.GetInt32();
                            }
                            if (gameplayNode.TryGetProperty("TradeMaxCards", out var tMaxCardsProp))
                            {
                                TradeMaxCards = tMaxCardsProp.GetInt32();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Console.WriteLine($"[Warning] Failed to load Config/gameplay_settings.json: {ex.Message}. Using default settings.");
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
    ""AttackRecovery"": {
      ""IntervalSeconds"": 180,
      ""AmountPerInterval"": 1
    },
    ""DefenseRecovery"": {
      ""IntervalSeconds"": 180,
      ""AmountPerInterval"": 1
    },
    ""LevelUp"": {
      ""BaseXpRequirement"": 1000,
      ""XpIncrementPerLevel"": 500,
      ""EnergyMaxIncreasePerLevel"": 2
    },
    ""DefaultDeckCapacity"": {
      ""AttackPower"": 100,
      ""DefensePower"": 100
    },
    ""CardGrowth"": {
      ""DefaultMasteryPercentage"": 100
    },
    ""CommunityUrl"": ""https://github.com/ryder720/MWoH-Server"",
    ""ResourceDropRatePercentage"": 100,
    ""EnableFriendRemoval24HourPenalty"": true,
    ""IgnoreAssignmentDates"": false,
    ""TradeCooldownDays"": 14,
    ""TradeMinLevel"": 10,
    ""TradeMaxCards"": 3
  }
}";
                try
                {
                    var dir = System.IO.Path.GetDirectoryName(configPath);
                    if (dir != null && !System.IO.Directory.Exists(dir))
                    {
                        System.IO.Directory.CreateDirectory(dir);
                    }
                    System.IO.File.WriteAllText(configPath, defaultJson);
                }
                catch {}
            }
        }
    }

    public class Alliance
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        [MaxLength(23)]
        public string Name { get; set; } = string.Empty;
        
        [MaxLength(100)]
        public string Slogan { get; set; } = "Assemble!";
        
        public int LeaderProfileId { get; set; }
        public int Level { get; set; } = 1;
        public long DonatedSilver { get; set; } = 0;
        public int Rating { get; set; } = 0;
        public int ProtectionWallCount { get; set; } = 0;
        public int SpeedAdaptorLevel { get; set; } = 0;
        public int BruiserAdaptorLevel { get; set; } = 0;
        public int TacticsAdaptorLevel { get; set; } = 0;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsQueuedForWar { get; set; } = false;
        public DateTime? WarQueueJoinedAt { get; set; }

        public System.Collections.Generic.ICollection<PlayerProfile> Members { get; set; } = new System.Collections.Generic.List<PlayerProfile>();
    }

    public class AllianceJoinRequest
    {
        [Key]
        public int Id { get; set; }
        
        public int AllianceId { get; set; }
        public int PlayerProfileId { get; set; }
        public string Status { get; set; } = "Pending"; // Pending, Accepted, Declined
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Alliance? Alliance { get; set; }
        public PlayerProfile? PlayerProfile { get; set; }
    }

    public class Trade
    {
        [Key]
        public int Id { get; set; }
        
        public int SenderProfileId { get; set; }
        public int ReceiverProfileId { get; set; }
        
        // Status: "Pending", "Completed", "Declined", "Canceled"
        public string Status { get; set; } = "Pending";
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
        
        // Financial assets
        public long OfferedSilver { get; set; }
        public long RequestedSilver { get; set; }
        
        // Cards (PlayerCard IDs serialized to JSON arrays)
        public string OfferedCardIdsJson { get; set; } = "[]";
        public string RequestedCardIdsJson { get; set; } = "[]";
        
        // Items (JSON array representing list of ItemTemplateId + Quantity)
        public string OfferedItemsJson { get; set; } = "[]";
        public string RequestedItemsJson { get; set; } = "[]";

        // Navigation
        public PlayerProfile? SenderProfile { get; set; }
        public PlayerProfile? ReceiverProfile { get; set; }
    }
}

