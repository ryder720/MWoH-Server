using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MwohServer.Data;
using MwohServer.Models;

namespace MwohServer.Services
{
    public class CardGrowthEngine : ICardGrowthEngine
    {
        private readonly MwohDbContext _dbContext;
        private readonly ILogger<CardGrowthEngine> _logger;
        private readonly IAssignmentEngine _assignmentEngine;

        public CardGrowthEngine(MwohDbContext dbContext, ILogger<CardGrowthEngine> logger, IAssignmentEngine assignmentEngine)
        {
            _dbContext = dbContext;
            _logger = logger;
            _assignmentEngine = assignmentEngine;
        }

        #region Helper Formulas
        private static int GetBaseCardSilverCost(int baseLevel)
        {
            if (baseLevel <= 9) return 75 + (baseLevel - 1) * 50;
            if (baseLevel <= 19) return 735 + (baseLevel - 10) * 70;
            if (baseLevel <= 29) return 1845 + (baseLevel - 20) * 90;
            if (baseLevel <= 39) return 3355 + (baseLevel - 30) * 110;
            if (baseLevel <= 49) return 5265 + (baseLevel - 40) * 130;
            if (baseLevel <= 59) return 7575 + (baseLevel - 50) * 150;
            if (baseLevel <= 69) return 10285 + (baseLevel - 60) * 170;
            if (baseLevel <= 79) return 13395 + (baseLevel - 70) * 190;
            if (baseLevel <= 89) return 16905 + (baseLevel - 80) * 210;
            return 20815 + (baseLevel - 90) * 230;
        }

        private static int GetBoosterLevelModifier(int baseLevel)
        {
            if (baseLevel <= 9) return 25;
            if (baseLevel <= 19) return 35;
            if (baseLevel <= 29) return 45;
            if (baseLevel <= 39) return 55;
            if (baseLevel <= 49) return 65;
            if (baseLevel <= 59) return 75;
            if (baseLevel <= 69) return 85;
            if (baseLevel <= 79) return 95;
            return 105;
        }

        private static int GetBoosterBaseXP(string rarity)
        {
            return rarity switch
            {
                "Common" or "Normal"                      => 100,
                "Uncommon" or "High Normal"               => 250,
                "Rare"                                    => 600,
                "Special Rare" or "High Rare"             => 1200,
                "Super Special Rare" or "Super Rare"      => 1500,
                "Ultimate Rare" or "Ultra Rare"           => 2000,
                "Legendary" or "Legend"                   => 3000,
                "Ultimate Legendary" or "Special Legend"  => 4000,
                _                                         => 100
            };
        }
        #endregion

        public EnhanceResult Enhance(int profileId, int targetCardId, string materialType, List<int> materialIds)
        {
            _logger.LogInformation($"[CardGrowthEngine] Enhance called for Profile: {profileId}, Target: {targetCardId}, MaterialType: {materialType}");

            var profile = _dbContext.Profiles
                .Include(p => p.Cards)
                .ThenInclude(c => c.CardTemplate)
                .Include(p => p.InventoryItems)
                .ThenInclude(pi => pi.ItemTemplate)
                .FirstOrDefault(p => p.Id == profileId);

            if (profile == null)
            {
                return new EnhanceResult { Success = false, Message = "Profile not found." };
            }

            var targetCard = profile.Cards.FirstOrDefault(c => c.Id == targetCardId);
            if (targetCard == null)
            {
                return new EnhanceResult { Success = false, Message = "Target card not found." };
            }

            int expGain = 0;
            List<PlayerInventoryItem> consumedSerums = new List<PlayerInventoryItem>();
            Dictionary<int, int> requestedSerumCounts = new Dictionary<int, int>();
            List<PlayerCard>? materialCardsList = null;
            int silverCost = 0;

            if (materialType == "serum")
            {
                foreach (var matId in materialIds)
                {
                    if (requestedSerumCounts.ContainsKey(matId))
                        requestedSerumCounts[matId]++;
                    else
                        requestedSerumCounts[matId] = 1;
                }

                foreach (var kvp in requestedSerumCounts)
                {
                    var materialId = kvp.Key;
                    var quantityNeeded = kvp.Value;

                    var serumItem = profile.InventoryItems.FirstOrDefault(pi => pi.ItemTemplateId == materialId);
                    if (serumItem == null || serumItem.Quantity < quantityNeeded)
                    {
                        var serumName = serumItem?.ItemTemplate?.Name ?? $"Serum (ID: {materialId})";
                        return new EnhanceResult { Success = false, Message = $"Insufficient {serumName} quantity in depot." };
                    }

                    bool isSuper = (serumItem.ItemTemplate != null && serumItem.ItemTemplate.Name.Contains("Super", StringComparison.OrdinalIgnoreCase)) || materialId == 36;
                    int expPerSerum = isSuper ? 5000 : 1000;
                    expGain += expPerSerum * quantityNeeded;
                    consumedSerums.Add(serumItem);
                }

                // Liftoff silver fee restriction for LevelUpSerum
                silverCost = 0;
            }
            else if (materialType == "card")
            {
                var materialCards = profile.Cards.Where(c => materialIds.Contains(c.Id)).ToList();
                if (materialCards.Count != materialIds.Count)
                {
                    return new EnhanceResult { Success = false, Message = "One or more material cards not found." };
                }
                foreach (var matCard in materialCards)
                {
                    if (matCard.IsLeader || matCard.IsInAttackDeck || matCard.IsInDefenseDeck)
                    {
                        return new EnhanceResult { Success = false, Message = $"Cannot sacrifice active representative or squad member: {matCard.CardTemplate?.Title}." };
                    }
                    if (matCard.Id == targetCard.Id)
                    {
                        return new EnhanceResult { Success = false, Message = "Cannot sacrifice the target card itself!" };
                    }
                }

                foreach (var matCard in materialCards)
                {
                    var baseRarityXp = GetBoosterBaseXP(matCard.CardTemplate?.Rarity ?? "Common");
                    var isSameAlignment = string.Equals(targetCard.CardTemplate?.Alignment, matCard.CardTemplate?.Alignment, StringComparison.OrdinalIgnoreCase);
                    var alignmentBonus = isSameAlignment ? 24 : 0;
                    var levelFactor = (matCard.CurrentLevel - 1) * 20;

                    expGain += baseRarityXp + alignmentBonus + levelFactor;

                    int baseCost = GetBaseCardSilverCost(targetCard.CurrentLevel);
                    int boosterModifier = GetBoosterLevelModifier(targetCard.CurrentLevel);
                    silverCost += baseCost + Math.Max(0, matCard.CurrentLevel - 1) * boosterModifier;
                }

                materialCardsList = materialCards;
            }
            else
            {
                return new EnhanceResult { Success = false, Message = "Unsupported material type." };
            }

            var maxLevel = targetCard.GetMaxLevel();

            bool targetHasAbility = !string.IsNullOrEmpty(targetCard.CardTemplate?.AbilityName);
            bool canUpgradeAbility = targetHasAbility && targetCard.AbilityLevel < 10;

            if (targetCard.CurrentLevel >= maxLevel)
            {
                if (!canUpgradeAbility || materialType == "serum")
                {
                    return new EnhanceResult { Success = false, Message = "Target Hero is already at maximum clearance capacity!" };
                }
            }

            if (profile.SilverBalance < silverCost)
            {
                return new EnhanceResult { Success = false, Message = "Insufficient Silver budget for forge synthesis." };
            }

            var newLevel = Math.Min(maxLevel, targetCard.CurrentLevel + (expGain / 100));
            targetCard.CurrentLevel = newLevel;

            targetCard.RecalculateStats();

            // Calculate Ability Level-Up Chance
            int abilityLevelUpChance = 0;
            bool abilityLeveledUp = false;

            if (targetHasAbility && targetCard.AbilityLevel < 10)
            {
                if (materialType == "card" && materialCardsList != null)
                {
                    foreach (var matCard in materialCardsList)
                    {
                        if (!string.IsNullOrEmpty(matCard.CardTemplate?.AbilityName))
                        {
                            int chanceForMat = 20;
                            if (matCard.CardTemplateId == targetCard.CardTemplateId)
                            {
                                chanceForMat = 100; // Duplicate card guarantees 100% chance
                            }
                            else
                            {
                                var matRarity = matCard.CardTemplate?.Rarity ?? "Normal";
                                chanceForMat = matRarity switch
                                {
                                    "Common" or "Normal"                                                  => 20,
                                    "Uncommon" or "High Normal"                                           => 20,
                                    "Rare"                                                                => 40,
                                    "Special Rare" or "High Rare"                                         => 60,
                                    "Super Special Rare" or "Super Rare"                                  => 80,
                                    "Ultimate Rare" or "Ultra Rare" or "Legendary" or "Legend" or "Ultimate Legendary" or "Special Legend" => 100,
                                    _                                                                     => 20
                                };
                            }
                            abilityLevelUpChance += chanceForMat;
                        }
                    }
                }
            }

            abilityLevelUpChance = Math.Min(100, abilityLevelUpChance);

            if (abilityLevelUpChance > 0)
            {
                var rand = new Random();
                var roll = rand.Next(1, 101);
                if (roll <= abilityLevelUpChance)
                {
                    targetCard.AbilityLevel = Math.Min(10, targetCard.AbilityLevel + 1);
                    abilityLeveledUp = true;
                }
            }

            // Deduct cost and consume material
            profile.SilverBalance -= silverCost;

            if (materialType == "serum")
            {
                foreach (var kvp in requestedSerumCounts)
                {
                    var serumItem = consumedSerums.FirstOrDefault(s => s.ItemTemplateId == kvp.Key);
                    if (serumItem != null)
                    {
                        serumItem.Quantity -= kvp.Value;
                    }
                }
            }
            else if (materialType == "card" && materialCardsList != null)
            {
                foreach (var matCard in materialCardsList)
                {
                    _dbContext.PlayerCards.Remove(matCard);
                }
            }

            try
            {
                _dbContext.SaveChanges();
                // Trigger assignment hook
                _assignmentEngine.RecordEvent(profileId, GoalType.EnhanceCard, 1);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CardGrowthEngine] Failed to commit card enhancement to SQLite.");
                return new EnhanceResult { Success = false, Message = "Database write error occurred." };
            }

            string forgeMessage = $"Forge committed! {targetCard.CardTemplate?.Title} upgraded to level {newLevel}!";
            if (abilityLeveledUp)
            {
                forgeMessage += $" Sync Ability [ {targetCard.CardTemplate?.AbilityName} ] upgraded to Level {targetCard.AbilityLevel}!";
            }

            return new EnhanceResult
            {
                Success = true,
                Message = forgeMessage,
                RemainingSilver = profile.SilverBalance,
                TargetCard = targetCard
            };
        }

        public FusionResult Fuse(int profileId, int baseCardId, int partnerCardId)
        {
            _logger.LogInformation($"[CardGrowthEngine] Fuse called for Profile: {profileId}, Base: {baseCardId}, Partner: {partnerCardId}");

            var profile = _dbContext.Profiles
                .Include(p => p.Cards)
                .ThenInclude(c => c.CardTemplate)
                .FirstOrDefault(p => p.Id == profileId);

            if (profile == null)
            {
                return new FusionResult { Success = false, Message = "Profile not found." };
            }

            var baseCard = profile.Cards.FirstOrDefault(c => c.Id == baseCardId);
            var partnerCard = profile.Cards.FirstOrDefault(c => c.Id == partnerCardId);

            if (baseCard == null || partnerCard == null)
            {
                return new FusionResult { Success = false, Message = "One or both selected cards were not found in your secured active files." };
            }

            if (baseCard.CardTemplate == null || partnerCard.CardTemplate == null)
            {
                return new FusionResult { Success = false, Message = "Could not resolve template assets for selected cards." };
            }

            if (baseCard.CardTemplate.Title.EndsWith("Cosmic Cube", StringComparison.OrdinalIgnoreCase))
            {
                return new FusionResult { Success = false, Message = "A Cosmic Cube cannot be used as the base card for fusion." };
            }

            bool isPartnerCosmicCube = partnerCard.CardTemplate.Title.EndsWith("Cosmic Cube", StringComparison.OrdinalIgnoreCase);
            if (isPartnerCosmicCube)
            {
                // Validate alignment compatibility
                if (!string.Equals(baseCard.CardTemplate.Alignment, partnerCard.CardTemplate.Alignment, StringComparison.OrdinalIgnoreCase))
                {
                    return new FusionResult { Success = false, Message = $"Cosmic Cube alignment ({partnerCard.CardTemplate.Alignment}) does not match base card alignment ({baseCard.CardTemplate.Alignment})." };
                }

                // Validate rarity compatibility
                var cubeRarity = partnerCard.CardTemplate.Rarity ?? "";
                var baseRarity = baseCard.CardTemplate.Rarity ?? "";
                bool rarityMatch = false;

                if (cubeRarity.Contains("Legend", StringComparison.OrdinalIgnoreCase) || cubeRarity.Contains("Legendary", StringComparison.OrdinalIgnoreCase))
                {
                    rarityMatch = baseRarity.Contains("Legend", StringComparison.OrdinalIgnoreCase) || baseRarity.Contains("Legendary", StringComparison.OrdinalIgnoreCase);
                }
                else // Ultimate Rare / Ultra Rare
                {
                    rarityMatch = baseRarity.Contains("Ultra", StringComparison.OrdinalIgnoreCase) || baseRarity.Contains("Ultimate", StringComparison.OrdinalIgnoreCase);
                }

                if (!rarityMatch)
                {
                    return new FusionResult { Success = false, Message = $"Cosmic Cube rarity ({cubeRarity}) is not compatible with base card rarity ({baseRarity})." };
                }

                // In-memory copy base card's exact stats onto the Cosmic Cube partner card
                partnerCard.CurrentLevel = baseCard.CurrentLevel;
                partnerCard.CurrentMastery = baseCard.CurrentMastery;
                partnerCard.AbilityLevel = baseCard.AbilityLevel;
                partnerCard.CurrentAtk = baseCard.CurrentAtk;
                partnerCard.CurrentDef = baseCard.CurrentDef;
                partnerCard.FusionBonusAtk = baseCard.FusionBonusAtk;
                partnerCard.FusionBonusDef = baseCard.FusionBonusDef;
                partnerCard.CardTemplate = baseCard.CardTemplate;
                partnerCard.CardTemplateId = baseCard.CardTemplateId;
            }

            // 1. Title verification (must be identical or partner must be a compatible Cosmic Cube)
            if (!isPartnerCosmicCube && baseCard.CardTemplate.Title != partnerCard.CardTemplate.Title)
            {
                return new FusionResult { Success = false, Message = "Fusion requires two identical Hero assets!" };
            }

            // 2. Representative or squad guards
            if (baseCard.IsLeader || baseCard.IsInAttackDeck || baseCard.IsInDefenseDeck)
            {
                return new FusionResult { Success = false, Message = $"Cannot fuse the core card because it is active in your deck/leader slot: {baseCard.CardTemplate?.Title}." };
            }
            if (partnerCard.IsLeader || partnerCard.IsInAttackDeck || partnerCard.IsInDefenseDeck)
            {
                return new FusionResult { Success = false, Message = $"Cannot consume the partner card because it is active in your deck/leader slot: {partnerCard.CardTemplate?.Title}." };
            }

            // 3. Determine target template (suffix logic)
            var baseTitle = baseCard.CardTemplate.Title;
            var baseVariant = baseCard.CardTemplate.VariantName ?? "Base";
            var targetVariant = baseVariant.EndsWith("+") ? baseVariant + "+" : baseVariant + "+";

            var targetTemplate = _dbContext.CardTemplates.FirstOrDefault(t => t.Title == baseTitle && t.VariantName == targetVariant);

            if (targetTemplate == null)
            {
                // Fallback suffix parsing
                targetTemplate = _dbContext.CardTemplates.FirstOrDefault(t => t.Title == baseTitle && t.VariantName.Contains(baseVariant + "+"));
            }

            if (targetTemplate == null)
            {
                return new FusionResult { Success = false, Message = $"{baseTitle} has already reached its final terminal fusion tier!" };
            }

            // 4. Rarity fee verification
            var rarity = baseCard.CardTemplate.Rarity ?? "Normal";
            int silverCost = rarity switch
            {
                "Common" or "Normal"                                                  => 1575,
                "High Normal" or "Uncommon"                                           => 3075,
                "Rare"                                                                => 8075,
                "High Rare" or "Special Rare"                                         => 20075,
                "Super Rare" or "Super Special Rare"                                  => 40075,
                "Ultra Rare" or "Ultimate Rare"                                       => 81505,
                "Legend" or "Legendary" or "Ultimate Legendary" or "Special Legend"   => 120000,
                _                                                                     => 8075
            };

            if (profile.SilverBalance < silverCost)
            {
                return new FusionResult { Success = false, Message = $"Insufficient Silver budget for fusion fee (💎 {silverCost.ToString("N0")} required)." };
            }

            // 5. Carry-over stat calculation (Max Level Check)
            var baseMaxLvl = baseCard.GetMaxLevel();
            var partnerMaxLvl = partnerCard.GetMaxLevel();
            
            bool isMaxFusion = (baseCard.CurrentLevel >= baseMaxLvl) && (partnerCard.CurrentLevel >= partnerMaxLvl);
            double factor = isMaxFusion ? 0.10 : 0.05;

            int inheritedAtk = (int)Math.Round((baseCard.CurrentAtk + partnerCard.CurrentAtk) * factor);
            int inheritedDef = (int)Math.Round((baseCard.CurrentDef + partnerCard.CurrentDef) * factor);

            // 6. Mastery carry-over sum
            var baseMaxMastery = baseCard.CardTemplate?.MaxMastery ?? 100;
            var partnerMaxMastery = partnerCard.CardTemplate?.MaxMastery ?? 100;
            
            int masteryContribA = (baseCard.CurrentMastery >= baseMaxMastery) ? baseCard.CurrentLevel : 0;
            int masteryContribB = (partnerCard.CurrentMastery >= partnerMaxMastery) ? partnerCard.CurrentLevel : 0;
            int startingMastery = masteryContribA + masteryContribB;

            // 7. Apply updates
            profile.SilverBalance -= silverCost;

            // Delete partner
            _dbContext.PlayerCards.Remove(partnerCard);

            // Accumulate permanent carry-over fusion bonuses
            baseCard.CardTemplateId = targetTemplate.Id;
            baseCard.CardTemplate = targetTemplate;
            baseCard.FusionBonusAtk += inheritedAtk;
            baseCard.FusionBonusDef += inheritedDef;

            // Reset level and ability level as per wiki rules
            baseCard.CurrentLevel = 1;
            baseCard.AbilityLevel = 1;

            // Calculate starting mastery stats
            var targetMaxMastery = targetTemplate.MaxMastery;
            if (targetMaxMastery <= 0) targetMaxMastery = 100;
            baseCard.CurrentMastery = Math.Min(targetMaxMastery, startingMastery);

            var activeMasteryAtk = targetMaxMastery > 0 ? (targetTemplate.MasteryBonusAtk * baseCard.CurrentMastery) / targetMaxMastery : 0;
            var activeMasteryDef = targetMaxMastery > 0 ? (targetTemplate.MasteryBonusDef * baseCard.CurrentMastery) / targetMaxMastery : 0;

            // Net combat stats
            baseCard.CurrentAtk = targetTemplate.BaseAtk + activeMasteryAtk + baseCard.FusionBonusAtk;
            baseCard.CurrentDef = targetTemplate.BaseDef + activeMasteryDef + baseCard.FusionBonusDef;

            try
            {
                _dbContext.SaveChanges();
                // Trigger assignment hook
                _assignmentEngine.RecordEvent(profileId, GoalType.FuseCard, 1);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CardGrowthEngine] Failed to commit card fusion to SQLite.");
                return new FusionResult { Success = false, Message = "Database write error occurred." };
            }

            string fusionMessage = $"Fusion committed! upgraded to [ {targetTemplate.VisualTitle} ]!";
            if (isMaxFusion)
            {
                fusionMessage += " 🔥 MAX FUSION BONUS ACHIEVED: 10% stats carry-over applied!";
            }
            else
            {
                fusionMessage += " (5% normal fusion stats carry-over applied).";
            }

            return new FusionResult
            {
                Success = true,
                Message = fusionMessage,
                RemainingSilver = profile.SilverBalance,
                BaseCard = baseCard
            };
        }
    }
}
