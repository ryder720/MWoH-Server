using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MwohServer.Data;
using MwohServer.Models;

namespace MwohServer.Services
{
    public class EventEngine : IEventEngine
    {
        private readonly MwohDbContext _dbContext;
        private readonly ILogger<EventEngine> _logger;
        private readonly ICardAbilityEvaluator _abilityEvaluator;
        private static List<EventTemplate> _templates = new();
        private static readonly object _lock = new();

        public EventEngine(MwohDbContext dbContext, ILogger<EventEngine> logger, ICardAbilityEvaluator abilityEvaluator)
        {
            _dbContext = dbContext;
            _logger = logger;
            _abilityEvaluator = abilityEvaluator;
            
            // Load templates on first initialization if empty
            lock (_lock)
            {
                if (!_templates.Any())
                {
                    ReloadTemplates();
                }
            }
        }

        public void ReloadTemplates()
        {
            _logger.LogInformation("EventEngine: Reloading event templates from JSON config...");
            lock (_lock)
            {
                try
                {
                    var configPath = Path.Combine(Directory.GetCurrentDirectory(), "Config", "events_config.json");
                    if (File.Exists(configPath))
                    {
                        var json = File.ReadAllText(configPath);
                        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        var loaded = JsonSerializer.Deserialize<List<EventTemplate>>(json, options);
                        if (loaded != null)
                        {
                            _templates = loaded;
                            _logger.LogInformation($"EventEngine: Successfully loaded {_templates.Count} event templates.");
                        }
                    }
                    else
                    {
                        _logger.LogWarning($"EventEngine: Configuration file not found at '{configPath}'. Using empty templates list.");
                        _templates = new List<EventTemplate>();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"EventEngine: Failed to load event templates: {ex.Message}");
                    _templates = new List<EventTemplate>();
                }
            }
        }

        public List<EventTemplate> GetTemplates()
        {
            lock (_lock)
            {
                return new List<EventTemplate>(_templates);
            }
        }

        public EventTemplate? GetActiveEvent()
        {
            var now = DateTime.UtcNow;
            lock (_lock)
            {
                // First check for active override
                var activeOverride = _templates.FirstOrDefault(t => t.IsActiveOverride);
                if (activeOverride != null) return activeOverride;

                // Otherwise, search for any event in active/calculating timeframe
                return _templates.FirstOrDefault(t => now >= t.StartDate && now < t.ResultDate);
            }
        }

        public string GetEventState(EventTemplate temp)
        {
            var now = DateTime.UtcNow;

            if (temp.IsActiveOverride)
            {
                if (now < temp.EndDate) return "Active";
                if (now >= temp.EndDate && now < temp.ResultDate) return "Calculating";
                return "Completed";
            }

            if (now < temp.StartDate) return "Upcoming";
            if (now >= temp.StartDate && now < temp.EndDate) return "Active";
            if (now >= temp.EndDate && now < temp.ResultDate) return "Calculating";
            return "Completed";
        }

        public PlayerEventProgress GetPlayerProgress(int profileId, string eventId)
        {
            var progress = _dbContext.PlayerEventProgresses
                .FirstOrDefault(ep => ep.PlayerProfileId == profileId && ep.EventId == eventId);

            if (progress == null)
            {
                progress = new PlayerEventProgress
                {
                    PlayerProfileId = profileId,
                    EventId = eventId,
                    Points = 0,
                    TierClaimed = 0,
                    RankRewardsClaimed = false,
                    CustomProgressJson = "{}",
                    LastUpdated = DateTime.UtcNow
                };
                _dbContext.PlayerEventProgresses.Add(progress);
                _dbContext.SaveChanges();
                _logger.LogInformation($"EventEngine: Auto-initialized Event Progress for operative: {profileId}, event: {eventId}");
            }

            return progress;
        }

        public void RecordEventPoints(int profileId, string eventId, int points)
        {
            if (points <= 0) return;

            var progress = GetPlayerProgress(profileId, eventId);
            progress.Points += points;
            progress.LastUpdated = DateTime.UtcNow;
            _dbContext.SaveChanges();
            
            _logger.LogInformation($"EventEngine: Operative {profileId} earned {points} points in event '{eventId}'. New total: {progress.Points} PTS.");
        }

        public int GetPlayerRank(int profileId, string eventId)
        {
            var allProgress = _dbContext.PlayerEventProgresses
                .Where(ep => ep.EventId == eventId)
                .OrderByDescending(ep => ep.Points)
                .ThenBy(ep => ep.LastUpdated)
                .ToList();

            var playerIndex = allProgress.FindIndex(ep => ep.PlayerProfileId == profileId);
            return playerIndex >= 0 ? playerIndex + 1 : allProgress.Count + 1;
        }

        public List<(string Nickname, int Level, int Points)> GetLeaderboard(string eventId, int limit = 5)
        {
            var leaderboard = _dbContext.PlayerEventProgresses
                .Include(ep => ep.PlayerProfile)
                .Where(ep => ep.EventId == eventId)
                .OrderByDescending(ep => ep.Points)
                .ThenBy(ep => ep.LastUpdated)
                .Take(limit)
                .ToList();

            return leaderboard.Select(ep => (
                ep.PlayerProfile?.Nickname ?? "Unknown Operative",
                ep.PlayerProfile?.Level ?? 1,
                ep.Points
            )).ToList();
        }

        public ClaimMilestoneResult ClaimMilestoneReward(int profileId, string eventId, int tierIndex)
        {
            var activeEvent = GetActiveEvent();
            if (activeEvent == null || activeEvent.Id != eventId)
            {
                return new ClaimMilestoneResult { Success = false, Message = "Requested event is not currently active." };
            }

            var progress = GetPlayerProgress(profileId, eventId);
            var profile = _dbContext.Profiles.Find(profileId);
            if (profile == null)
            {
                return new ClaimMilestoneResult { Success = false, Message = "Player profile not found." };
            }

            // Parse custom config JSON milestones
            List<MilestoneConfig> milestones;
            try
            {
                using (var doc = JsonDocument.Parse(activeEvent.CustomConfigJson))
                {
                    var root = doc.RootElement;
                    if (root.TryGetProperty("Milestones", out var milestonesNode))
                    {
                        milestones = JsonSerializer.Deserialize<List<MilestoneConfig>>(milestonesNode.GetRawText()) ?? new();
                    }
                    else
                    {
                        return new ClaimMilestoneResult { Success = false, Message = "Milestones configuration is missing." };
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"EventEngine: Failed to parse event milestones config: {ex.Message}");
                return new ClaimMilestoneResult { Success = false, Message = "Invalid event milestone configuration." };
            }

            if (tierIndex < 0 || tierIndex >= milestones.Count)
            {
                return new ClaimMilestoneResult { Success = false, Message = "Invalid milestone tier requested." };
            }

            var milestone = milestones[tierIndex];

            // Points check
            if (progress.Points < milestone.TargetPoints)
            {
                return new ClaimMilestoneResult { Success = false, Message = $"Clearance insufficient. You need {milestone.TargetPoints} points (Current: {progress.Points} PTS)." };
            }

            // Claim check (using bitmask)
            var claimBit = 1 << tierIndex;
            if ((progress.TierClaimed & claimBit) != 0)
            {
                return new ClaimMilestoneResult { Success = false, Message = "Reward already secured in S.H.I.E.L.D. archive." };
            }

            // Award reward
            try
            {
                AwardAsset(profileId, profile, milestone.RewardType, milestone.RewardValue, milestone.RewardQuantity);
                
                // Save claiming progress
                progress.TierClaimed |= claimBit;
                _dbContext.SaveChanges();

                _logger.LogInformation($"EventEngine: Operative {profileId} claimed milestone tier {tierIndex} reward: {milestone.RewardName}");
                return new ClaimMilestoneResult 
                { 
                    Success = true, 
                    Message = $"Milestone reward successfully claimed: {milestone.RewardName}",
                    NewTierClaimed = progress.TierClaimed
                };
            }
            catch (Exception ex)
            {
                _logger.LogError($"EventEngine: Failed to award milestone: {ex.Message}");
                return new ClaimMilestoneResult { Success = false, Message = "Failed to dispatch assets. Core sync interrupt." };
            }
        }

        public EventCalculationResult CalculateAndDispatchRewards(string eventId)
        {
            var template = GetTemplates().FirstOrDefault(t => t.Id == eventId);
            if (template == null)
            {
                return new EventCalculationResult { Success = false, Message = $"Event '{eventId}' not found in blueprints." };
            }

            List<RankRewardConfig> rankRewards;
            try
            {
                using (var doc = JsonDocument.Parse(template.CustomConfigJson))
                {
                    var root = doc.RootElement;
                    if (root.TryGetProperty("RankRewards", out var rewardsNode))
                    {
                        rankRewards = JsonSerializer.Deserialize<List<RankRewardConfig>>(rewardsNode.GetRawText()) ?? new();
                    }
                    else
                    {
                        return new EventCalculationResult { Success = false, Message = "Ranking rewards configuration is missing." };
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"EventEngine: Failed to parse rank rewards config: {ex.Message}");
                return new EventCalculationResult { Success = false, Message = "Invalid event rewards configuration." };
            }

            // Fetch progress sorted by points descending, tiebroken by earlier update
            var progresses = _dbContext.PlayerEventProgresses
                .Include(ep => ep.PlayerProfile)
                .Where(ep => ep.EventId == eventId)
                .OrderByDescending(ep => ep.Points)
                .ThenBy(ep => ep.LastUpdated)
                .ToList();

            int processedCount = 0;

            for (int i = 0; i < progresses.Count; i++)
            {
                var progress = progresses[i];
                if (progress.RankRewardsClaimed) continue;

                var rank = i + 1;
                var matchedReward = rankRewards.FirstOrDefault(r => rank >= r.MinRank && rank <= r.MaxRank);

                if (matchedReward != null && progress.PlayerProfile != null)
                {
                    try
                    {
                        AwardAsset(progress.PlayerProfileId, progress.PlayerProfile, matchedReward.RewardType, matchedReward.RewardValue, matchedReward.RewardQuantity);
                        progress.RankRewardsClaimed = true;
                        processedCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"EventEngine: Calculation failed for operative {progress.PlayerProfileId} at rank {rank}: {ex.Message}");
                    }
                }
            }

            _dbContext.SaveChanges();
            _logger.LogInformation($"EventEngine: Ran results calculation for event '{eventId}'. Processed {processedCount} operatives.");

            return new EventCalculationResult
            {
                Success = true,
                Message = $"Mainframe calculation completed successfully. Dispatched ranking rewards to {processedCount} active operatives.",
                AgentsProcessed = processedCount
            };
        }

        private void AwardAsset(int profileId, PlayerProfile profile, string rewardType, int value, int quantity)
        {
            if (string.Equals(rewardType, "Silver", StringComparison.OrdinalIgnoreCase))
            {
                profile.SilverBalance += value;
            }
            else if (string.Equals(rewardType, "MobaCoin", StringComparison.OrdinalIgnoreCase))
            {
                profile.MobaCoinBalance += value;
            }
            else if (string.Equals(rewardType, "RallyPoints", StringComparison.OrdinalIgnoreCase))
            {
                profile.RallyPoints += value;
            }
            else if (string.Equals(rewardType, "Item", StringComparison.OrdinalIgnoreCase))
            {
                var invItem = _dbContext.PlayerInventoryItems
                    .FirstOrDefault(ii => ii.PlayerProfileId == profileId && ii.ItemTemplateId == value);
                
                if (invItem == null)
                {
                    invItem = new PlayerInventoryItem
                    {
                        PlayerProfileId = profileId,
                        ItemTemplateId = value,
                        Quantity = quantity
                    };
                    _dbContext.PlayerInventoryItems.Add(invItem);
                }
                else
                {
                    invItem.Quantity += quantity;
                }
            }
            else if (string.Equals(rewardType, "Card", StringComparison.OrdinalIgnoreCase))
            {
                var template = _dbContext.CardTemplates.Find(value);
                if (template != null)
                {
                    for (int q = 0; q < quantity; q++)
                    {
                        var newCard = new PlayerCard();
                        newCard.InitializeStats(template, GameplaySettings.DefaultMasteryPercentage);
                        newCard.PlayerProfileId = profileId;
                        _dbContext.PlayerCards.Add(newCard);
                    }
                }
                else
                {
                    throw new Exception($"Card template {value} not found in database.");
                }
            }
            else
            {
                throw new Exception($"Unsupported reward asset type: {rewardType}");
            }
        }

        public RaidProgressState GetRaidState(int profileId, string eventId)
        {
            var progress = GetPlayerProgress(profileId, eventId);
            RaidProgressState state;
            try
            {
                state = JsonSerializer.Deserialize<RaidProgressState>(progress.CustomProgressJson) ?? new RaidProgressState();
            }
            catch
            {
                state = new RaidProgressState();
            }

            var activeEvent = GetActiveEvent();
            string bossName = "Galactus";
            long easyBaseHp = 120000;
            long mediumBaseHp = 600000;
            long hardBaseHp = 2500000;

            if (activeEvent != null && activeEvent.Id == eventId)
            {
                try
                {
                    using (var doc = JsonDocument.Parse(activeEvent.CustomConfigJson))
                    {
                        var root = doc.RootElement;
                        if (root.TryGetProperty("BossName", out var nameProp)) bossName = nameProp.GetString() ?? bossName;
                        if (root.TryGetProperty("EasyBaseHp", out var ehp)) easyBaseHp = ehp.GetInt64();
                        if (root.TryGetProperty("MediumBaseHp", out var mhp)) mediumBaseHp = mhp.GetInt64();
                        if (root.TryGetProperty("HardBaseHp", out var hhp)) hardBaseHp = hhp.GetInt64();
                    }
                }
                catch { }
            }

            int baseLevel = (progress.Points / 1200) + 1;
            var bodyParts = new[] { "Left Cosmic Wing", "Command Helmet", "Power Reactor Core", "Devouring Left Claw", "Cosmic Core" };
            var rng = new Random();

            bool changed = false;

            if (!state.EasyTarget.IsInitialized)
            {
                state.EasyTarget.Level = Math.Clamp(baseLevel + rng.Next(-2, 3), 1, 100);
                state.EasyTarget.BodyPartName = bodyParts[rng.Next(bodyParts.Length)];
                state.EasyTarget.MainHpMax = (long)(easyBaseHp * (1.0 + state.EasyTarget.Level * 0.12));
                state.EasyTarget.BodyPartHpMax = (long)(state.EasyTarget.MainHpMax * 0.3);
                state.EasyTarget.MainHpCurrent = state.EasyTarget.MainHpMax;
                state.EasyTarget.BodyPartHpCurrent = state.EasyTarget.BodyPartHpMax;
                state.EasyTarget.IsInitialized = true;
                changed = true;
            }

            if (!state.MediumTarget.IsInitialized)
            {
                state.MediumTarget.Level = Math.Clamp((int)(baseLevel * 1.5) + rng.Next(0, 6), 5, 200);
                state.MediumTarget.BodyPartName = bodyParts[rng.Next(bodyParts.Length)];
                state.MediumTarget.MainHpMax = (long)(mediumBaseHp * (1.0 + state.MediumTarget.Level * 0.12));
                state.MediumTarget.BodyPartHpMax = (long)(state.MediumTarget.MainHpMax * 0.3);
                state.MediumTarget.MainHpCurrent = state.MediumTarget.MainHpMax;
                state.MediumTarget.BodyPartHpCurrent = state.MediumTarget.BodyPartHpMax;
                state.MediumTarget.IsInitialized = true;
                changed = true;
            }

            if (!state.HardTarget.IsInitialized)
            {
                state.HardTarget.Level = Math.Clamp((int)(baseLevel * 2.5) + rng.Next(5, 16), 15, 500);
                state.HardTarget.BodyPartName = bodyParts[rng.Next(bodyParts.Length)];
                state.HardTarget.MainHpMax = (long)(hardBaseHp * (1.0 + state.HardTarget.Level * 0.12));
                state.HardTarget.BodyPartHpMax = (long)(state.HardTarget.MainHpMax * 0.3);
                state.HardTarget.MainHpCurrent = state.HardTarget.MainHpMax;
                state.HardTarget.BodyPartHpCurrent = state.HardTarget.BodyPartHpMax;
                state.HardTarget.IsInitialized = true;
                changed = true;
            }

            if (changed)
            {
                progress.CustomProgressJson = JsonSerializer.Serialize(state);
                _dbContext.SaveChanges();
            }

            return state;
        }

        public List<HelperDto> GetAvailableHelpers(int profileId, int limit = 6)
        {
            var helperProfiles = _dbContext.Profiles
                .Include(p => p.Cards)
                    .ThenInclude(c => c.CardTemplate)
                .Where(p => p.Id != profileId)
                .OrderByDescending(p => p.Id)
                .Take(limit)
                .ToList();

            var helpers = new List<HelperDto>();
            foreach (var hp in helperProfiles)
            {
                var leaderCard = hp.Cards.FirstOrDefault(c => c.IsLeader) 
                    ?? hp.Cards.OrderByDescending(c => c.CurrentAtk).FirstOrDefault();

                if (leaderCard != null && leaderCard.CardTemplate != null)
                {
                    helpers.Add(new HelperDto
                    {
                        ProfileId = hp.Id,
                        Nickname = hp.Nickname,
                        Level = hp.Level,
                        CardId = leaderCard.Id,
                        CardTitle = leaderCard.CardTemplate.Title,
                        CardRarity = leaderCard.CardTemplate.Rarity,
                        CardImage = "/images/cards/" + leaderCard.CardTemplate.ImageFileName,
                        SkillName = leaderCard.CardTemplate.AbilityName ?? "No Ability",
                        SkillEffect = leaderCard.CardTemplate.AbilityEffect ?? "No Effect"
                    });
                }
            }
            return helpers;
        }

        public void SelectRaidHelper(int profileId, string eventId, int helperProfileId)
        {
            var progress = GetPlayerProgress(profileId, eventId);
            RaidProgressState state;
            try
            {
                state = JsonSerializer.Deserialize<RaidProgressState>(progress.CustomProgressJson) ?? new RaidProgressState();
            }
            catch
            {
                state = new RaidProgressState();
            }

            state.HelperProfileId = helperProfileId;
            progress.CustomProgressJson = JsonSerializer.Serialize(state);
            _dbContext.SaveChanges();
        }

        public RaidBattleResolutionResult ResolveRaidBattle(int profileId, string eventId, string difficulty, int costMultiplier)
        {
            var result = new RaidBattleResolutionResult { Difficulty = difficulty };
            var activeEvent = GetActiveEvent();
            if (activeEvent == null || activeEvent.Id != eventId)
            {
                result.Message = "Global alert: Active incursions sequence not resolved.";
                return result;
            }

            var profile = _dbContext.Profiles
                .Include(p => p.Cards)
                    .ThenInclude(c => c.CardTemplate)
                .FirstOrDefault(p => p.Id == profileId);

            if (profile == null)
            {
                result.Message = "Mainframe sync failed: Player profile not found.";
                return result;
            }

            // Assemble Friendly Deck (excluding helper which is added later)
            var friendlyDeck = profile.Cards.Where(c => c.IsInAttackDeck).ToList();
            if (!friendlyDeck.Any())
            {
                var leader = profile.Cards.FirstOrDefault(c => c.IsLeader);
                if (leader != null) friendlyDeck.Add(leader);
                else
                {
                    var topCard = profile.Cards.OrderByDescending(c => c.CurrentAtk).FirstOrDefault();
                    if (topCard != null) friendlyDeck.Add(topCard);
                }
            }

            // AP check based on dynamic deck cost
            int baseApCost = friendlyDeck.Sum(c => c.CardTemplate?.PowerRequirement ?? 10);
            if (baseApCost <= 0) baseApCost = 10;
            int apCost = baseApCost * (costMultiplier == 3 ? 3 : 1);

            if (profile.AttackPowerCurrent < apCost)
            {
                result.Message = $"Clearance power warning: Insufficient Attack Power (Required: {apCost} AP). Please recover reserves.";
                return result;
            }

            // Deduct AP
            profile.AttackPowerCurrent -= apCost;

            var state = GetRaidState(profileId, eventId);
            RaidTargetState target;
            if (string.Equals(difficulty, "Medium", StringComparison.OrdinalIgnoreCase))
            {
                target = state.MediumTarget;
            }
            else if (string.Equals(difficulty, "Hard", StringComparison.OrdinalIgnoreCase))
            {
                target = state.HardTarget;
            }
            else
            {
                target = state.EasyTarget;
            }

            if (!target.IsInitialized)
            {
                result.Message = "Target initialization mismatch. Please refresh primary feeds.";
                return result;
            }

            // Add helper leader card as 6th supportive card if selected
            PlayerProfile? helperProfile = null;
            PlayerCard? helperCard = null;

            if (state.HelperProfileId.HasValue)
            {
                helperProfile = _dbContext.Profiles
                    .Include(p => p.Cards)
                        .ThenInclude(c => c.CardTemplate)
                    .FirstOrDefault(p => p.Id == state.HelperProfileId.Value);

                if (helperProfile != null)
                {
                    helperCard = helperProfile.Cards.FirstOrDefault(c => c.IsLeader) 
                        ?? helperProfile.Cards.OrderByDescending(c => c.CurrentAtk).FirstOrDefault();

                    if (helperCard != null)
                    {
                        var helperEvalCard = new PlayerCard
                        {
                            Id = helperCard.Id,
                            CardTemplateId = helperCard.CardTemplateId,
                            CardTemplate = helperCard.CardTemplate,
                            CurrentAtk = helperCard.CurrentAtk,
                            CurrentDef = helperCard.CurrentDef,
                            AbilityLevel = helperCard.AbilityLevel,
                            IsLeader = false
                        };
                        friendlyDeck.Add(helperEvalCard);
                    }
                }
            }

            // Evaluate Squad stats
            var friendlyStats = _abilityEvaluator.EvaluateDeck(friendlyDeck, new List<PlayerCard>(), isAttackingDeck: true);
            long totalAtk = friendlyStats.Sum(s => s.FinalAtk);
            long baseDmg = totalAtk * (costMultiplier == 3 ? 3 : 1);

            // Fetch config scaling def
            string bossName = "Galactus";
            long easyBaseDef = 1000;
            long mediumBaseDef = 6000;
            long hardBaseDef = 25000;
            int pointsPerDefeat = 150;
            long baseSilver = 2500;

            if (string.Equals(difficulty, "Medium", StringComparison.OrdinalIgnoreCase))
            {
                pointsPerDefeat = 750;
                baseSilver = 12000;
            }
            else if (string.Equals(difficulty, "Hard", StringComparison.OrdinalIgnoreCase))
            {
                pointsPerDefeat = 3500;
                baseSilver = 50000;
            }

            try
            {
                using (var doc = JsonDocument.Parse(activeEvent.CustomConfigJson))
                {
                    var root = doc.RootElement;
                    if (root.TryGetProperty("BossName", out var nameProp)) bossName = nameProp.GetString() ?? bossName;
                    if (root.TryGetProperty("EasyBaseDef", out var edef)) easyBaseDef = edef.GetInt64();
                    if (root.TryGetProperty("MediumBaseDef", out var mdef)) mediumBaseDef = mdef.GetInt64();
                    if (root.TryGetProperty("HardBaseDef", out var hdef)) hardBaseDef = hdef.GetInt64();
                    if (root.TryGetProperty("PointsPerEasyDefeat", out var ep)) pointsPerDefeat = string.Equals(difficulty, "Easy", StringComparison.OrdinalIgnoreCase) ? ep.GetInt32() : pointsPerDefeat;
                    if (root.TryGetProperty("PointsPerMediumDefeat", out var mp)) pointsPerDefeat = string.Equals(difficulty, "Medium", StringComparison.OrdinalIgnoreCase) ? mp.GetInt32() : pointsPerDefeat;
                    if (root.TryGetProperty("PointsPerHardDefeat", out var hp)) pointsPerDefeat = string.Equals(difficulty, "Hard", StringComparison.OrdinalIgnoreCase) ? hp.GetInt32() : pointsPerDefeat;
                }
            }
            catch { }

            long baseDef = string.Equals(difficulty, "Medium", StringComparison.OrdinalIgnoreCase) ? mediumBaseDef
                         : string.Equals(difficulty, "Hard", StringComparison.OrdinalIgnoreCase) ? hardBaseDef
                         : easyBaseDef;

            long bossDef = (long)(baseDef * (1.0 + target.Level * 0.08));
            long netDmg = Math.Max((long)(baseDmg * 0.10), baseDmg - bossDef);

            // Record logs
            var logs = new List<string>();
            logs.Add($"[SQUAD INITIALIZATION] Attacking Deck assembled with {friendlyDeck.Count} operative cards.");

            if (helperProfile != null && helperCard != null)
            {
                logs.Add($"[COOPERATIVE LINK] Bound Agent {helperProfile.Nickname}'s active leader card [{helperCard.CardTemplate?.Title ?? "Unknown"}] as supportive 6th card.");
                if (!string.IsNullOrWhiteSpace(helperCard.CardTemplate?.AbilityName))
                {
                    logs.Add($"[TACTICAL ASSIST] Cooperative Ability Forced: {helperCard.CardTemplate.AbilityName} ({helperCard.CardTemplate.AbilityEffect}) activated!");
                }
            }

            logs.Add($"[TACTICAL BOARDS] Squad aggregate ATK evaluated at {totalAtk:N0} PTS.");
            if (costMultiplier == 3)
            {
                logs.Add($"[OVERDRIVE CHARGES] {apCost} AP Overdrive Strike initiated. Damage output increased by 3.0x!");
            }
            else
            {
                logs.Add($"[STANDARD STRIKE] {apCost} AP Strike initiated.");
            }
            logs.Add($"[BOSS BARRIER] {bossName} shields absorbed {bossDef:N0} damage.");
            logs.Add($"[STRIKE INVASION] Squad dealt {netDmg:N0} net damage to {bossName}'s {target.BodyPartName}.");

            long mainHpBefore = target.MainHpCurrent;
            long partHpBefore = target.BodyPartHpCurrent;

            target.MainHpCurrent = Math.Max(0, target.MainHpCurrent - netDmg);
            target.BodyPartHpCurrent = Math.Max(0, target.BodyPartHpCurrent - netDmg);

            string victoryType = "Defeat";
            int pointsEarned = 0;
            long silverEarned = 0;

            if (netDmg >= mainHpBefore)
            {
                victoryType = "OneShot";
                pointsEarned = pointsPerDefeat * 2 * costMultiplier;
                silverEarned = (long)(baseSilver * (1.0 + target.Level * 0.05) * 2 * costMultiplier);
                
                target.IsInitialized = false; // regenerates next load
                state.HelperProfileId = null; // clear helper

                logs.Add($"[LEGENDARY OUTCOME] Complete one-shot destruction triggered! {bossName} has been completely vaporized.");
                logs.Add($"[SYSTEM SYNC] Earned {pointsEarned:N0} Milestones points (+100% one-shot bonus) and secured {silverEarned:N0} Silver!");
            }
            else if (netDmg >= partHpBefore)
            {
                victoryType = "PartDefeated";
                pointsEarned = pointsPerDefeat * costMultiplier;
                silverEarned = (long)(baseSilver * (1.0 + target.Level * 0.05) * costMultiplier);

                target.IsInitialized = false; // regenerates next load
                state.HelperProfileId = null; // clear helper

                logs.Add($"[TACTICAL CLEARANCE] Target body part {target.BodyPartName} destroyed! {bossName} retreated from sector.");
                logs.Add($"[SYSTEM SYNC] Earned {pointsEarned:N0} Milestones points and secured {silverEarned:N0} Silver.");
            }
            else
            {
                victoryType = "Defeat";
                pointsEarned = (int)Math.Max(5, (netDmg * 0.05) * costMultiplier);
                silverEarned = 0;

                logs.Add($"[TACTICAL STANDBY] Remaining HP: {target.MainHpCurrent:N0} (Main), {target.BodyPartHpCurrent:N0} ({target.BodyPartName}). Damage serialized to database.");
                logs.Add($"[SYSTEM SYNC] Earned {pointsEarned:N0} Consolation Milestones points.");
            }

            // Award Rewards
            RecordEventPoints(profileId, eventId, pointsEarned);
            if (silverEarned > 0)
            {
                profile.SilverBalance += (int)silverEarned;
            }

            // Serialize progress state
            var progress = GetPlayerProgress(profileId, eventId);
            progress.CustomProgressJson = JsonSerializer.Serialize(state);
            progress.LastUpdated = DateTime.UtcNow;
            
            _dbContext.SaveChanges();

            result.Success = true;
            result.Message = "Operational combat evaluation complete.";
            result.BossName = bossName;
            result.BossLevel = target.Level;
            result.BodyPartName = target.BodyPartName;
            result.PlayerDamage = baseDmg;
            result.BossDefense = bossDef;
            result.NetDamage = netDmg;
            result.MainHpBefore = mainHpBefore;
            result.MainHpAfter = target.MainHpCurrent;
            result.PartHpBefore = partHpBefore;
            result.PartHpAfter = target.BodyPartHpCurrent;
            result.VictoryType = victoryType;
            result.PointsEarned = pointsEarned;
            result.SilverEarned = silverEarned;
            result.CombatLogs = logs;

            return result;
        }

        // Inner Classes for parsing JSON CustomConfig
        private class MilestoneConfig
        {
            public int TargetPoints { get; set; }
            public string RewardType { get; set; } = string.Empty;
            public int RewardValue { get; set; }
            public int RewardQuantity { get; set; } = 1;
            public string RewardName { get; set; } = string.Empty;
        }

        private class RankRewardConfig
        {
            public int MinRank { get; set; }
            public int MaxRank { get; set; }
            public string RewardType { get; set; } = string.Empty;
            public int RewardValue { get; set; }
            public int RewardQuantity { get; set; } = 1;
            public string RewardName { get; set; } = string.Empty;
        }
    }
}
