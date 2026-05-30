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

        public ItemLedger(ILogger<ItemLedger> logger, MwohDbContext dbContext, IGachaSummoner gachaSummoner)
        {
            _logger = logger;
            _dbContext = dbContext;
            _gachaSummoner = gachaSummoner;
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
            else if (invItem.ItemTemplate.Type == "GachaTicket")
            {
                // Verify squad capacity limit first
                int currentCardCount = _dbContext.PlayerCards.Count(pc => pc.PlayerProfileId == profileId);
                if (currentCardCount + 1 > profile.MaxCardCapacity)
                {
                    return new ItemUseResult { Success = false, Message = $"Inventory capacity reached ({currentCardCount}/{profile.MaxCardCapacity}). Clear squad space first." };
                }

                PlayerCard? pulledCard = null;
                string ticketName = invItem.ItemTemplate.Name;

                // 1. Direct integration with GachaSummoner for configured ticket packs
                if (ticketName.Equals("Ultimate Card Pack Ticket", StringComparison.OrdinalIgnoreCase))
                {
                    var gachaResult = _gachaSummoner.Pull(profileId, 1, "Ticket", 1);
                    if (!gachaResult.Success)
                    {
                        return new ItemUseResult { Success = false, Message = gachaResult.Message };
                    }
                    pulledCard = gachaResult.PulledCards.FirstOrDefault();
                    alreadyDecremented = true;
                }
                else if (ticketName.Equals("Special Ultimate Card Pack Ticket", StringComparison.OrdinalIgnoreCase))
                {
                    // Map special ticket to elite recruitment node (Pack ID 2)
                    var gachaResult = _gachaSummoner.Pull(profileId, 2, "Ticket", 1);
                    if (!gachaResult.Success)
                    {
                        return new ItemUseResult { Success = false, Message = gachaResult.Message };
                    }
                    pulledCard = gachaResult.PulledCards.FirstOrDefault();
                    alreadyDecremented = true;
                }
                else
                {
                    // 2. Custom programmatic rolls for specialized tickets
                    var rand = new Random();
                    var roll = rand.NextDouble() * 100.0;
                    string chosenRarity = "Normal";

                    // Determine rates based on ticket type
                    if (ticketName.Equals("Half-Anniversary Ticket", StringComparison.OrdinalIgnoreCase))
                    {
                        // Enhanced elite rates
                        if (roll <= 3.0) chosenRarity = "Legendary";
                        else if (roll <= 15.0) chosenRarity = "Super Rare";
                        else if (roll <= 60.0) chosenRarity = "Rare";
                        else chosenRarity = "Normal";
                    }
                    else
                    {
                        // Standard rates
                        if (roll <= 1.0) chosenRarity = "Legendary";
                        else if (roll <= 5.0) chosenRarity = "Super Rare";
                        else if (roll <= 30.0) chosenRarity = "Rare";
                        else chosenRarity = "Normal";
                    }

                    var rarityOptions = new List<string> { chosenRarity };
                    if (chosenRarity == "Normal") { rarityOptions.Add("Common"); rarityOptions.Add("Uncommon"); }
                    else if (chosenRarity == "Rare") { rarityOptions.Add("Special Rare"); }
                    else if (chosenRarity == "Super Rare") { rarityOptions.Add("SR"); rarityOptions.Add("Super Special Rare"); rarityOptions.Add("Ultimate Rare"); }
                    else if (chosenRarity == "Legendary") { rarityOptions.Add("Legend"); rarityOptions.Add("Ultimate Legendary"); }

                    // Fetch base card templates
                    var query = _dbContext.CardTemplates.AsEnumerable()
                        .Where(t => !t.VariantName.Contains("+") && !t.Title.Contains("+"));

                    // Apply filters based on ticket name
                    if (ticketName.Contains("Super Hero", StringComparison.OrdinalIgnoreCase))
                    {
                        query = query.Where(t => t.Faction.Equals("Super Hero", StringComparison.OrdinalIgnoreCase));
                    }
                    else if (ticketName.Contains("Bruiser", StringComparison.OrdinalIgnoreCase))
                    {
                        query = query.Where(t => t.Alignment.Equals("Bruiser", StringComparison.OrdinalIgnoreCase));
                    }
                    else if (ticketName.Contains("Tactics", StringComparison.OrdinalIgnoreCase))
                    {
                        query = query.Where(t => t.Alignment.Equals("Tactics", StringComparison.OrdinalIgnoreCase));
                    }
                    else if (ticketName.Contains("Speed", StringComparison.OrdinalIgnoreCase))
                    {
                        query = query.Where(t => t.Alignment.Equals("Speed", StringComparison.OrdinalIgnoreCase));
                    }

                    var filteredTemplates = query.Where(t => rarityOptions.Contains(t.Rarity, StringComparer.OrdinalIgnoreCase)).ToList();

                    // Fallback to any matching template if no cards match rarity
                    if (!filteredTemplates.Any())
                    {
                        filteredTemplates = query.ToList();
                    }

                    if (!filteredTemplates.Any())
                    {
                        // Direct database fallback to avoid crashes
                        filteredTemplates = _dbContext.CardTemplates.Where(t => !t.VariantName.Contains("+")).ToList();
                    }

                    var chosenTemplate = filteredTemplates[rand.Next(filteredTemplates.Count)];
                    pulledCard = new PlayerCard { PlayerProfileId = profileId };
                    pulledCard.InitializeStats(chosenTemplate, GameplaySettings.DefaultMasteryPercentage);

                    _dbContext.PlayerCards.Add(pulledCard);
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
