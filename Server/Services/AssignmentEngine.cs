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
    public class AssignmentEngine : IAssignmentEngine
    {
        private readonly ILogger<AssignmentEngine> _logger;
        private readonly MwohDbContext _dbContext;
        private static readonly List<AssignmentTemplate> _templates = new();
        private static readonly object _lock = new();

        public AssignmentEngine(ILogger<AssignmentEngine> logger, MwohDbContext dbContext)
        {
            _logger = logger;
            _dbContext = dbContext;
            
            // Reload templates on initialization if not already loaded
            lock (_lock)
            {
                if (_templates.Count == 0)
                {
                    ReloadTemplates();
                }
            }
        }

        public void ReloadTemplates()
        {
            lock (_lock)
            {
                _templates.Clear();
                
                var baseConfigPath = Path.Combine(Directory.GetCurrentDirectory(), "Config", "assignments_config.json");
                
                try
                {
                    if (File.Exists(baseConfigPath))
                    {
                        var jsonText = File.ReadAllText(baseConfigPath);
                        var baseTemplates = JsonSerializer.Deserialize<List<AssignmentTemplate>>(jsonText, new JsonSerializerOptions
                        {
                            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                            PropertyNameCaseInsensitive = true
                        });
                        if (baseTemplates != null)
                        {
                            _templates.AddRange(baseTemplates);
                            _logger.LogInformation($"[AssignmentEngine] Loaded {_templates.Count} base assignments from {baseConfigPath}.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[AssignmentEngine] Failed to load assignments_config.json: {ex.Message}");
                }

                // Check for custom player-created assignments in Config/Assignments/ directory
                var customDir = Path.Combine(Directory.GetCurrentDirectory(), "Config", "Assignments");
                try
                {
                    if (!Directory.Exists(customDir))
                    {
                        Directory.CreateDirectory(customDir);
                    }
                    else
                    {
                        var customFiles = Directory.GetFiles(customDir, "*.json");
                        foreach (var file in customFiles)
                        {
                            var jsonText = File.ReadAllText(file);
                            var customTemplates = JsonSerializer.Deserialize<List<AssignmentTemplate>>(jsonText, new JsonSerializerOptions
                            {
                                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                                PropertyNameCaseInsensitive = true
                            });
                            if (customTemplates != null)
                            {
                                _templates.AddRange(customTemplates);
                                _logger.LogInformation($"[AssignmentEngine] Loaded {customTemplates.Count} custom assignments from {Path.GetFileName(file)}.");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[AssignmentEngine] Failed to load custom assignments: {ex.Message}");
                }

                // Seed dynamic hardcoded fallbacks if no assignments could be loaded
                if (_templates.Count == 0)
                {
                    SeedFallbacks();
                    _logger.LogWarning("[AssignmentEngine] Config files missing or empty. Seeded dynamic onboarding fallbacks.");
                }
            }
        }

        private void SeedFallbacks()
        {
            // Seed base onboarding assignments
            _templates.Add(new AssignmentTemplate { Id = "init_rally_draw", Title = "Draw one Rally Card Pack", Description = "Draw one Rally Card Pack at the mainframe.", GoalType = "DrawRallyPack", GoalTarget = 1, RewardType = "RallyPoints", RewardValue = 2000, GroupName = "Initial" });
            _templates.Add(new AssignmentTemplate { Id = "init_boost_card", Title = "Boost One Card", Description = "Inject ISO-8 Serum into one card to raise clearance level.", GoalType = "EnhanceCard", GoalTarget = 1, RewardType = "Silver", RewardValue = 50000, GroupName = "Initial" });
            _templates.Add(new AssignmentTemplate { Id = "init_fight_battle", Title = "Fight in One Battle", Description = "Engage in ranked PvP combat once.", GoalType = "PvpBattle", GoalTarget = 1, RewardType = "Item", RewardValue = 3, RewardQuantity = 1, GroupName = "Initial" }); // Personal Power Pack (ID: 3)
            _templates.Add(new AssignmentTemplate { Id = "init_complete_op2", Title = "Finish Operation 2", Description = "Complete progressive Story Operation 2.", GoalType = "CompleteOperation", GoalTarget = 2, RewardType = "Item", RewardValue = 1, RewardQuantity = 1, GroupName = "Initial" }); // Personal Energy Pack (ID: 1)
            
            // Seed level milestones
            _templates.Add(new AssignmentTemplate { Id = "lvl_10", Title = "Achieve Level 10", Description = "Unlock Clearance level 10.", GoalType = "LevelUp", GoalTarget = 10, RewardType = "Item", RewardValue = 2, RewardQuantity = 1, GroupName = "Level" }); // Ultimate ticket
            _templates.Add(new AssignmentTemplate { Id = "lvl_15", Title = "Achieve Level 15", Description = "Unlock Clearance level 15.", GoalType = "LevelUp", GoalTarget = 15, RewardType = "CardStock", RewardValue = 5, GroupName = "Level" });
            _templates.Add(new AssignmentTemplate { Id = "lvl_20", Title = "Achieve Level 20", Description = "Unlock Clearance level 20.", GoalType = "LevelUp", GoalTarget = 20, RewardType = "Item", RewardValue = 2, RewardQuantity = 1, GroupName = "Level" });
        }

        public List<AssignmentTemplate> GetActiveTemplates()
        {
            var now = DateTime.UtcNow;
            lock (_lock)
            {
                return _templates.Where(t =>
                {
                    if (GameplaySettings.IgnoreAssignmentDates) return true;
                    if (t.StartDate.HasValue && now < t.StartDate.Value) return false;
                    if (t.EndDate.HasValue && now > t.EndDate.Value) return false;
                    return true;
                }).ToList();
            }
        }

        public List<PlayerAssignmentProgressDto> GetPlayerProgress(int profileId)
        {
            var profile = _dbContext.Profiles
                .FirstOrDefault(p => p.Id == profileId);

            if (profile == null) return new List<PlayerAssignmentProgressDto>();

            var now = DateTime.UtcNow;
            var activeTemplates = GetActiveTemplates();
            
            var progressRecords = _dbContext.PlayerAssignmentProgress
                .Where(p => p.PlayerProfileId == profileId)
                .ToList();

            var result = new List<PlayerAssignmentProgressDto>();

            foreach (var template in activeTemplates)
            {
                var record = progressRecords.FirstOrDefault(r => r.AssignmentId == template.Id);
                
                var currentProgress = record?.CurrentProgress ?? 0;
                var isCompleted = record?.IsCompleted ?? false;
                var isClaimed = record?.IsClaimed ?? false;

                // Auto-evaluate level milestones dynamically on fetching
                if (template.GoalType == "LevelUp" && !isCompleted)
                {
                    if (profile.Level >= template.GoalTarget)
                    {
                        currentProgress = profile.Level;
                        isCompleted = true;
                        
                        if (record == null)
                        {
                            record = new PlayerAssignmentProgress
                            {
                                PlayerProfileId = profileId,
                                AssignmentId = template.Id,
                                CurrentProgress = currentProgress,
                                IsCompleted = true,
                                IsClaimed = false,
                                LastUpdated = DateTime.UtcNow
                            };
                            _dbContext.PlayerAssignmentProgress.Add(record);
                        }
                        else
                        {
                            record.CurrentProgress = currentProgress;
                            record.IsCompleted = true;
                            record.LastUpdated = DateTime.UtcNow;
                        }
                        _dbContext.SaveChanges();
                    }
                }
                // Auto-evaluate login milestones dynamically on fetching
                if (template.GoalType == "LoginTomorrow" && !isCompleted)
                {
                    if (record == null)
                    {
                        record = new PlayerAssignmentProgress
                        {
                            PlayerProfileId = profileId,
                            AssignmentId = template.Id,
                            CurrentProgress = 0,
                            IsCompleted = false,
                            IsClaimed = false,
                            LastUpdated = DateTime.UtcNow
                        };
                        _dbContext.PlayerAssignmentProgress.Add(record);
                        _dbContext.SaveChanges();
                    }
                    else if (record.LastUpdated.Date < DateTime.UtcNow.Date)
                    {
                        currentProgress = 1;
                        isCompleted = true;
                        record.CurrentProgress = 1;
                        record.IsCompleted = true;
                        record.LastUpdated = DateTime.UtcNow;
                        _dbContext.SaveChanges();
                    }
                }

                var secondsRemaining = -1;
                if (!GameplaySettings.IgnoreAssignmentDates && template.EndDate.HasValue)
                {
                    var diff = template.EndDate.Value - now;
                    secondsRemaining = diff.TotalSeconds > 0 ? (int)diff.TotalSeconds : 0;
                }

                result.Add(new PlayerAssignmentProgressDto
                {
                    Template = template,
                    CurrentProgress = currentProgress,
                    IsCompleted = isCompleted,
                    IsClaimed = isClaimed,
                    IsActive = true,
                    SecondsRemaining = secondsRemaining
                });
            }

            return result;
        }

        public void RecordEvent(int profileId, GoalType goalType, int value)
        {
            var profile = _dbContext.Profiles.FirstOrDefault(p => p.Id == profileId);
            if (profile == null) return;

            var goalTypeStr = goalType.ToString();
            var activeTemplates = GetActiveTemplates().Where(t => t.GoalType == goalTypeStr && !t.IsCompletionBonus).ToList();
            if (!activeTemplates.Any()) return;

            var progressRecords = _dbContext.PlayerAssignmentProgress
                .Where(p => p.PlayerProfileId == profileId)
                .ToList();

            var databaseModified = false;

            foreach (var template in activeTemplates)
            {
                var record = progressRecords.FirstOrDefault(r => r.AssignmentId == template.Id);
                
                if (record != null && record.IsCompleted) continue;

                if (record == null)
                {
                    record = new PlayerAssignmentProgress
                    {
                        PlayerProfileId = profileId,
                        AssignmentId = template.Id,
                        CurrentProgress = 0,
                        IsCompleted = false,
                        IsClaimed = false,
                        LastUpdated = DateTime.UtcNow
                    };
                    _dbContext.PlayerAssignmentProgress.Add(record);
                    progressRecords.Add(record);
                }

                // Increment or set progress value based on goal type
                if (goalType == GoalType.LevelUp)
                {
                    record.CurrentProgress = value;
                }
                else if (goalType == GoalType.CompleteOperation)
                {
                    // If target is Operation X, value represents the cleared operation ID
                    if (value >= template.GoalTarget)
                    {
                        record.CurrentProgress = 1;
                    }
                }
                else if (goalType == GoalType.WinStreak)
                {
                    // Streaks are tracked as raw value set directly
                    record.CurrentProgress = Math.Max(record.CurrentProgress, value);
                }
                else
                {
                    record.CurrentProgress += value;
                }

                // Check completion bounds
                if (record.CurrentProgress >= template.GoalTarget)
                {
                    record.CurrentProgress = template.GoalTarget;
                    record.IsCompleted = true;
                    _logger.LogInformation($"[AssignmentEngine] Assignment Completed: profile {profileId} finished {template.Id} ({template.Title})!");
                }

                record.LastUpdated = DateTime.UtcNow;
                databaseModified = true;
            }

            if (databaseModified)
            {
                _dbContext.SaveChanges();
            }
        }

        public ClaimResult ClaimReward(int profileId, string assignmentId)
        {
            var result = new ClaimResult { Success = false };

            var profile = _dbContext.Profiles.FirstOrDefault(p => p.Id == profileId);
            if (profile == null)
            {
                result.Message = "⚠️ DOSSIER LINK ERROR // Agent profile mismatch.";
                return result;
            }

            lock (_lock)
            {
                var template = _templates.FirstOrDefault(t => t.Id == assignmentId);
                if (template == null)
                {
                    result.Message = "⚠️ CLASSIFICATION ERROR // Quest blueprint not found.";
                    return result;
                }

                var record = _dbContext.PlayerAssignmentProgress
                    .FirstOrDefault(r => r.PlayerProfileId == profileId && r.AssignmentId == assignmentId);

                if (record == null || !record.IsCompleted)
                {
                    result.Message = "⚠️ SECURITY LOCK // Quest criteria not fully achieved.";
                    return result;
                }

                if (record.IsClaimed)
                {
                    result.Message = "⚠️ ACCESS RESTRICTED // Clearance rewards already claimed.";
                    return result;
                }

                // Process Reward disbursement
                var rewardDescription = "";
                if (string.Equals(template.RewardType, "Silver", StringComparison.OrdinalIgnoreCase))
                {
                    profile.SilverBalance += template.RewardValue;
                    rewardDescription = $"{template.RewardValue:N0} Silver Credits";
                }
                else if (string.Equals(template.RewardType, "RallyPoints", StringComparison.OrdinalIgnoreCase))
                {
                    profile.RallyPoints += template.RewardValue;
                    rewardDescription = $"{template.RewardValue:N0} Rally Points";
                }
                else if (string.Equals(template.RewardType, "MobaCoin", StringComparison.OrdinalIgnoreCase))
                {
                    profile.MobaCoinBalance += template.RewardValue;
                    rewardDescription = $"{template.RewardValue:N0} MobaCoins";
                }
                else if (string.Equals(template.RewardType, "CardStock", StringComparison.OrdinalIgnoreCase))
                {
                    profile.MaxCardCapacity += template.RewardValue;
                    rewardDescription = $"+{template.RewardValue} Hero Slot Expansion";
                }
                else if (string.Equals(template.RewardType, "Item", StringComparison.OrdinalIgnoreCase))
                {
                    var itemTemp = _dbContext.ItemTemplates.FirstOrDefault(t => t.Id == template.RewardValue)
                                   ?? _dbContext.ItemTemplates.FirstOrDefault(t => t.Name.Contains("Energy Iso-8") && template.RewardValue == 1)
                                   ?? _dbContext.ItemTemplates.FirstOrDefault(t => t.Name.Contains("Ultimate Card Pack Ticket") && template.RewardValue == 2)
                                   ?? _dbContext.ItemTemplates.FirstOrDefault(t => t.Name.Contains("Attack Iso-8") && template.RewardValue == 3)
                                   ?? _dbContext.ItemTemplates.FirstOrDefault(t => t.Name.Contains("Shield Barrier") && template.RewardValue == 5)
                                   ?? _dbContext.ItemTemplates.FirstOrDefault();

                    if (itemTemp == null)
                    {
                        result.Message = "⚠️ TRANSACTION EXCEPTION // Reward item blueprint corrupted.";
                        return result;
                    }

                    var invItem = _dbContext.PlayerInventoryItems
                        .FirstOrDefault(pi => pi.PlayerProfileId == profileId && pi.ItemTemplateId == itemTemp.Id);

                    if (invItem == null)
                    {
                        invItem = new PlayerInventoryItem
                        {
                            PlayerProfileId = profileId,
                            ItemTemplateId = itemTemp.Id,
                            Quantity = template.RewardQuantity
                        };
                        _dbContext.PlayerInventoryItems.Add(invItem);
                    }
                    else
                    {
                        invItem.Quantity += template.RewardQuantity;
                    }
                    rewardDescription = $"{template.RewardQuantity}x {itemTemp.Name}";
                }
                else if (string.Equals(template.RewardType, "Card", StringComparison.OrdinalIgnoreCase))
                {
                    var cardTemp = _dbContext.CardTemplates.FirstOrDefault(t => t.Id == template.RewardValue)
                                   ?? _dbContext.CardTemplates.FirstOrDefault(t => t.Title == "Leopardess Tigra")
                                   ?? _dbContext.CardTemplates.FirstOrDefault(t => t.Title == "Cosmic Energy Havok")
                                   ?? _dbContext.CardTemplates.FirstOrDefault();

                    if (cardTemp == null)
                    {
                        result.Message = "⚠️ TRANSACTION EXCEPTION // Reward card blueprint corrupted.";
                        return result;
                    }

                    int currentCardCount = _dbContext.PlayerCards.Count(pc => pc.PlayerProfileId == profileId);
                    if (currentCardCount >= profile.MaxCardCapacity)
                    {
                        result.Message = $"⚠️ STORAGE OVERFLOW // Hero capacity exceeded ({currentCardCount}/{profile.MaxCardCapacity}). Purge squad items at lab first.";
                        return result;
                    }

                    for (int i = 0; i < template.RewardQuantity; i++)
                    {
                        var newCard = new PlayerCard { PlayerProfileId = profileId };
                        newCard.InitializeStats(cardTemp, GameplaySettings.DefaultMasteryPercentage);
                        _dbContext.PlayerCards.Add(newCard);
                    }
                    rewardDescription = $"{template.RewardQuantity}x {cardTemp.Title}";
                }

                // Mark claimed
                record.IsClaimed = true;
                _dbContext.SaveChanges();

                result.Success = true;
                result.SilverBalance = profile.SilverBalance;
                result.RallyPoints = profile.RallyPoints;
                result.MobaCoinBalance = profile.MobaCoinBalance;
                result.RewardText = rewardDescription;
                result.Message = $"🎉 SECURED REWARD // Successfully acquired {rewardDescription}!";

                // Trigger Batch Bonus completion checks (Special Event groups)
                if (!string.IsNullOrEmpty(template.GroupName) && template.GroupName.StartsWith("Special Assignment"))
                {
                    CheckBatchCompletionBonus(profileId, template.GroupName, template.Batch, result);
                }

                return result;
            }
        }

        private void CheckBatchCompletionBonus(int profileId, string groupName, int batch, ClaimResult claimResult)
        {
            lock (_lock)
            {
                // Find completion bonus blueprint for this event batch
                var bonusTemplate = _templates.FirstOrDefault(t => t.GroupName == groupName && t.Batch == batch && t.IsCompletionBonus);
                if (bonusTemplate == null) return;

                // Check if already claimed
                var bonusRecord = _dbContext.PlayerAssignmentProgress
                    .FirstOrDefault(r => r.PlayerProfileId == profileId && r.AssignmentId == bonusTemplate.Id);
                
                if (bonusRecord != null && bonusRecord.IsClaimed) return;

                // Grab regular assignments in this specific event batch
                var regularTemplates = _templates.Where(t => t.GroupName == groupName && t.Batch == batch && !t.IsCompletionBonus).ToList();
                if (regularTemplates.Count == 0) return;

                var regularIds = regularTemplates.Select(t => t.Id).ToList();

                // Fetch database progress status for the regular assignments in this event batch
                var claimedCount = _dbContext.PlayerAssignmentProgress
                    .Count(r => r.PlayerProfileId == profileId && regularIds.Contains(r.AssignmentId) && r.IsClaimed);

                if (claimedCount == regularTemplates.Count)
                {
                    // Success! Grant Batch Bonus
                    var profile = _dbContext.Profiles.FirstOrDefault(p => p.Id == profileId);
                    if (profile == null) return;

                    var bonusDescription = "";
                    if (string.Equals(bonusTemplate.RewardType, "Card", StringComparison.OrdinalIgnoreCase))
                    {
                        var cardTemp = _dbContext.CardTemplates.FirstOrDefault(t => t.Id == bonusTemplate.RewardValue)
                                       ?? _dbContext.CardTemplates.FirstOrDefault(t => t.Title == "Leopardess Tigra")
                                       ?? _dbContext.CardTemplates.FirstOrDefault(t => t.Title == "Cosmic Energy Havok")
                                       ?? _dbContext.CardTemplates.FirstOrDefault();

                        if (cardTemp != null)
                        {
                            int currentCardCount = _dbContext.PlayerCards.Count(pc => pc.PlayerProfileId == profileId);
                            if (currentCardCount >= profile.MaxCardCapacity)
                            {
                                // Fail gracefully by skipping this automatic drop and leaving a logger entry, 
                                // but we will still push the bonus completion status to completed so they get it when slots open.
                                _logger.LogWarning($"[AssignmentEngine] Profile {profileId} missed Batch Bonus {cardTemp.Title} due to full slots.");
                                return;
                            }

                            for (int i = 0; i < bonusTemplate.RewardQuantity; i++)
                            {
                                var newCard = new PlayerCard { PlayerProfileId = profileId };
                                newCard.InitializeStats(cardTemp, GameplaySettings.DefaultMasteryPercentage);
                                _dbContext.PlayerCards.Add(newCard);
                            }
                            bonusDescription = $"{bonusTemplate.RewardQuantity}x {cardTemp.Title}";
                        }
                    }
                    else if (string.Equals(bonusTemplate.RewardType, "Item", StringComparison.OrdinalIgnoreCase))
                    {
                        var itemTemp = _dbContext.ItemTemplates.FirstOrDefault(t => t.Id == bonusTemplate.RewardValue)
                                       ?? _dbContext.ItemTemplates.FirstOrDefault(t => t.Name.Contains("Energy Iso-8") && bonusTemplate.RewardValue == 1)
                                       ?? _dbContext.ItemTemplates.FirstOrDefault(t => t.Name.Contains("Ultimate Card Pack Ticket") && bonusTemplate.RewardValue == 2)
                                       ?? _dbContext.ItemTemplates.FirstOrDefault(t => t.Name.Contains("Attack Iso-8") && bonusTemplate.RewardValue == 3)
                                       ?? _dbContext.ItemTemplates.FirstOrDefault(t => t.Name.Contains("Shield Barrier") && bonusTemplate.RewardValue == 5)
                                       ?? _dbContext.ItemTemplates.FirstOrDefault();

                        if (itemTemp != null)
                        {
                            var invItem = _dbContext.PlayerInventoryItems
                                .FirstOrDefault(pi => pi.PlayerProfileId == profileId && pi.ItemTemplateId == itemTemp.Id);

                            if (invItem == null)
                            {
                                invItem = new PlayerInventoryItem
                                {
                                    PlayerProfileId = profileId,
                                    ItemTemplateId = itemTemp.Id,
                                    Quantity = bonusTemplate.RewardQuantity
                                };
                                _dbContext.PlayerInventoryItems.Add(invItem);
                            }
                            else
                            {
                                invItem.Quantity += bonusTemplate.RewardQuantity;
                            }
                            bonusDescription = $"{bonusTemplate.RewardQuantity}x {itemTemp.Name}";
                        }
                    }
                    else if (string.Equals(bonusTemplate.RewardType, "Silver", StringComparison.OrdinalIgnoreCase))
                    {
                        profile.SilverBalance += bonusTemplate.RewardValue;
                        bonusDescription = $"{bonusTemplate.RewardValue:N0} Silver Credits";
                    }

                    if (bonusRecord == null)
                    {
                        bonusRecord = new PlayerAssignmentProgress
                        {
                            PlayerProfileId = profileId,
                            AssignmentId = bonusTemplate.Id,
                            CurrentProgress = 1,
                            IsCompleted = true,
                            IsClaimed = true,
                            LastUpdated = DateTime.UtcNow
                        };
                        _dbContext.PlayerAssignmentProgress.Add(bonusRecord);
                    }
                    else
                    {
                        bonusRecord.CurrentProgress = 1;
                        bonusRecord.IsCompleted = true;
                        bonusRecord.IsClaimed = true;
                        bonusRecord.LastUpdated = DateTime.UtcNow;
                    }

                    _dbContext.SaveChanges();

                    claimResult.SilverBalance = profile.SilverBalance;
                    claimResult.RallyPoints = profile.RallyPoints;
                    claimResult.MobaCoinBalance = profile.MobaCoinBalance;
                    claimResult.Message += $" \n🌟 BATCH COMPLETION BONUS UNLOCKED // Secure tactical asset: {bonusDescription}!";
                    _logger.LogInformation($"[AssignmentEngine] Profile {profileId} completed Batch Bonus {bonusTemplate.Id}!");
                }
            }
        }
    }
}
