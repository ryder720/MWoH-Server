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
    public class ShopEngine : IShopEngine
    {
        private readonly ILogger<ShopEngine> _logger;
        private readonly MwohDbContext _dbContext;
        private List<ShopPackage> _packages = new();
        private readonly string _configPath;

        public ShopEngine(ILogger<ShopEngine> logger, MwohDbContext dbContext)
        {
            _logger = logger;
            _dbContext = dbContext;
            _configPath = Path.Combine(Directory.GetCurrentDirectory(), "Config", "shop_config.json");
            LoadConfig();
        }

        public void LoadConfig()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    string jsonText = File.ReadAllText(_configPath);
                    var parsed = JsonSerializer.Deserialize<List<ShopPackage>>(jsonText, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    if (parsed != null)
                    {
                        _packages = parsed;
                        _logger.LogInformation($"[ShopEngine] Successfully loaded {_packages.Count} shop packages from config.");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ShopEngine] Error loading shop config: {ex.Message}");
            }

            // Fallback packages if config loading fails
            _packages = new List<ShopPackage>
            {
                new() { Id = "mobacoin_1k", Name = "1,000 MobaCoins Pack", Description = "Secure high-priority virtual currency credits.", Type = "MobaCoin", Quantity = 1000, SilverCost = 50000, IsFree = false },
                new() { Id = "power_pack_1", Name = "Power Pack", Description = "Restores both AP and DP.", Type = "Item", ItemTemplateName = "Power Pack", Quantity = 1, SilverCost = 10000, IsFree = false },
                new() { Id = "energy_pack_1", Name = "Energy Pack", Description = "Restores Energy.", Type = "Item", ItemTemplateName = "Energy Pack", Quantity = 1, SilverCost = 5000, IsFree = false }
            };
            _logger.LogWarning("[ShopEngine] Initialized fallback shop packages.");
        }

        public List<ShopPackage> GetShopPackages()
        {
            return _packages;
        }

        public void ReloadConfig()
        {
            LoadConfig();
        }

        public ShopPurchaseResult PurchasePackage(int profileId, string packageId)
        {
            var package = _packages.FirstOrDefault(p => p.Id == packageId);
            if (package == null)
            {
                return new ShopPurchaseResult { Success = false, Message = "Package not found in database." };
            }

            // Enforce ACID compliance using EF Core Transaction block
            using var transaction = _dbContext.Database.BeginTransaction();
            try
            {
                var profile = _dbContext.Profiles
                    .Include(p => p.InventoryItems)
                    .FirstOrDefault(p => p.Id == profileId);

                if (profile == null)
                {
                    return new ShopPurchaseResult { Success = false, Message = "Player profile mismatch." };
                }

                // Balance check
                if (!package.IsFree)
                {
                    if (profile.SilverBalance < package.SilverCost)
                    {
                        return new ShopPurchaseResult 
                        { 
                            Success = false, 
                            Message = $"⚠️ INSUFFICIENT SILVER // Costs {package.SilverCost:N0} Silver but you only possess {profile.SilverBalance:N0}." 
                        };
                    }

                    // Deduct Silver
                    profile.SilverBalance -= package.SilverCost;
                }

                // Distribute reward
                if (package.Type.Equals("MobaCoin", StringComparison.OrdinalIgnoreCase))
                {
                    profile.MobaCoinBalance += package.Quantity;
                    _dbContext.SaveChanges();
                    transaction.Commit();

                    _logger.LogInformation($"[ShopEngine] Profile {profileId} purchased {package.Name}. Gained {package.Quantity} MobaCoins.");
                    return new ShopPurchaseResult
                    {
                        Success = true,
                        Message = $"🔋 ACQUIRED // Added {package.Quantity:N0} MobaCoins to active balance!",
                        NewSilverBalance = profile.SilverBalance,
                        NewMobaCoinBalance = profile.MobaCoinBalance,
                        AddedQuantity = package.Quantity,
                        ItemName = "MobaCoin"
                    };
                }
                else if (package.Type.Equals("Item", StringComparison.OrdinalIgnoreCase))
                {
                    var templateName = package.ItemTemplateName;
                    var templateNameLower = templateName.ToLower();
                    var template = _dbContext.ItemTemplates.FirstOrDefault(t => t.Name.ToLower() == templateNameLower);
                    
                    if (template == null)
                    {
                        // Fallback auto-seeding logic
                        _logger.LogWarning($"[ShopEngine] ItemTemplate '{templateName}' not found in database. Initiating auto-seeder...");
                        
                        string type = "EnergyRestorative";
                        string desc = "Restores active reserves.";
                        string img = "item_energy_full.png";

                        if (templateName.Contains("Power Pack", StringComparison.OrdinalIgnoreCase))
                        {
                            type = "AttackPowerRestorative";
                            desc = "A premium S.H.I.E.L.D. tactical supply pack that fully restores both Attack Power and Defense Power.";
                            img = "item_power_pack.png";
                        }
                        else
                        {
                            type = "EnergyRestorative";
                            desc = "A premium S.H.I.E.L.D. energy reserve pack that fully restores active Energy.";
                            img = "item_energy_pack.png";
                        }

                        template = new ItemTemplate
                        {
                            Name = templateName,
                            Description = desc,
                            Type = type,
                            EffectValue = 100,
                            ImageFileName = img
                        };
                        _dbContext.ItemTemplates.Add(template);
                        _dbContext.SaveChanges();
                    }

                    // Find or create PlayerInventoryItem
                    var invItem = _dbContext.PlayerInventoryItems
                        .FirstOrDefault(pi => pi.PlayerProfileId == profileId && pi.ItemTemplateId == template.Id);

                    if (invItem == null)
                    {
                        invItem = new PlayerInventoryItem
                        {
                            PlayerProfileId = profileId,
                            ItemTemplateId = template.Id,
                            Quantity = 0
                        };
                        _dbContext.PlayerInventoryItems.Add(invItem);
                    }

                    invItem.Quantity += package.Quantity;
                    _dbContext.SaveChanges();
                    transaction.Commit();

                    _logger.LogInformation($"[ShopEngine] Profile {profileId} purchased {package.Name}. Gained {package.Quantity}x {template.Name}.");
                    return new ShopPurchaseResult
                    {
                        Success = true,
                        Message = $"📦 ACQUIRED // Added {package.Quantity}x {template.Name} directly to inventory locker!",
                        NewSilverBalance = profile.SilverBalance,
                        NewMobaCoinBalance = profile.MobaCoinBalance,
                        AddedQuantity = package.Quantity,
                        ItemName = template.Name
                    };
                }
                else
                {
                    return new ShopPurchaseResult { Success = false, Message = "Unknown reward configuration type." };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ShopEngine] ACID Transaction aborted: {ex.Message}");
                try
                {
                    transaction.Rollback();
                }
                catch { /* Ignore double fail */ }
                return new ShopPurchaseResult { Success = false, Message = "Database sync error. Transaction aborted securely." };
            }
        }
    }
}
