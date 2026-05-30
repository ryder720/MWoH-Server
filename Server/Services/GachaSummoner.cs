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
        private readonly IAssignmentEngine _assignmentEngine;

        public GachaSummoner(MwohDbContext dbContext, ILogger<GachaSummoner> logger, IAssignmentEngine assignmentEngine)
        {
            _dbContext = dbContext;
            _logger = logger;
            _assignmentEngine = assignmentEngine;
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

            // 1. Read card pool filter from pack config
            var cardPool = new List<string>();
            if (selectedPackNode.TryGetProperty("card_pool", out var poolProp) && poolProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in poolProp.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var strVal = item.GetString();
                        if (!string.IsNullOrEmpty(strVal))
                        {
                            cardPool.Add(strVal);
                        }
                    }
                }
            }

            // 2. Read featured cards weights from pack config
            var featuredCards = new Dictionary<string, double>();
            if (selectedPackNode.TryGetProperty("featured_cards", out var featProp) && featProp.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in featProp.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Number)
                    {
                        featuredCards[prop.Name] = prop.Value.GetDouble();
                    }
                }
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
                if (chosenRarity.Equals("Normal", StringComparison.OrdinalIgnoreCase))
                {
                    rarityOptions.Add("Common");
                    rarityOptions.Add("Uncommon");
                }
                else if (chosenRarity.Equals("Rare", StringComparison.OrdinalIgnoreCase))
                {
                    rarityOptions.Add("Special Rare");
                }
                else if (chosenRarity.Equals("Super Rare", StringComparison.OrdinalIgnoreCase))
                {
                    rarityOptions.Add("SR");
                    rarityOptions.Add("Super Special Rare");
                    rarityOptions.Add("Ultimate Rare");
                }
                else if (chosenRarity.Equals("Legendary", StringComparison.OrdinalIgnoreCase))
                {
                    rarityOptions.Add("Legend");
                    rarityOptions.Add("Ultimate Legendary");
                }

                // Retrieve all base (non-fused) templates matching rarity
                var templates = _dbContext.CardTemplates
                    .AsEnumerable()
                    .Where(t => rarityOptions.Contains(t.Rarity, StringComparer.OrdinalIgnoreCase)
                                && !t.VariantName.Contains("+")
                                && !t.Title.Contains("+"))
                    .ToList();

                // Apply card pool filter if defined
                if (cardPool.Any())
                {
                    templates = templates
                        .Where(t => cardPool.Any(title => t.Title.Equals(title, StringComparison.OrdinalIgnoreCase) 
                                                         || t.Title.Contains(title, StringComparison.OrdinalIgnoreCase)))
                        .ToList();
                }

                // Fallback: If no base templates found for this rarity, fall back to any base template
                if (!templates.Any())
                {
                    templates = _dbContext.CardTemplates
                        .AsEnumerable()
                        .Where(t => !t.VariantName.Contains("+") && !t.Title.Contains("+"))
                        .ToList();

                    if (cardPool.Any())
                    {
                        var poolFallback = templates
                            .Where(t => cardPool.Any(title => t.Title.Equals(title, StringComparison.OrdinalIgnoreCase) 
                                                             || t.Title.Contains(title, StringComparison.OrdinalIgnoreCase)))
                            .ToList();
                        if (poolFallback.Any()) templates = poolFallback;
                    }
                }

                if (!templates.Any())
                {
                    return new GachaResult { Success = false, Message = "No tactical asset blueprints in database." };
                }

                // Weighted random selection based on featured rates
                CardTemplate chosenTemplate;
                if (featuredCards.Any())
                {
                    var weights = new List<(CardTemplate Template, double Weight)>();
                    double totalWeight = 0.0;

                    foreach (var t in templates)
                    {
                        double weight = 1.0;
                        var matchedFeatKey = featuredCards.Keys.FirstOrDefault(k => 
                            t.Title.Equals(k, StringComparison.OrdinalIgnoreCase) || 
                            t.Title.Contains(k, StringComparison.OrdinalIgnoreCase));
                        
                        if (matchedFeatKey != null)
                        {
                            weight = featuredCards[matchedFeatKey];
                        }

                        weights.Add((t, weight));
                        totalWeight += weight;
                    }

                    var rollWeight = rand.NextDouble() * totalWeight;
                    double currentWeightSum = 0.0;
                    chosenTemplate = templates.First(); // Default

                    foreach (var item in weights)
                    {
                        currentWeightSum += item.Weight;
                        if (rollWeight <= currentWeightSum)
                        {
                            chosenTemplate = item.Template;
                            break;
                        }
                    }
                }
                else
                {
                    chosenTemplate = templates[rand.Next(templates.Count)];
                }

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
                
                // Trigger assignment hook
                if (currencyType == "Rally")
                {
                    _assignmentEngine.RecordEvent(profileId, GoalType.DrawRallyPack, pullCount);
                }
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

        public GachaResult PullViaTicket(int profileId, string ticketName)
        {
            _logger.LogInformation($"[GachaSummoner] PullViaTicket called for Profile: {profileId}, Ticket: {ticketName}");

            // 1. Direct integration with configured ticket packs
            if (ticketName.Equals("Ultimate Card Pack Ticket", StringComparison.OrdinalIgnoreCase))
            {
                return Pull(profileId, 1, "Ticket", 1);
            }
            if (ticketName.Equals("Special Ultimate Card Pack Ticket", StringComparison.OrdinalIgnoreCase))
            {
                return Pull(profileId, 2, "Ticket", 1);
            }

            var profile = _dbContext.Profiles.FirstOrDefault(p => p.Id == profileId);
            if (profile == null)
            {
                return new GachaResult { Success = false, Message = "Profile not found." };
            }

            // Verify squad capacity limit
            int currentCardCount = _dbContext.PlayerCards.Count(pc => pc.PlayerProfileId == profileId);
            if (currentCardCount + 1 > profile.MaxCardCapacity)
            {
                return new GachaResult { Success = false, Message = $"Inventory capacity reached ({currentCardCount}/{profile.MaxCardCapacity}). Clear squad space first." };
            }

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
            var pulledCard = new PlayerCard { PlayerProfileId = profileId };
            pulledCard.InitializeStats(chosenTemplate, GameplaySettings.DefaultMasteryPercentage);

            _dbContext.PlayerCards.Add(pulledCard);
            _dbContext.SaveChanges();

            // Eagerly fetch/load template properties for navigation
            pulledCard.CardTemplate = _dbContext.CardTemplates.FirstOrDefault(t => t.Id == pulledCard.CardTemplateId);

            _logger.LogInformation($"[GachaSummoner] Ticket recruitment successful. Profile: {profileId} recruited {pulledCard.CardTemplate?.VisualTitle}");

            return new GachaResult
            {
                Success = true,
                Message = $"Successfully recruited {pulledCard.CardTemplate?.VisualTitle} into active squad!",
                NewMobaCoins = profile.MobaCoinBalance,
                NewSilver = profile.SilverBalance,
                NewRallyPoints = profile.RallyPoints,
                PulledCards = new List<PlayerCard> { pulledCard }
            };
        }
    }
}
