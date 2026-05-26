using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MwohServer.Data;
using MwohServer.Models;

namespace MwohServer.Services
{
    public class GachaSummoner : IGachaSummoner
    {
        private readonly MwohDbContext _dbContext;
        private readonly ILogger<GachaSummoner> _logger;

        public GachaSummoner(MwohDbContext dbContext, ILogger<GachaSummoner> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        public GachaResult Pull(int profileId, int packId, string currencyType, int pullCount)
        {
            _logger.LogInformation($"[GachaSummoner] Pull called for Profile: {profileId}, Pack: {packId}, Currency: {currencyType}, Count: {pullCount}");

            var profile = _dbContext.Profiles.FirstOrDefault(p => p.Id == profileId);
            if (profile == null)
            {
                return new GachaResult { Success = false, Message = "Profile not found." };
            }

            var configPath = Path.Combine(Directory.GetCurrentDirectory(), "Config", "gacha_config.json");
            if (!File.Exists(configPath))
            {
                return new GachaResult { Success = false, Message = "Gacha system is offline." };
            }

            string jsonText;
            try
            {
                jsonText = File.ReadAllText(configPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[GachaSummoner] Failed to read gacha config file.");
                return new GachaResult { Success = false, Message = "Failed to load gacha configuration." };
            }

            using var doc = JsonDocument.Parse(jsonText);
            var packs = doc.RootElement.EnumerateArray().ToList();
            var selectedPackNode = packs.FirstOrDefault(p => p.GetProperty("id").GetInt32() == packId);

            if (selectedPackNode.ValueKind == JsonValueKind.Undefined)
            {
                return new GachaResult { Success = false, Message = "Recruitment node not found." };
            }

            // Read costs
            int ticketItemId = 0;
            int costTicket = 0;
            if (selectedPackNode.TryGetProperty("ticket_item_id", out var ticketIdProp))
            {
                ticketItemId = ticketIdProp.GetInt32();
            }
            if (selectedPackNode.TryGetProperty("cost_ticket", out var costTicketProp))
            {
                costTicket = costTicketProp.GetInt32();
            }

            int totalCost = 0;
            PlayerInventoryItem? ticketInventoryItem = null;

            if (currencyType == "Rally")
            {
                if (!selectedPackNode.TryGetProperty("cost_rally", out var costRallyProp))
                {
                    return new GachaResult { Success = false, Message = "Rally currency not supported for this recruitment node." };
                }
                int costRally = costRallyProp.GetInt32();
                totalCost = costRally * pullCount;
                if (profile.RallyPoints < totalCost)
                {
                    return new GachaResult { Success = false, Message = "Insufficient Rally Points. Co-op rally required." };
                }
            }
            else if (currencyType == "Ticket")
            {
                if (ticketItemId == 0 || costTicket == 0)
                {
                    return new GachaResult { Success = false, Message = "Ticket currency not supported for this recruitment node." };
                }

                int totalTicketCost = costTicket * pullCount;
                ticketInventoryItem = _dbContext.PlayerInventoryItems
                    .Include(pi => pi.ItemTemplate)
                    .FirstOrDefault(pi => pi.PlayerProfileId == profileId && pi.ItemTemplateId == ticketItemId);

                if (ticketInventoryItem == null || ticketInventoryItem.Quantity < totalTicketCost)
                {
                    var itemName = ticketInventoryItem?.ItemTemplate?.Name ?? "Required Tickets";
                    return new GachaResult { Success = false, Message = $"Insufficient {itemName}. Acquisition denied." };
                }
            }
            else
            {
                int costMobacoin = 0;
                int costSilver = 0;
                if (selectedPackNode.TryGetProperty("cost_mobacoin", out var mobacoinProp)) costMobacoin = mobacoinProp.GetInt32();
                if (selectedPackNode.TryGetProperty("cost_silver", out var silverProp)) costSilver = silverProp.GetInt32();

                if (currencyType == "MobaCoin")
                {
                    if (costMobacoin <= 0)
                    {
                        return new GachaResult { Success = false, Message = "MobaCoin currency not supported for this recruitment node." };
                    }
                    totalCost = costMobacoin * pullCount;
                    if (profile.MobaCoinBalance < totalCost)
                    {
                        return new GachaResult { Success = false, Message = "Insufficient MobaCoins. Acquisition denied." };
                    }
                }
                else // Silver
                {
                    if (costSilver <= 0)
                    {
                        return new GachaResult { Success = false, Message = "Silver currency not supported for this recruitment node." };
                    }
                    totalCost = costSilver * pullCount;
                    if (profile.SilverBalance < totalCost)
                    {
                        return new GachaResult { Success = false, Message = "Insufficient Silver resources. Acquisition denied." };
                    }
                }
            }

            // Verify inventory capacity (MaxCardCapacity limit)
            int currentCardCount = _dbContext.PlayerCards.Count(pc => pc.PlayerProfileId == profile.Id);
            if (currentCardCount + pullCount > profile.MaxCardCapacity)
            {
                return new GachaResult { Success = false, Message = $"Inventory capacity reached ({currentCardCount}/{profile.MaxCardCapacity}). Clear squad space first." };
            }

            // Deduct cost
            if (currencyType == "Rally")
            {
                profile.RallyPoints -= totalCost;
            }
            else if (currencyType == "Ticket")
            {
                if (ticketInventoryItem != null)
                {
                    ticketInventoryItem.Quantity -= costTicket * pullCount;
                }
            }
            else if (currencyType == "MobaCoin")
            {
                profile.MobaCoinBalance -= totalCost;
            }
            else
            {
                profile.SilverBalance -= totalCost;
            }

            // Get rates
            var ratesNode = selectedPackNode.GetProperty("rates");
            var ratesDict = new Dictionary<string, double>();
            foreach (var prop in ratesNode.EnumerateObject())
            {
                ratesDict[prop.Name] = prop.Value.GetDouble();
            }

            // Roll cards and construct entity objects
            var rand = new Random();
            var rolledCards = new List<PlayerCard>();
            
            for (int i = 0; i < pullCount; i++)
            {
                var roll = rand.NextDouble() * 100.0;
                var chosenRarity = "Normal";
                double cumulative = 0.0;

                foreach (var kvp in ratesDict)
                {
                    cumulative += kvp.Value;
                    if (roll <= cumulative)
                    {
                        chosenRarity = kvp.Key;
                        break;
                    }
                }

                var rarityOptions = new List<string> { chosenRarity };
                if (chosenRarity == "Super Rare") rarityOptions.Add("SR");
                if (chosenRarity == "Legendary") rarityOptions.Add("Legend");

                var templates = _dbContext.CardTemplates
                    .AsEnumerable()
                    .Where(t => rarityOptions.Contains(t.Rarity, StringComparer.OrdinalIgnoreCase))
                    .ToList();

                if (!templates.Any())
                {
                    templates = _dbContext.CardTemplates.ToList();
                }

                if (!templates.Any())
                {
                    return new GachaResult { Success = false, Message = "No tactical asset blueprints in database." };
                }

                var chosenTemplate = templates[rand.Next(templates.Count)];

                var newCard = new PlayerCard
                {
                    PlayerProfileId = profile.Id
                };
                newCard.InitializeStats(chosenTemplate, GameplaySettings.DefaultMasteryPercentage);
                
                // Track internally and save to EF Context
                _dbContext.PlayerCards.Add(newCard);
                rolledCards.Add(newCard);
            }

            try
            {
                // Save changes in a single SQL transaction
                _dbContext.SaveChanges();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[GachaSummoner] Failed to commit player cards to SQLite.");
                return new GachaResult { Success = false, Message = "Database write error occurred." };
            }

            // Fetch templates eagerly to ensure navigation properties are populated for the return model
            foreach (var card in rolledCards)
            {
                card.CardTemplate = _dbContext.CardTemplates.FirstOrDefault(t => t.Id == card.CardTemplateId);
            }

            _logger.LogInformation($"[GachaSummoner] Successfully pulled {pullCount} cards for profile {profileId}.");

            return new GachaResult
            {
                Success = true,
                NewMobaCoins = profile.MobaCoinBalance,
                NewSilver = profile.SilverBalance,
                NewRallyPoints = profile.RallyPoints,
                PulledCards = rolledCards
            };
        }
    }
}
