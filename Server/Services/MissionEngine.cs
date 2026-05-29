using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MwohServer.Data;
using MwohServer.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MwohServer.Services
{
    public class MissionEngine : IMissionEngine
    {
        private readonly ILogger<MissionEngine> _logger;
        private readonly MwohDbContext _dbContext;
        private static readonly List<OperationBlueprint> _operations = new();

        static MissionEngine()
        {
            try
            {
                var jsonPath = Path.Combine(Directory.GetCurrentDirectory(), "Data", "operations_db.json");
                if (!System.IO.File.Exists(jsonPath))
                {
                    jsonPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "Tools", "Scraper", "operations_db.json");
                }
                if (!System.IO.File.Exists(jsonPath))
                {
                    jsonPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Tools", "Scraper", "operations_db.json");
                }
                if (System.IO.File.Exists(jsonPath))
                {
                    var jsonText = System.IO.File.ReadAllText(jsonPath);
                    _operations = JsonSerializer.Deserialize<List<OperationBlueprint>>(jsonText, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }) ?? new List<OperationBlueprint>();
                }
            }
            catch
            {
                // Let it remain empty; fallback handled dynamically below
            }

            // Seed progressive fallbacks if scraper JSON failed to load
            if (_operations.Count == 0)
            {
                for (int i = 1; i <= 29; i++)
                {
                    var op = new OperationBlueprint
                    {
                        OperationId = i,
                        Title = $"Operation {i}: Secure Node {i}",
                        CleanName = $"Secure Node {i}",
                        EnergyCost = Math.Max(1, i / 2),
                        XpReward = Math.Max(1, i / 2),
                        SilverMin = i * 20,
                        SilverMax = i * 24,
                        BossName = "Sentinel Guard",
                        BossSilverReward = i * 150
                    };

                    // Seed sub-missions for the operation
                    for (int j = 1; j <= 5; j++)
                    {
                        op.Missions.Add(new MissionBlueprint
                        {
                            MissionCode = $"{i}-{j}",
                            Name = $"Sector Node {i}-{j}",
                            EnergyCost = op.EnergyCost,
                            XpReward = op.XpReward,
                            SilverMin = op.SilverMin,
                            SilverMax = op.SilverMax,
                            PossibleDrops = new List<string> { "Spider-Man", "Iron Man", "Captain America" }
                        });
                    }
                    _operations.Add(op);
                }
            }
        }

        private readonly IAssignmentEngine _assignmentEngine;

        public MissionEngine(ILogger<MissionEngine> logger, MwohDbContext dbContext, IAssignmentEngine assignmentEngine)
        {
            _logger = logger;
            _dbContext = dbContext;
            _assignmentEngine = assignmentEngine;
        }

        public List<OperationBlueprint> GetOperations()
        {
            return _operations;
        }

        public (OperationBlueprint? Operation, MissionBlueprint? Mission) GetMissionBlueprint(string missionCode)
        {
            foreach (var op in _operations)
            {
                var m = op.Missions.FirstOrDefault(x => x.MissionCode == missionCode);
                if (m != null)
                {
                    return (op, m);
                }
            }
            return (null, null);
        }

        public void RestoreEnergy(PlayerProfile profile)
        {
            if (profile == null) return;

            var now = DateTime.UtcNow;
            var lastRecovery = DateTime.SpecifyKind(profile.LastEnergyRecoveryTime, DateTimeKind.Utc);

            if (profile.EnergyCurrent < profile.EnergyMax)
            {
                var secondsElapsed = (now - lastRecovery).TotalSeconds;
                var recoveryInterval = GameplaySettings.EnergyRecoveryIntervalSeconds;
                if (secondsElapsed >= recoveryInterval && recoveryInterval > 0)
                {
                    var intervals = (int)(secondsElapsed / recoveryInterval);
                    var restoredEnergy = intervals * GameplaySettings.EnergyRecoveryAmount;
                    profile.EnergyCurrent = Math.Min(profile.EnergyMax, profile.EnergyCurrent + restoredEnergy);
                    
                    // Advance LastEnergyRecoveryTime by the exact intervals consumed
                    profile.LastEnergyRecoveryTime = lastRecovery.AddSeconds(intervals * recoveryInterval);
                    _dbContext.SaveChanges();
                }
            }
            else
            {
                // Energy is already at or above max, keep the recovery time pinned to now
                if (lastRecovery < now)
                {
                    profile.LastEnergyRecoveryTime = now;
                    _dbContext.SaveChanges();
                }
            }
        }

        private MissionProgressState GetPlayerMissionProgress(PlayerProfile profile)
        {
            try
            {
                if (!string.IsNullOrEmpty(profile.MissionProgressJson))
                {
                    return JsonSerializer.Deserialize<MissionProgressState>(profile.MissionProgressJson) ?? new MissionProgressState();
                }
            }
            catch { }
            return new MissionProgressState();
        }

        private void SavePlayerMissionProgress(PlayerProfile profile, MissionProgressState state)
        {
            profile.MissionProgressJson = JsonSerializer.Serialize(state);
        }

        public MissionAttackResult Attack(int profileId, string missionCode)
        {
            var profile = _dbContext.Profiles
                .Include(p => p.Cards)
                    .ThenInclude(c => c.CardTemplate)
                .FirstOrDefault(p => p.Id == profileId);

            if (profile == null)
            {
                return new MissionAttackResult { Success = false, Message = "Profile not synced." };
            }

            // Lazy Catch up on timed energy recovery first
            RestoreEnergy(profile);

            var (activeOp, activeMission) = GetMissionBlueprint(missionCode);
            if (activeOp == null || activeMission == null)
            {
                return new MissionAttackResult { Success = false, Message = "Mission blueprint mismatch." };
            }

            if (profile.EnergyCurrent < activeMission.EnergyCost)
            {
                return new MissionAttackResult
                {
                    Success = false,
                    Message = "⚠️ DEPLOYMENT ENERGY DEPLETED // Sync items at S.H.I.E.L.D. depot."
                };
            }

            profile.EnergyCurrent -= activeMission.EnergyCost;
            _assignmentEngine.RecordEvent(profileId, GoalType.StartMission, 1);
            var progressState = GetPlayerMissionProgress(profile);
            progressState.ActiveMissionProgress = Math.Min(100, progressState.ActiveMissionProgress + 20);

            var rand = new Random();
            var silverGained = rand.Next(activeMission.SilverMin, activeMission.SilverMax + 1);
            profile.SilverBalance += silverGained;

            var xpGained = activeMission.XpReward;
            profile.Experience += xpGained;

            var levelUp = false;
            if (profile.Level >= 200)
            {
                profile.Experience = 0;
            }
            else
            {
                while (profile.Level < 200)
                {
                    var nextExpNeeded = GameplaySettings.BaseXpRequirement + (profile.Level - 1) * GameplaySettings.XpIncrementPerLevel;
                    if (profile.Experience >= nextExpNeeded)
                    {
                        profile.Experience -= nextExpNeeded;
                        profile.Level++;
                        _assignmentEngine.RecordEvent(profileId, GoalType.LevelUp, profile.Level);
                        profile.StatPoints += 3;
                        profile.EnergyCurrent = profile.EnergyMax;
                        levelUp = true;

                        if (profile.Level >= 200)
                        {
                            profile.Experience = 0;
                            break;
                        }
                    }
                    else
                    {
                        break;
                    }
                }
            }

            var cardDropped = false;
            var droppedCardName = "";

            int currentCardCount = _dbContext.PlayerCards.Count(pc => pc.PlayerProfileId == profile.Id);
            bool isInventoryFull = currentCardCount >= profile.MaxCardCapacity;

            if (rand.Next(1, 101) <= 20 && activeMission.PossibleDrops.Count > 0)
            {
                var templateName = activeMission.PossibleDrops[rand.Next(activeMission.PossibleDrops.Count)];
                var cardTemp = _dbContext.CardTemplates.FirstOrDefault(t => t.Title == templateName);
                if (cardTemp != null)
                {
                    if (isInventoryFull)
                    {
                        _logger.LogWarning($"[MissionEngine] Card drop skipped for Profile {profile.Id} - Inventory full ({profile.MaxCardCapacity}/{profile.MaxCardCapacity}).");
                    }
                    else
                    {
                        cardDropped = true;
                        droppedCardName = cardTemp.Title;

                        var droppedCard = new PlayerCard
                        {
                            PlayerProfileId = profile.Id
                        };
                        droppedCard.InitializeStats(cardTemp, GameplaySettings.DefaultMasteryPercentage);
                        _dbContext.PlayerCards.Add(droppedCard);
                    }
                }
            }

            // ── Card Mastery Grinding ────────────────────────────────────────────────
            // Cards that are active in the Attack Deck or designated as squad leader
            // gain mastery experience every time the player clicks through a mission.
            var masteryGained = 0;
            foreach (var card in profile.Cards)
            {
                if (!card.IsInAttackDeck && !card.IsLeader) continue;
                if (card.CardTemplate == null) continue;

                var maxMastery = card.CardTemplate.MaxMastery;
                if (maxMastery <= 0) maxMastery = 100;

                if (card.CurrentMastery < maxMastery)
                {
                    card.CurrentMastery = Math.Min(maxMastery,
                        card.CurrentMastery + GameplaySettings.MasteryGainPerMissionClick);
                    card.RecalculateStats();
                    masteryGained++;
                }
            }

            if (masteryGained > 0)
            {
                _logger.LogDebug("[MissionEngine] Mastery incremented for {Count} active card(s) on profile {Id}.", masteryGained, profileId);
            }
            // ────────────────────────────────────────────────────────────────────────

            SavePlayerMissionProgress(profile, progressState);
            _dbContext.SaveChanges();

            var logLines = new List<string>
            {
                $"DEPLOYED SQUAD DEEP INTO SECTOR {missionCode}.",
                $"DEFEATED HOSTILE ELEMENTS IN THE AREA.",
                $"GAINED +{silverGained} SILVER AND +{xpGained} XP."
            };
            if (levelUp)
            {
                logLines.Add($"⚡ LEVEL UP! CLEARANCE LEVEL INCREMENTED TO {profile.Level}!");
            }
            if (cardDropped)
            {
                logLines.Add($"🎁 TRANSMISSION DECRYPTED: RECOVERED HERO ASSET {droppedCardName}!");
            }
            else if (isInventoryFull && activeMission.PossibleDrops.Count > 0)
            {
                logLines.Add($"⚠️ WARNING: INVENTORY EXCEEDS MAXIMUM SECURED FILES ({profile.MaxCardCapacity}/{profile.MaxCardCapacity}). NO HERO ASSETS RECOVERED!");
            }

            return new MissionAttackResult
            {
                Success = true,
                EnergyCurrent = profile.EnergyCurrent,
                EnergyMax = profile.EnergyMax,
                EnergyPct = (double)profile.EnergyCurrent / profile.EnergyMax * 100,
                ProgressPct = progressState.ActiveMissionProgress,
                CardDropped = cardDropped,
                DroppedCardName = droppedCardName,
                LogLines = logLines
            };
        }

        public BossBattleResult EngageBoss(int profileId, string missionCode, List<int>? supportProfileIds = null)
        {
            var profile = _dbContext.Profiles
                .Include(p => p.Cards)
                    .ThenInclude(c => c.CardTemplate)
                .FirstOrDefault(p => p.Id == profileId);

            if (profile == null)
            {
                return new BossBattleResult { Success = false, Message = "Profile mismatch." };
            }

            // Restore energy first
            RestoreEnergy(profile);

            var (activeOp, activeMission) = GetMissionBlueprint(missionCode);
            if (activeOp == null || activeMission == null)
            {
                return new BossBattleResult { Success = false, Message = "Mission blueprint mismatch." };
            }

            // Gather Support Reinforcements
            var combatLogLines = new List<string>();
            int supportBonusAtk = 0;
            int supportBonusDef = 0;

            if (supportProfileIds != null && supportProfileIds.Count > 0)
            {
                combatLogLines.Add("📡 S.H.I.E.L.D. TACTICAL FREQUENCY LINKED // CONNECTING SUPPORT REINFORCEMENTS...");
                foreach (var supportId in supportProfileIds)
                {
                    var supportProfile = _dbContext.Profiles
                        .Include(p => p.Cards)
                            .ThenInclude(c => c.CardTemplate)
                        .FirstOrDefault(p => p.Id == supportId);

                    if (supportProfile != null)
                    {
                        var leaderCard = supportProfile.Cards.FirstOrDefault(c => c.IsLeader) ?? supportProfile.Cards.FirstOrDefault();
                        if (leaderCard != null)
                        {
                            supportBonusAtk += leaderCard.CurrentAtk;
                            supportBonusDef += leaderCard.CurrentDef;
                            combatLogLines.Add($"🤝 Agent {supportProfile.Nickname} deployed {leaderCard.CardTemplate?.Title ?? "Card"} (ATK: {leaderCard.CurrentAtk:N0} / DEF: {leaderCard.CurrentDef:N0}) to assist!");
                        }
                    }
                }
                combatLogLines.Add($"⚡ TOTAL CO-OP COMBAT POWER ADDED: ATK +{supportBonusAtk:N0} / DEF +{supportBonusDef:N0}!");
            }

            combatLogLines.Add($"⚔️ ENGAGING SUPER VILLAIN: {activeOp.BossName}...");
            combatLogLines.Add($"🔥 UNLEASHING ALL DESTRUCTION PROTOCOLS!");
            combatLogLines.Add("🎯 MISSION TARGET SECURED // Clearance levels updated.");

            var bossRewardTemplateName = activeMission.PossibleDrops.Count > 0 
                ? activeMission.PossibleDrops[0] 
                : "Spider-Man";

            var rewardTemplate = _dbContext.CardTemplates.FirstOrDefault(t => t.Title == bossRewardTemplateName)
                ?? _dbContext.CardTemplates.FirstOrDefault();

            var droppedCardName = "";
            int currentCardCount = _dbContext.PlayerCards.Count(pc => pc.PlayerProfileId == profile.Id);
            bool isInventoryFull = currentCardCount >= profile.MaxCardCapacity;

            if (rewardTemplate != null)
            {
                if (isInventoryFull)
                {
                    _logger.LogWarning($"[MissionEngine] Boss card drop skipped for Profile {profile.Id} - Inventory full ({profile.MaxCardCapacity}/{profile.MaxCardCapacity}).");
                }
                else
                {
                    droppedCardName = rewardTemplate.Title;
                    var bossRewardCard = new PlayerCard
                    {
                        PlayerProfileId = profile.Id
                    };
                    bossRewardCard.InitializeStats(rewardTemplate, GameplaySettings.DefaultMasteryPercentage);
                    _dbContext.PlayerCards.Add(bossRewardCard);
                }
            }

            profile.SilverBalance += activeOp.BossSilverReward;

            // Trigger CompleteOperation assignment hook
            var partsCode = missionCode.Split('-');
            if (partsCode.Length > 0 && int.TryParse(partsCode[0], out int finishedOpId))
            {
                _assignmentEngine.RecordEvent(profileId, GoalType.CompleteOperation, finishedOpId);
            }

            var progressState = GetPlayerMissionProgress(profile);
            if (!progressState.CompletedMissions.ContainsKey(missionCode))
            {
                progressState.CompletedMissions.Add(missionCode, 1);
            }
            else
            {
                progressState.CompletedMissions[missionCode]++;
            }

            var mSubIndex = 1;
            var parts = missionCode.Split('-');
            if (parts.Length > 1 && int.TryParse(parts[1], out int parsedIndex))
            {
                mSubIndex = parsedIndex;
            }

            if (activeOp.OperationId == progressState.UnlockedOperationId && mSubIndex == progressState.UnlockedMissionId)
            {
                if (mSubIndex < 5)
                {
                    progressState.UnlockedMissionId++;
                }
                else
                {
                    progressState.UnlockedOperationId = Math.Min(29, progressState.UnlockedOperationId + 1);
                    progressState.UnlockedMissionId = 1;
                }
            }

            progressState.ActiveMissionProgress = 0;

            // Resource drop logic
            bool resourceDropped = false;
            string droppedResourceName = "";
            string droppedResourceImage = "";

            try
            {
                var rand = new Random();
                var resourcePartsCode = missionCode.Split('-');
                if (resourcePartsCode.Length > 0 && int.TryParse(resourcePartsCode[0], out int opId) && opId >= 2 && opId <= 29)
                {
                    var dropRate = GameplaySettings.ResourceDropRatePercentage;
                    if (rand.Next(1, 101) <= dropRate)
                    {
                        string groupName = "";
                        string[]? colors = null;

                        if (opId >= 2 && opId <= 5)
                        {
                            groupName = "Storm's Cape";
                            colors = new[] { "Red", "Blue", "Green", "Yellow", "Purple", "Emerald" };
                        }
                        else if (opId >= 6 && opId <= 9)
                        {
                            groupName = "Suitcase";
                            colors = new[] { "Red", "Blue", "Green", "Yellow", "Purple", "Emerald" };
                        }
                        else if (opId >= 10 && opId <= 13)
                        {
                            groupName = "Sword of Proficiency";
                            colors = new[] { "Red", "Blue", "Green", "Yellow", "Purple", "Emerald" };
                        }
                        else if (opId >= 14 && opId <= 17)
                        {
                            groupName = "Assassin's Choker";
                            colors = new[] { "Red", "Blue", "Green", "Yellow", "Purple", "Emerald" };
                        }
                        else if (opId >= 18 && opId <= 21)
                        {
                            groupName = "Chain Belt";
                            colors = new[] { "Red", "Blue", "Green", "Yellow", "Purple", "Cyan" };
                        }
                        else if (opId >= 22 && opId <= 25)
                        {
                            groupName = "Geirr";
                            colors = new[] { "Crimson", "Cobalt", "Emerald", "Amber", "Violet", "Aqua" };
                        }
                        else if (opId >= 26 && opId <= 29)
                        {
                            groupName = "Projectile Array";
                            colors = new[] { "Red", "Blue", "Green", "Yellow", "Violet", "Aqua" };
                        }

                        if (colors != null)
                        {
                            var color = colors[rand.Next(colors.Length)];
                            string resourceTemplateName;

                            if (groupName == "Storm's Cape")
                                resourceTemplateName = $"Storm's {color} Cape";
                            else if (groupName == "Assassin's Choker")
                                resourceTemplateName = $"Assassin's {color} Choker";
                            else if (groupName == "Geirr")
                                resourceTemplateName = $"{color} Geirr";
                            else
                                resourceTemplateName = $"{color} {groupName}";

                            var itemTemplate = _dbContext.ItemTemplates.FirstOrDefault(t => t.Name == resourceTemplateName);
                            if (itemTemplate != null)
                            {
                                var playerItem = _dbContext.PlayerInventoryItems
                                    .FirstOrDefault(pi => pi.PlayerProfileId == profile.Id && pi.ItemTemplateId == itemTemplate.Id);

                                if (playerItem == null)
                                {
                                    playerItem = new PlayerInventoryItem
                                    {
                                        PlayerProfileId = profile.Id,
                                        ItemTemplateId = itemTemplate.Id,
                                        Quantity = 1
                                    };
                                    _dbContext.PlayerInventoryItems.Add(playerItem);
                                }
                                else
                                {
                                    playerItem.Quantity++;
                                }

                                resourceDropped = true;
                                droppedResourceName = itemTemplate.Name;
                                droppedResourceImage = itemTemplate.ImageFileName;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[MissionEngine] Failed to award resource drop: {ex.Message}");
            }

            SavePlayerMissionProgress(profile, progressState);
            _dbContext.SaveChanges();

            var clearMessage = isInventoryFull
                ? $"⚠️ TARGET BOSS NEUTRALIZED // INVENTORY FULL ({profile.MaxCardCapacity}/{profile.MaxCardCapacity}) - NO BOSS RECOVERED! Unlocked sector: {progressState.UnlockedOperationId}-{progressState.UnlockedMissionId}!"
                : $"Target boss neutralized! Clearance level sync complete! Unlocked sector: {progressState.UnlockedOperationId}-{progressState.UnlockedMissionId}!";

            if (resourceDropped)
            {
                clearMessage += $" // RECOVERED TACTICAL RESOURCE: {droppedResourceName}!";
            }

            return new BossBattleResult
            {
                Success = true,
                DroppedCardName = droppedCardName,
                Message = clearMessage,
                UnlockedOperationId = progressState.UnlockedOperationId,
                UnlockedMissionId = progressState.UnlockedMissionId,
                ResourceDropped = resourceDropped,
                DroppedResourceName = droppedResourceName,
                DroppedResourceImage = droppedResourceImage,
                LogLines = combatLogLines
            };
        }
    }
}
