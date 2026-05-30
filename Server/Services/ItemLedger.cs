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

        public ItemLedger(ILogger<ItemLedger> logger, MwohDbContext dbContext)
        {
            _logger = logger;
            _dbContext = dbContext;
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
                message = $"Attack Power restored by {refill} points!";
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

                var card = _dbContext.PlayerCards
                    .Include(pc => pc.CardTemplate)
                    .FirstOrDefault(pc => pc.PlayerProfileId == profileId && pc.Id == targetCardId);

                if (card == null)
                {
                    return new ItemUseResult { Success = false, Message = "Target card not located in catalog." };
                }

                int maxLevel = card.GetMaxLevel();
                if (card.CurrentLevel >= maxLevel)
                {
                    return new ItemUseResult { Success = false, Message = $"⚠️ CAP REACHED // {card.CardTemplate?.Title} is already at its rarity clearance limit (Lv. {maxLevel})." };
                }

                int boost = invItem.ItemTemplate.EffectValue > 0 ? invItem.ItemTemplate.EffectValue : 3;
                int oldLevel = card.CurrentLevel;
                card.CurrentLevel = Math.Min(maxLevel, card.CurrentLevel + boost);
                int levelsGained = card.CurrentLevel - oldLevel;

                card.RecalculateStats();

                message = $"🧪 SERUM INJECTED // {card.CardTemplate?.Title} boosted by +{levelsGained} level(s)!";
                updatedCard = new
                {
                    id = card.Id,
                    level = card.CurrentLevel,
                    atk = card.CurrentAtk,
                    def = card.CurrentDef,
                    masteryCur = card.CurrentMastery
                };
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

            // Decrement ledger stock count
            invItem.Quantity--;
            _dbContext.SaveChanges();

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
