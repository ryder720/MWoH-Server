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

        public ItemUseResult UseItem(int profileId, int itemId)
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
            if (invItem.ItemTemplate!.Type == "EnergyRestorative")
            {
                int refill = (profile.EnergyMax * invItem.ItemTemplate.EffectValue) / 100;
                profile.EnergyCurrent = Math.Min(profile.EnergyMax, profile.EnergyCurrent + refill);
                message = $"Energy restored by {refill} points!";
            }
            else if (invItem.ItemTemplate.Type == "AttackPowerRestorative")
            {
                message = "Combat attack power fully replenished!";
            }
            else if (invItem.ItemTemplate.Type == "DefensePowerRestorative")
            {
                message = "Combat defense power fully replenished!";
            }
            else if (invItem.ItemTemplate.Type == "MasteryIso8")
            {
                message = "ISO-8 synthesized card mastery incremented successfully!";
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
                Silver = profile.SilverBalance,
                MobaCoin = profile.MobaCoinBalance
            };
        }
    }
}
