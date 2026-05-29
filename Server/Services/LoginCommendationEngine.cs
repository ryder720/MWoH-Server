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
    public class LoginCommendationEngine : ILoginCommendationEngine
    {
        private readonly ILogger<LoginCommendationEngine> _logger;
        private readonly MwohDbContext _dbContext;
        private static readonly List<LoginCampaignTemplate> _templates = new();
        private static readonly object _lock = new();

        public LoginCommendationEngine(ILogger<LoginCommendationEngine> logger, MwohDbContext dbContext)
        {
            _logger = logger;
            _dbContext = dbContext;

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

                var baseConfigPath = Path.Combine(Directory.GetCurrentDirectory(), "Config", "login_commendations_config.json");
                try
                {
                    if (File.Exists(baseConfigPath))
                    {
                        var jsonText = File.ReadAllText(baseConfigPath);
                        var baseTemplates = JsonSerializer.Deserialize<List<LoginCampaignTemplate>>(jsonText, new JsonSerializerOptions
                        {
                            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                            PropertyNameCaseInsensitive = true
                        });
                        if (baseTemplates != null)
                        {
                            _templates.AddRange(baseTemplates);
                            _logger.LogInformation($"[LoginCommendationEngine] Loaded {_templates.Count} base daily calendars from {baseConfigPath}.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[LoginCommendationEngine] Failed to load login_commendations_config.json: {ex.Message}");
                }

                // Check for custom player-created login calendars in Config/LoginCommendations/ directory
                var customDir = Path.Combine(Directory.GetCurrentDirectory(), "Config", "LoginCommendations");
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
                            var customTemplates = JsonSerializer.Deserialize<List<LoginCampaignTemplate>>(jsonText, new JsonSerializerOptions
                            {
                                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                               PropertyNameCaseInsensitive = true
                            });
                            if (customTemplates != null)
                            {
                                _templates.AddRange(customTemplates);
                                _logger.LogInformation($"[LoginCommendationEngine] Loaded {customTemplates.Count} custom daily calendars from {Path.GetFileName(file)}.");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[LoginCommendationEngine] Failed to load custom daily calendars: {ex.Message}");
                }

                // Hardcoded fallback seeding if configurations are missing
                if (_templates.Count == 0)
                {
                    SeedFallbacks();
                    _logger.LogWarning("[LoginCommendationEngine] Config files missing. Seeded example onboarding login campaign.");
                }
            }
        }

        private void SeedFallbacks()
        {
            var defaultRewards = new List<LoginRewardTemplate>
            {
                new() { Day = 1, RewardType = "Silver", RewardValue = 50000, RewardQuantity = 1 },
                new() { Day = 2, RewardType = "RallyPoints", RewardValue = 2000, RewardQuantity = 1 },
                new() { Day = 3, RewardType = "Item", RewardValue = 1, RewardQuantity = 1 }, // Energy Iso-8 (L)
                new() { Day = 4, RewardType = "Item", RewardValue = 5, RewardQuantity = 1 }, // Shield Barrier
                new() { Day = 5, RewardType = "MobaCoin", RewardValue = 100, RewardQuantity = 1 },
                new() { Day = 6, RewardType = "Item", RewardValue = 2, RewardQuantity = 1 }, // Ultimate Gacha Ticket
                new() { Day = 7, RewardType = "Card", RewardValue = 0, RewardQuantity = 1 } // [Ho Ho Ho] Spider-Man
            };

            _templates.Add(new LoginCampaignTemplate
            {
                Id = "example_holiday_login",
                Title = "S.H.I.E.L.D. Holiday Commendation",
                Description = "A seasonal thank-you daily reward calendar directly from S.H.I.E.L.D. Command.",
                Rewards = defaultRewards,
                IsActive = true,
                FallbackRewardType = "Silver",
                FallbackRewardValue = 10000,
                FallbackRewardQuantity = 1
            });
        }

        public List<LoginCampaignTemplate> GetActiveCampaigns()
        {
            var now = DateTime.UtcNow;
            lock (_lock)
            {
                return _templates.Where(t =>
                {
                    if (GameplaySettings.IgnoreLoginCommendationDates) return true;
                    if (!t.IsActive) return false;
                    if (t.StartDate.HasValue && now < t.StartDate.Value) return false;
                    if (t.EndDate.HasValue && now > t.EndDate.Value) return false;
                    return true;
                }).ToList();
            }
        }

        private DateTime GetResetDay(DateTime time)
        {
            if (time == DateTime.MinValue) return DateTime.MinValue;
            // Daily reset shifts at 5 AM Eastern Time. 5 AM ET is 9:00 AM UTC.
            // Subtracting 5 hours aligns the 5:00 AM daily reset window exactly with 12:00 AM (midnight) of that coordinate day.
            return time.AddHours(-5).Date;
        }

        public List<PlayerLoginCommendationDto> GetPlayerProgress(int profileId)
        {
            var activeCampaigns = GetActiveCampaigns();
            var progressRecords = _dbContext.PlayerLoginCommendations
                .Where(p => p.PlayerProfileId == profileId)
                .ToList();

            var result = new List<PlayerLoginCommendationDto>();
            var now = DateTime.UtcNow;

            foreach (var campaign in activeCampaigns)
            {
                var record = progressRecords.FirstOrDefault(r => r.CampaignId == campaign.Id);
                var totalLogins = record?.TotalLogins ?? 0;
                var lastLogin = record?.LastLoginDate ?? DateTime.MinValue;

                var alreadyLoggedToday = record != null && GetResetDay(now) <= GetResetDay(lastLogin);
                var claimedDays = record != null 
                    ? JsonSerializer.Deserialize<List<int>>(record.ClaimedDaysJson) ?? new List<int>()
                    : new List<int>();

                var nextDayToClaim = alreadyLoggedToday ? -1 : totalLogins + 1;

                // Calculate seconds until next reset (Tomorrow at 5:00 AM ET)
                var nextReset = GetResetDay(now).AddDays(1).AddHours(5);
                var diff = nextReset - now;
                var secondsUntilReset = diff.TotalSeconds > 0 ? (int)diff.TotalSeconds : 0;

                result.Add(new PlayerLoginCommendationDto
                {
                    Campaign = campaign,
                    TotalLogins = totalLogins,
                    AlreadyLoggedToday = alreadyLoggedToday,
                    ClaimedDays = claimedDays,
                    NextDayToClaim = nextDayToClaim,
                    SecondsUntilReset = secondsUntilReset
                });
            }

            return result;
        }

        public LoginProcessResult ProcessDailyLogin(int profileId)
        {
            var result = new LoginProcessResult { UnlockedReward = false };

            var profile = _dbContext.Profiles.FirstOrDefault(p => p.Id == profileId);
            if (profile == null) return result;

            var activeCampaigns = GetActiveCampaigns();
            if (activeCampaigns.Count == 0) return result;

            var progressRecords = _dbContext.PlayerLoginCommendations
                .Where(p => p.PlayerProfileId == profileId)
                .ToList();

            var now = DateTime.UtcNow;
            var databaseModified = false;
            var rewardDescriptions = new List<string>();

            lock (_lock)
            {
                foreach (var campaign in activeCampaigns)
                {
                    var record = progressRecords.FirstOrDefault(r => r.CampaignId == campaign.Id);
                    if (record == null)
                    {
                        record = new PlayerLoginCommendationProgress
                        {
                            PlayerProfileId = profileId,
                            CampaignId = campaign.Id,
                            TotalLogins = 0,
                            LastLoginDate = DateTime.MinValue,
                            ClaimedDaysJson = "[]"
                        };
                        _dbContext.PlayerLoginCommendations.Add(record);
                        progressRecords.Add(record);
                    }

                    var lastResetDay = GetResetDay(record.LastLoginDate);
                    var currentResetDay = GetResetDay(now);

                    if (currentResetDay > lastResetDay)
                    {
                        var prevLastLoginDate = record.LastLoginDate;
                        // Player logged in on a new day!
                        record.TotalLogins++;
                        record.LastLoginDate = now;

                        var claimedDays = JsonSerializer.Deserialize<List<int>>(record.ClaimedDaysJson) ?? new List<int>();
                        var currentDay = record.TotalLogins;

                        // Check reward blueprint
                        var reward = campaign.Rewards.FirstOrDefault(r => r.Day == currentDay);
                        if (reward == null)
                        {
                            // Beyond final day, disburse fallback rewards
                            reward = new LoginRewardTemplate
                            {
                                Day = currentDay,
                                RewardType = campaign.FallbackRewardType,
                                RewardValue = campaign.FallbackRewardValue,
                                RewardQuantity = campaign.FallbackRewardQuantity
                            };
                        }

                        // Disburse Reward
                        var disburseMessage = "";
                        var success = DisburseReward(profile, reward, ref disburseMessage);
                        
                        if (success)
                        {
                            claimedDays.Add(currentDay);
                            record.ClaimedDaysJson = JsonSerializer.Serialize(claimedDays);
                            databaseModified = true;

                            result.UnlockedReward = true;
                            result.CampaignTitle = campaign.Title;
                            result.DayNumber = currentDay;
                            rewardDescriptions.Add($"{campaign.Title} (Day {currentDay}): {disburseMessage}");
                        }
                        else
                        {
                            // If card capacity is full, revert the day increment so they get it when slots open on next login
                            record.TotalLogins--;
                            record.LastLoginDate = prevLastLoginDate;
                            _logger.LogWarning($"[LoginCommendationEngine] Reverted login progression for profileId {profileId} due to full slots.");
                            result.Message = disburseMessage;
                            return result;
                        }
                    }
                }

                if (databaseModified)
                {
                    _dbContext.SaveChanges();

                    result.SilverBalance = profile.SilverBalance;
                    result.RallyPoints = profile.RallyPoints;
                    result.MobaCoinBalance = profile.MobaCoinBalance;
                    result.RewardText = string.Join("\n", rewardDescriptions);
                    result.Message = $"🎉 DAILY LOGINS STAMPED // Successfully acquired:\n{string.Join("\n", rewardDescriptions)}";
                    _logger.LogInformation($"[LoginCommendationEngine] Stamped daily login rewards for profileId {profileId}.");
                }
            }

            return result;
        }

        private bool DisburseReward(PlayerProfile profile, LoginRewardTemplate reward, ref string disburseMessage)
        {
            if (string.Equals(reward.RewardType, "Silver", StringComparison.OrdinalIgnoreCase))
            {
                profile.SilverBalance += reward.RewardValue;
                disburseMessage = $"🪙 {reward.RewardValue:N0} Silver Credits";
                return true;
            }
            else if (string.Equals(reward.RewardType, "RallyPoints", StringComparison.OrdinalIgnoreCase))
            {
                profile.RallyPoints += reward.RewardValue;
                disburseMessage = $"⚡ {reward.RewardValue:N0} Rally Points";
                return true;
            }
            else if (string.Equals(reward.RewardType, "MobaCoin", StringComparison.OrdinalIgnoreCase))
            {
                profile.MobaCoinBalance += reward.RewardValue;
                disburseMessage = $"🪙 {reward.RewardValue:N0} MobaCoins";
                return true;
            }
            else if (string.Equals(reward.RewardType, "CardStock", StringComparison.OrdinalIgnoreCase))
            {
                profile.MaxCardCapacity += reward.RewardValue;
                disburseMessage = $"+{reward.RewardValue} Hero Capacity slots";
                return true;
            }
            else if (string.Equals(reward.RewardType, "Item", StringComparison.OrdinalIgnoreCase))
            {
                var itemTemp = _dbContext.ItemTemplates.FirstOrDefault(t => t.Id == reward.RewardValue)
                               ?? _dbContext.ItemTemplates.FirstOrDefault(t => t.Name.Contains("Energy Iso-8") && reward.RewardValue == 1)
                               ?? _dbContext.ItemTemplates.FirstOrDefault(t => t.Name.Contains("Ultimate Card Pack Ticket") && reward.RewardValue == 2)
                               ?? _dbContext.ItemTemplates.FirstOrDefault(t => t.Name.Contains("Attack Iso-8") && reward.RewardValue == 3)
                               ?? _dbContext.ItemTemplates.FirstOrDefault(t => t.Name.Contains("Shield Barrier") && reward.RewardValue == 5)
                               ?? _dbContext.ItemTemplates.FirstOrDefault();

                if (itemTemp == null)
                {
                    disburseMessage = "⚠️ TRANSACTION EXCEPTION // Reward item blueprint corrupted.";
                    return false;
                }

                var invItem = _dbContext.PlayerInventoryItems
                    .FirstOrDefault(pi => pi.PlayerProfileId == profile.Id && pi.ItemTemplateId == itemTemp.Id);

                if (invItem == null)
                {
                    invItem = new PlayerInventoryItem
                    {
                        PlayerProfileId = profile.Id,
                        ItemTemplateId = itemTemp.Id,
                        Quantity = reward.RewardQuantity
                    };
                    _dbContext.PlayerInventoryItems.Add(invItem);
                }
                else
                {
                    invItem.Quantity += reward.RewardQuantity;
                }
                disburseMessage = $"📦 {reward.RewardQuantity}x {itemTemp.Name}";
                return true;
            }
            else if (string.Equals(reward.RewardType, "Card", StringComparison.OrdinalIgnoreCase))
            {
                var cardTemp = _dbContext.CardTemplates.FirstOrDefault(t => t.Id == reward.RewardValue)
                               ?? _dbContext.CardTemplates.FirstOrDefault(t => t.Title.Contains("Spider-Man"))
                               ?? _dbContext.CardTemplates.FirstOrDefault(t => t.Title.Contains("Captain America"))
                               ?? _dbContext.CardTemplates.FirstOrDefault();

                if (cardTemp == null)
                {
                    disburseMessage = "⚠️ TRANSACTION EXCEPTION // Reward card blueprint corrupted.";
                    return false;
                }

                int currentCardCount = _dbContext.PlayerCards.Count(pc => pc.PlayerProfileId == profile.Id);
                if (currentCardCount >= profile.MaxCardCapacity)
                {
                    disburseMessage = $"⚠️ STORAGE OVERFLOW // Max Hero slots reached ({currentCardCount}/{profile.MaxCardCapacity}). Clear room to claim Spider-Man!";
                    return false;
                }

                for (int i = 0; i < reward.RewardQuantity; i++)
                {
                    var newCard = new PlayerCard { PlayerProfileId = profile.Id };
                    newCard.InitializeStats(cardTemp, GameplaySettings.DefaultMasteryPercentage);
                    _dbContext.PlayerCards.Add(newCard);
                }
                disburseMessage = $"🃏 {reward.RewardQuantity}x {cardTemp.Title}";
                return true;
            }

            disburseMessage = "⚠️ UNKNOWN REWARD VALUE TYPE.";
            return false;
        }
    }
}
