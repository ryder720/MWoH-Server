using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MwohServer.Data;
using MwohServer.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MwohServer.Services
{
    public class ItemLedger : IItemLedger
    {
        private readonly ILogger<ItemLedger> _logger;
        private readonly MwohDbContext _dbContext;
        private readonly IGachaSummoner _gachaSummoner;
        private readonly ICardGrowthEngine _cardGrowthEngine;

        public ItemLedger(ILogger<ItemLedger> logger, MwohDbContext dbContext, IGachaSummoner gachaSummoner, ICardGrowthEngine cardGrowthEngine)
        {
            _logger = logger;
            _dbContext = dbContext;
            _gachaSummoner = gachaSummoner;
            _cardGrowthEngine = cardGrowthEngine;
        }

        public List<PlayerInventoryItem> GetInventory(int profileId)
        {
            return _dbContext.PlayerInventoryItems
                .Include(pi => pi.ItemTemplate)
                .Where(pi => pi.PlayerProfileId == profileId)
                .ToList();
        }

        public ItemUseResult UseItem(int profileId, int itemId, int targetCardId = 0)
        {
            var invItem = _dbContext.PlayerInventoryItems
                .Include(pi => pi.ItemTemplate)
                .FirstOrDefault(pi => pi.PlayerProfileId == profileId && pi.ItemTemplateId == itemId);

            if (invItem == null || invItem.Quantity <= 0)
            {
                return new ItemUseResult { Success = false, Message = "Insufficient item quantity." };
            }

            var profile = _dbContext.Profiles.FirstOrDefault(p => p.Id == profileId);
            if (profile == null)
            {
                return new ItemUseResult { Success = false, Message = "Profile mismatch." };
            }

            // Apply item effect based on template classification
            string message = "";
            object? updatedCard = null;
            bool alreadyDecremented = false;

            if (invItem.ItemTemplate!.Type == "EnergyRestorative")
            {
                int refill = (profile.EnergyMax * invItem.ItemTemplate.EffectValue) / 100;
                profile.EnergyCurrent = Math.Min(profile.EnergyMax, profile.EnergyCurrent + refill);
                message = $"Energy restored by {refill} points!";
            }
            else if (invItem.ItemTemplate.Type == "AttackPowerRestorative")
            {
                int refill = (profile.AttackPower * invItem.ItemTemplate.EffectValue) / 100;
                profile.AttackPowerCurrent = Math.Min(profile.AttackPower, profile.AttackPowerCurrent + refill);
                
                bool isPowerPack = invItem.ItemTemplate.Name.Contains("Power Pack", StringComparison.OrdinalIgnoreCase) 
                                   || invItem.ItemTemplate.Name.Contains("Power Kit", StringComparison.OrdinalIgnoreCase);
                if (isPowerPack)
                {
                    int defRefill = (profile.DefensePower * invItem.ItemTemplate.EffectValue) / 100;
                    profile.DefensePowerCurrent = Math.Min(profile.DefensePower, profile.DefensePowerCurrent + defRefill);
                    message = $"Power Pack restored both Attack Power (by {refill} points) and Defense Power (by {defRefill} points)!";
                }
                else
                {
                    message = $"Attack Power restored by {refill} points!";
                }
            }
            else if (invItem.ItemTemplate.Type == "DefensePowerRestorative")
            {
                int refill = (profile.DefensePower * invItem.ItemTemplate.EffectValue) / 100;
                profile.DefensePowerCurrent = Math.Min(profile.DefensePower, profile.DefensePowerCurrent + refill);
                message = $"Defense Power restored by {refill} points!";
            }
            else if (invItem.ItemTemplate.Type == "LevelUpSerum")
            {
                if (targetCardId == 0)
                {
                    return new ItemUseResult { Success = false, Message = "Please select a target card to boost." };
                }

                // Delegate to CardGrowthEngine to keep cap calculation & consumption unified
                var growthResult = _cardGrowthEngine.Enhance(profileId, targetCardId, "serum", new List<int> { itemId });
                if (!growthResult.Success)
                {
                    return new ItemUseResult { Success = false, Message = growthResult.Message };
                }

                message = growthResult.Message;
                var card = growthResult.TargetCard;
                if (card != null)
                {
                    updatedCard = new
                    {
                        id = card.Id,
                        level = card.CurrentLevel,
                        atk = card.CurrentAtk,
                        def = card.CurrentDef,
                        masteryCur = card.CurrentMastery
                    };
                }
                
                alreadyDecremented = true; // Let CardGrowthEngine handle the deduction
            }
            else if (invItem.ItemTemplate.Type == "MasteryIso8")
            {
                if (targetCardId == 0)
                {
                    return new ItemUseResult { Success = false, Message = "Please select a target card to apply mastery." };
                }

                var card = _dbContext.PlayerCards
                    .Include(pc => pc.CardTemplate)
                    .FirstOrDefault(pc => pc.PlayerProfileId == profileId && pc.Id == targetCardId);

                if (card == null)
                {
                    return new ItemUseResult { Success = false, Message = "Target card not located in catalog." };
                }

                int maxMastery = card.CardTemplate?.MaxMastery ?? 100;
                if (maxMastery <= 0) maxMastery = 100;

                if (card.CurrentMastery >= maxMastery)
                {
                    return new ItemUseResult { Success = false, Message = $"⚠️ CAP REACHED // {card.CardTemplate?.Title} has already reached maximum mastery ({maxMastery})." };
                }

                int gain = invItem.ItemTemplate.EffectValue > 0 ? invItem.ItemTemplate.EffectValue : 10;
                int oldMastery = card.CurrentMastery;
                card.CurrentMastery = Math.Min(maxMastery, card.CurrentMastery + gain);
                int masteryGained = card.CurrentMastery - oldMastery;

                card.RecalculateStats();

                message = $"🧪 ISO-8 SYNTHESISED // {card.CardTemplate?.Title} mastery increased by +{masteryGained}!";
                updatedCard = new
                {
                    id = card.Id,
                    level = card.CurrentLevel,
                    atk = card.CurrentAtk,
                    def = card.CurrentDef,
                    masteryCur = card.CurrentMastery
                };
            }
            else if (invItem.ItemTemplate.Type == "InventoryExpansion")
            {
                int oldCap = profile.MaxCardCapacity;
                int gain = invItem.ItemTemplate.EffectValue > 0 ? invItem.ItemTemplate.EffectValue : 5;
                profile.MaxCardCapacity += gain;
                message = $"📁 UPLINK SECURED // Squad slot capacity expanded from {oldCap} to {profile.MaxCardCapacity} files!";
            }
            else if (invItem.ItemTemplate.Type == "GachaTicket")
            {
                var gachaResult = _gachaSummoner.PullViaTicket(profileId, invItem.ItemTemplate.Name);
                if (!gachaResult.Success)
                {
                    return new ItemUseResult { Success = false, Message = gachaResult.Message };
                }

                PlayerCard? pulledCard = gachaResult.PulledCards.FirstOrDefault();

                bool isConfiguredPack = invItem.ItemTemplate.Name.Equals("Ultimate Card Pack Ticket", StringComparison.OrdinalIgnoreCase) 
                                      || invItem.ItemTemplate.Name.Equals("Special Ultimate Card Pack Ticket", StringComparison.OrdinalIgnoreCase);
                if (isConfiguredPack)
                {
                    alreadyDecremented = true;
                }

                if (pulledCard != null)
                {
                    // Map pulled card details
                    var cardTemp = _dbContext.CardTemplates.FirstOrDefault(t => t.Id == pulledCard.CardTemplateId);
                    message = $"🃏 RECRUITMENT DEPLOYED // Successfully recruited {cardTemp?.VisualTitle} into active squad!";
                    updatedCard = new
                    {
                        id = pulledCard.Id,
                        templateId = pulledCard.CardTemplateId,
                        title = cardTemp?.Title ?? "Unknown Hero",
                        visualTitle = cardTemp?.VisualTitle ?? "Hero",
                        variant = cardTemp?.VariantName ?? "Base",
                        alignment = cardTemp?.Alignment ?? "Speed",
                        rarity = cardTemp?.Rarity ?? "Normal",
                        imageFile = cardTemp?.ImageFileName ?? "",
                        baseAtk = cardTemp?.BaseAtk ?? 1000,
                        baseDef = cardTemp?.BaseDef ?? 1000,
                        maxAtk = cardTemp?.MaxAtk ?? 4000,
                        maxDef = cardTemp?.MaxDef ?? 4000,
                        powerRequirement = cardTemp?.PowerRequirement ?? 5,
                        quote = cardTemp?.Quote ?? ""
                    };
                }
            }

            // Decrement ledger stock count
            if (!alreadyDecremented)
            {
                invItem.Quantity--;
                _dbContext.SaveChanges();
            }

            _logger.LogInformation($"[ItemLedger] Profile {profileId} successfully consumed item {itemId} ({invItem.ItemTemplate.Name}). Remaining: {invItem.Quantity}");

            return new ItemUseResult
            {
                Success = true,
                Message = message,
                RemainingQuantity = invItem.Quantity,
                Level = profile.Level,
                EnergyMax = profile.EnergyMax,
                EnergyCurrent = profile.EnergyCurrent,
                AttackPowerCurrent = profile.AttackPowerCurrent,
                AttackPowerMax = profile.AttackPower,
                DefensePowerCurrent = profile.DefensePowerCurrent,
                DefensePowerMax = profile.DefensePower,
                Silver = profile.SilverBalance,
                MobaCoin = profile.MobaCoinBalance,
                UpdatedCard = updatedCard
            };
        }
    }
}
