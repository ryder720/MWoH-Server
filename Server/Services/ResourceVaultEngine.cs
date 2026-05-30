using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MwohServer.Data;
using MwohServer.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace MwohServer.Services
{
    public class ResourceVaultEngine : IResourceVaultEngine
    {
        private readonly MwohDbContext _dbContext;
        private readonly ILogger<ResourceVaultEngine> _logger;

        public ResourceVaultEngine(MwohDbContext dbContext, ILogger<ResourceVaultEngine> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        public RedeemResult Redeem(int profileId, string groupKey)
        {
            _logger.LogInformation($"[ResourceVaultEngine] Redeem called for profile {profileId}, groupKey: {groupKey}");

            var profile = _dbContext.Profiles
                .Include(p => p.InventoryItems)
                    .ThenInclude(pi => pi.ItemTemplate)
                .Include(p => p.Cards)
                .FirstOrDefault(p => p.Id == profileId);

            if (profile == null)
            {
                return new RedeemResult { Success = false, Message = "Profile mismatch." };
            }

            if (string.IsNullOrEmpty(groupKey))
            {
                return new RedeemResult { Success = false, Message = "Group key missing." };
            }

            string groupName = groupKey switch
            {
                "StormsCape" => "Storm's Cape",
                "Suitcase" => "Suitcase",
                "SwordOfProficiency" => "Sword of Proficiency",
                "AssassinsChoker" => "Assassin's Choker",
                "ChainBelt" => "Chain Belt",
                "Geirr" => "Geirr",
                "ProjectileArray" => "Projectile Array",
                _ => string.Empty
            };

            if (string.IsNullOrEmpty(groupName))
            {
                return new RedeemResult { Success = false, Message = "Invalid resource group." };
            }

            var redemptionsDict = new Dictionary<string, int>();
            if (!string.IsNullOrEmpty(profile.ResourceRedemptionsJson))
            {
                try
                {
                    redemptionsDict = JsonSerializer.Deserialize<Dictionary<string, int>>(profile.ResourceRedemptionsJson) ?? new();
                }
                catch { }
            }

            int count = 0;
            redemptionsDict.TryGetValue(groupKey, out count);
            if (count >= 3)
            {
                return new RedeemResult { Success = false, Message = "⚠️ MAXIMUM REDEMPTIONS REACHED // S.H.I.E.L.D. data caps reached." };
            }

            var groupTemplates = _dbContext.ItemTemplates
                .Where(t => t.Type == "Resource" && (
                    (groupKey == "StormsCape" && t.Name.Contains("Storm's") && t.Name.Contains("Cape")) ||
                    (groupKey == "Suitcase" && t.Name.Contains("Suitcase")) ||
                    (groupKey == "SwordOfProficiency" && t.Name.Contains("Sword")) ||
                    (groupKey == "AssassinsChoker" && t.Name.Contains("Assassin's") && t.Name.Contains("Choker")) ||
                    (groupKey == "ChainBelt" && t.Name.Contains("Chain Belt")) ||
                    (groupKey == "Geirr" && t.Name.Contains("Geirr")) ||
                    (groupKey == "ProjectileArray" && t.Name.Contains("Projectile Array"))
                )).ToList();

            if (groupTemplates.Count < 6)
            {
                return new RedeemResult { Success = false, Message = "⚠️ SYSTEM ERROR // Template files corrupted." };
            }

            var inventoryMatch = new List<PlayerInventoryItem>();
            foreach (var temp in groupTemplates)
            {
                var pItem = profile.InventoryItems.FirstOrDefault(pi => pi.ItemTemplateId == temp.Id && pi.Quantity >= 1);
                if (pItem == null)
                {
                    return new RedeemResult { Success = false, Message = "⚠️ SET INCOMPLETE // Missing required colors." };
                }
                inventoryMatch.Add(pItem);
            }

            int setIndex = count + 1;
            string rewardMessage = "";
            string rewardCardTitle = "";
            bool isCardReward = true;

            if (setIndex == 1 || setIndex == 3)
            {
                rewardCardTitle = groupKey switch
                {
                    "StormsCape" => "Queen of Lightning Storm",
                    "Suitcase" => "Legal Eagle She-Hulk",
                    "SwordOfProficiency" => "Taskmaster",
                    "AssassinsChoker" => "X-23",
                    "ChainBelt" => "Knuckle Up Luke Cage",
                    "Geirr" => "Escort of Souls Valkyrie",
                    "ProjectileArray" => "Friend In Need War Machine",
                    _ => ""
                };

                int currentCardCount = _dbContext.PlayerCards.Count(pc => pc.PlayerProfileId == profile.Id);
                if (currentCardCount >= profile.MaxCardCapacity)
                {
                    return new RedeemResult { Success = false, Message = $"⚠️ DEPLOYMENT REJECTED // SQUAD FILES FULL ({profile.MaxCardCapacity}/{profile.MaxCardCapacity})." };
                }
            }
            else
            {
                isCardReward = false;
            }

            // Deduct resource items
            foreach (var pItem in inventoryMatch)
            {
                pItem.Quantity = Math.Max(0, pItem.Quantity - 1);
            }

            if (isCardReward)
            {
                var cardTemplate = _dbContext.CardTemplates.FirstOrDefault(t => t.Title == rewardCardTitle);
                if (cardTemplate == null)
                {
                    cardTemplate = _dbContext.CardTemplates.FirstOrDefault();
                }

                if (cardTemplate != null)
                {
                    var newCard = new PlayerCard { PlayerProfileId = profile.Id };
                    newCard.InitializeStats(cardTemplate, GameplaySettings.DefaultMasteryPercentage);
                    _dbContext.PlayerCards.Add(newCard);
                    rewardMessage = $"RECOVERED HERO: {cardTemplate.VisualTitle ?? cardTemplate.Title} added to your deck roster!";
                }
            }
            else
            {
                var serumTemplate = _dbContext.ItemTemplates.FirstOrDefault(t => t.Type == "LevelUpSerum");
                if (serumTemplate != null)
                {
                    var pItem = _dbContext.PlayerInventoryItems.FirstOrDefault(pi => pi.PlayerProfileId == profile.Id && pi.ItemTemplateId == serumTemplate.Id);
                    if (pItem == null)
                    {
                        pItem = new PlayerInventoryItem
                        {
                            PlayerProfileId = profile.Id,
                            ItemTemplateId = serumTemplate.Id,
                            Quantity = 3
                        };
                        _dbContext.PlayerInventoryItems.Add(pItem);
                    }
                    else
                    {
                        pItem.Quantity += 3;
                    }
                    rewardMessage = $"SECURED SUPPLIES: Added 3x Level-Up ISO-8 Serums to tactical depot!";
                }
                else
                {
                    rewardMessage = "ISO-8 supply seeder failed.";
                }
            }

            redemptionsDict[groupKey] = count + 1;
            profile.ResourceRedemptionsJson = JsonSerializer.Serialize(redemptionsDict);

            try
            {
                _dbContext.SaveChanges();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ResourceVaultEngine] Failed to commit resource set redemption to database.");
                return new RedeemResult { Success = false, Message = "Database write error occurred during reward grant." };
            }

            var updatedResources = profile.InventoryItems
                .Where(pi => pi.ItemTemplate != null && pi.ItemTemplate.Type == "Resource")
                .Select(pi => new UpdatedResourceDto
                {
                    Id = pi.ItemTemplateId,
                    Qty = pi.Quantity
                }).ToList();

            return new RedeemResult
            {
                Success = true,
                Message = $"CONGRATULATIONS // {rewardMessage}",
                Redemptions = redemptionsDict,
                UpdatedResources = updatedResources
            };
        }

        public DonateResult Donate(int profileId, string groupKey)
        {
            _logger.LogInformation($"[ResourceVaultEngine] Donate called for profile {profileId}, groupKey: {groupKey}");

            var profile = _dbContext.Profiles
                .Include(p => p.InventoryItems)
                    .ThenInclude(pi => pi.ItemTemplate)
                .FirstOrDefault(p => p.Id == profileId);

            if (profile == null)
            {
                return new DonateResult { Success = false, Message = "Profile mismatch." };
            }

            if (string.IsNullOrEmpty(groupKey))
            {
                return new DonateResult { Success = false, Message = "Group key missing." };
            }

            var groupResources = profile.InventoryItems
                .Where(pi => pi.ItemTemplate != null && pi.ItemTemplate.Type == "Resource" && (
                    (groupKey == "StormsCape" && pi.ItemTemplate.Name.Contains("Storm's") && pi.ItemTemplate.Name.Contains("Cape")) ||
                    (groupKey == "Suitcase" && pi.ItemTemplate.Name.Contains("Suitcase")) ||
                    (groupKey == "SwordOfProficiency" && pi.ItemTemplate.Name.Contains("Sword")) ||
                    (groupKey == "AssassinsChoker" && pi.ItemTemplate.Name.Contains("Assassin's") && pi.ItemTemplate.Name.Contains("Choker")) ||
                    (groupKey == "ChainBelt" && pi.ItemTemplate.Name.Contains("Chain Belt")) ||
                    (groupKey == "Geirr" && pi.ItemTemplate.Name.Contains("Geirr")) ||
                    (groupKey == "ProjectileArray" && pi.ItemTemplate.Name.Contains("Projectile Array"))
                ) && pi.Quantity > 0).ToList();

            if (groupResources.Count == 0)
            {
                return new DonateResult { Success = false, Message = "⚠️ RESOURCE STOCK EMPTY // No excess drops to donate." };
            }

            long totalSilverGained = 0;
            int totalItemsDonated = 0;

            foreach (var pi in groupResources)
            {
                int quantity = pi.Quantity;
                int valuePerItem = pi.ItemTemplate?.EffectValue ?? 2000;
                totalSilverGained += (long)quantity * valuePerItem;
                totalItemsDonated += quantity;

                pi.Quantity = 0;
            }

            profile.SilverBalance += totalSilverGained;

            try
            {
                _dbContext.SaveChanges();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ResourceVaultEngine] Failed to commit resource donations to database.");
                return new DonateResult { Success = false, Message = "Database write error occurred during resource donation." };
            }

            var updatedResources = profile.InventoryItems
                .Where(pi => pi.ItemTemplate != null && pi.ItemTemplate.Type == "Resource")
                .Select(pi => new UpdatedResourceDto
                {
                    Id = pi.ItemTemplateId,
                    Qty = pi.Quantity
                }).ToList();

            return new DonateResult
            {
                Success = true,
                Message = $"DONATION COMPLETE // Contributed {totalItemsDonated} assets to S.H.I.E.L.D. tactical mainframe. Credited +{totalSilverGained} Silver!",
                SilverBalance = profile.SilverBalance,
                UpdatedResources = updatedResources
            };
        }
    }
}
