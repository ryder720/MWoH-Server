using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Net.Http;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using MwohServer.Models;

namespace MwohServer.Data
{
    public static class DatabaseSeeder
    {
        public static void SeedCards(MwohDbContext context, ILogger logger)
        {
            if (context.CardTemplates.Any())
            {
                logger.LogInformation("[Seeder] Card templates already seeded. Skipping JSON import.");
                return;
            }

            // Path mapping relative to compiled assembly location or project execution folder
            string jsonPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Tools", "Scraper", "cards_db.json");
            
            if (!File.Exists(jsonPath))
            {
                // Fallback for execution from main project directory (dotnet run)
                jsonPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "Tools", "Scraper", "cards_db.json");
            }

            if (!File.Exists(jsonPath))
            {
                logger.LogWarning($"[Seeder] JSON card database not found at '{jsonPath}'. Seeding default fallback cards.");
                SeedFallbacks(context);
                return;
            }

            try
            {
                string jsonText = File.ReadAllText(jsonPath);
                using (JsonDocument doc = JsonDocument.Parse(jsonText))
                {
                    int addedCount = 0;
                    foreach (var cardElement in doc.RootElement.EnumerateArray())
                    {
                        string title = cardElement.GetProperty("title").GetString() ?? "";
                        string alignment = cardElement.GetProperty("alignment").GetString() ?? "";
                        
                        var general = cardElement.GetProperty("general");
                        string faction = general.TryGetProperty("faction", out var f) ? f.GetString() ?? "None" : "None";
                        string gender = general.TryGetProperty("gender", out var g) ? g.GetString() ?? "None" : "None";
                        string abilityName = general.TryGetProperty("ability_name", out var abN) ? abN.GetString() ?? "" : "";
                        string abilityEffect = general.TryGetProperty("ability_effect", out var abE) ? abE.GetString() ?? "" : "";

                        // Parse each variant inside the card as an individual SQLite CardTemplate
                        var variants = cardElement.GetProperty("variants");
                        int variantIndex = 0;
                        foreach (var variantProperty in variants.EnumerateObject())
                        {
                            string variantName = variantProperty.Name;
                            var varEl = variantProperty.Value;

                            string rarity = varEl.TryGetProperty("rarity", out var r) ? r.GetString() ?? "Normal" : "Normal";
                            
                            int power = 10;
                            if (varEl.TryGetProperty("power", out var pVal))
                            {
                                if (pVal.ValueKind == JsonValueKind.Number) power = pVal.GetInt32();
                                else if (pVal.ValueKind == JsonValueKind.String && int.TryParse(pVal.GetString(), out int pParsed)) power = pParsed;
                            }

                            string quote = varEl.TryGetProperty("quote", out var q) ? q.GetString() ?? "" : "";
                            
                            // Map stats
                            var stats = varEl.GetProperty("stats");
                            int baseAtk = GetIntStat(stats, "base_atk", GetIntStat(stats, "catalog_atk", GetIntStat(stats, "proper_fused_atk")));
                            int baseDef = GetIntStat(stats, "base_def", GetIntStat(stats, "catalog_def", GetIntStat(stats, "proper_fused_def")));
                            
                            // Support fallback mapping keys for fused variant structures
                            int maxAtk = GetIntStat(stats, "max_atk", GetIntStat(stats, "catalog_atk"));
                            int maxDef = GetIntStat(stats, "max_def", GetIntStat(stats, "catalog_def"));
                            
                            int masteryAtk = GetIntStat(stats, "mastery_bonus_atk");
                            int masteryDef = GetIntStat(stats, "mastery_bonus_def");

                            // Image filename mapping (clean special chars to match scraper titles)
                            string safeTitle = new string(title.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
                            string imageFile = $"{safeTitle}_{variantIndex + 1}.jpg";

                            context.CardTemplates.Add(new CardTemplate
                            {
                                Title = title,
                                VisualTitle = cardElement.TryGetProperty("visual_title", out var vt) ? vt.GetString() ?? title : title,
                                Alignment = alignment,
                                Rarity = rarity,
                                Faction = faction,
                                Gender = gender,
                                PowerRequirement = power,
                                BaseAtk = baseAtk,
                                BaseDef = baseDef,
                                MaxAtk = maxAtk,
                                MaxDef = maxDef,
                                MasteryBonusAtk = masteryAtk,
                                MasteryBonusDef = masteryDef,
                                AbilityName = abilityName,
                                AbilityEffect = abilityEffect,
                                Quote = quote,
                                ImageFileName = imageFile,
                                VariantName = variantName
                            });
                            
                            variantIndex++;
                            addedCount++;
                        }
                    }

                    context.SaveChanges();
                    logger.LogInformation($"[Seeder] Successfully imported {addedCount} Card templates into SQLite database.");
                    
                    // Give the default user profile a couple of starter cards to test
                    SeedPlayerStarterInventory(context, logger);
                }
            }
            catch (Exception ex)
            {
                logger.LogError($"[Seeder] Failed to seed cards: {ex.Message}");
                SeedFallbacks(context);
            }
        }

        private static int GetIntStat(JsonElement stats, string key, int fallback = 0)
        {
            if (stats.TryGetProperty(key, out var val))
            {
                if (val.ValueKind == JsonValueKind.Number) return val.GetInt32();
                if (val.ValueKind == JsonValueKind.String && val.GetString() is string str && int.TryParse(str.Replace(",", ""), out int parsed)) return parsed;
            }
            return fallback;
        }

        private static void SeedFallbacks(MwohDbContext context)
        {
            context.CardTemplates.Add(new CardTemplate
            {
                Title = "Spider-Man",
                VisualTitle = "[Great Responsibility] Spider-Man",
                Alignment = "Speed",
                Rarity = "Rare",
                Faction = "Super Hero",
                Gender = "Male",
                PowerRequirement = 12,
                BaseAtk = 2000,
                BaseDef = 1800,
                MaxAtk = 5000,
                MaxDef = 4500,
                AbilityName = "Web-Slinger",
                AbilityEffect = "Strengthen Speed ATK.",
                Quote = "With great power comes great responsibility!",
                ImageFileName = "SpiderMan_1.jpg",
                VariantName = "Base"
            });
            context.CardTemplates.Add(new CardTemplate
            {
                Title = "Iron Man",
                VisualTitle = "[Arc Reactor] Iron Man",
                Alignment = "Tactics",
                Rarity = "Super Rare",
                Faction = "Super Hero",
                Gender = "Male",
                PowerRequirement = 14,
                BaseAtk = 2500,
                BaseDef = 2400,
                MaxAtk = 6000,
                MaxDef = 5800,
                AbilityName = "Uni-Beam",
                AbilityEffect = "Strengthen Tactics ATK.",
                Quote = "I am Iron Man.",
                ImageFileName = "IronMan_1.jpg",
                VariantName = "Base"
            });
            context.CardTemplates.Add(new CardTemplate
            {
                Title = "Captain America",
                VisualTitle = "[Sentinel of Liberty] Captain America",
                Alignment = "Bruiser",
                Rarity = "Rare",
                Faction = "Super Hero",
                Gender = "Male",
                PowerRequirement = 13,
                BaseAtk = 2200,
                BaseDef = 2600,
                MaxAtk = 5200,
                MaxDef = 6200,
                AbilityName = "Shield Toss",
                AbilityEffect = "Strengthen Bruiser DEF.",
                Quote = "I can do this all day.",
                ImageFileName = "CaptainAmerica_1.jpg",
                VariantName = "Base"
            });
            context.CardTemplates.Add(new CardTemplate
            {
                Title = "Black Widow",
                VisualTitle = "[Covert Operative] Black Widow",
                Alignment = "Speed",
                Rarity = "Normal",
                Faction = "Super Hero",
                Gender = "Female",
                PowerRequirement = 8,
                BaseAtk = 1200,
                BaseDef = 1100,
                MaxAtk = 3200,
                MaxDef = 3000,
                AbilityName = "Stinger Blast",
                AbilityEffect = "Slightly Strengthen Speed ATK.",
                Quote = "Nothing lasts forever.",
                ImageFileName = "BlackWidow_1.jpg",
                VariantName = "Base"
            });
            context.CardTemplates.Add(new CardTemplate
            {
                Title = "Hulk",
                VisualTitle = "[Smash Protocol] Hulk",
                Alignment = "Bruiser",
                Rarity = "Legendary",
                Faction = "Super Hero",
                Gender = "Male",
                PowerRequirement = 18,
                BaseAtk = 3500,
                BaseDef = 2800,
                MaxAtk = 8500,
                MaxDef = 6800,
                AbilityName = "Gamma Slam",
                AbilityEffect = "Massively Strengthen Bruiser ATK.",
                Quote = "HULK SMASH!",
                ImageFileName = "Hulk_1.jpg",
                VariantName = "Base"
            });
            context.CardTemplates.Add(new CardTemplate
            {
                Title = "Thor",
                VisualTitle = "[God of Thunder] Thor",
                Alignment = "Tactics",
                Rarity = "Legendary",
                Faction = "Super Hero",
                Gender = "Male",
                PowerRequirement = 17,
                BaseAtk = 3400,
                BaseDef = 3000,
                MaxAtk = 8200,
                MaxDef = 7200,
                AbilityName = "Lightning Strike",
                AbilityEffect = "Massively Strengthen Tactics ATK.",
                Quote = "Bring me Thanos!",
                ImageFileName = "Thor_1.jpg",
                VariantName = "Base"
            });
            context.SaveChanges();
        }

        private static void SeedPlayerStarterInventory(MwohDbContext context, ILogger logger)
        {
            var profile = context.Profiles.FirstOrDefault(p => p.Id == 1);
            if (profile != null && !context.PlayerCards.Any(pc => pc.PlayerProfileId == profile.Id))
            {
                // Assign first 6 templates as owned starter cards
                var templates = context.CardTemplates.Take(6).ToList();
                int index = 0;
                foreach (var temp in templates)
                {
                    bool isLeader = (index == 0);
                    bool inDecks = (index < 5); // Max 5 cards in deck

                    context.PlayerCards.Add(new PlayerCard
                    {
                        PlayerProfileId = profile.Id,
                        CardTemplateId = temp.Id,
                        CurrentLevel = 1,
                        CurrentMastery = 0,
                        CurrentAtk = temp.BaseAtk,
                        CurrentDef = temp.BaseDef,
                        IsLeader = isLeader,
                        IsInAttackDeck = inDecks,
                        IsInDefenseDeck = inDecks
                    });
                    index++;
                }
                context.SaveChanges();
                logger.LogInformation($"[Seeder] Seeded 6 starter cards (5 in active decks, 1 leader) for default profile '{profile.Nickname}'.");
            }
        }

        public static void SeedItems(MwohDbContext context, ILogger logger)
        {
            if (context.ItemTemplates.Any())
            {
                logger.LogInformation("[Seeder] Item templates already seeded. Skipping seeder.");
                return;
            }

            string jsonPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Tools", "Scraper", "items_db.json");
            
            if (!File.Exists(jsonPath))
            {
                jsonPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "Tools", "Scraper", "items_db.json");
            }

            if (!File.Exists(jsonPath))
            {
                logger.LogWarning($"[Seeder] JSON items database not found at '{jsonPath}'. Seeding default fallback items.");
                SeedDefaultFallbackItems(context);
                return;
            }

            try
            {
                string jsonText = File.ReadAllText(jsonPath);
                using (JsonDocument doc = JsonDocument.Parse(jsonText))
                {
                    int addedCount = 0;
                    var profile = context.Profiles.FirstOrDefault(p => p.Id == 1);

                    foreach (var itemElement in doc.RootElement.EnumerateArray())
                    {
                        string name = itemElement.GetProperty("name").GetString() ?? "";
                        string description = itemElement.GetProperty("description").GetString() ?? "";
                        string type = itemElement.GetProperty("type").GetString() ?? "General";
                        int effectValue = itemElement.GetProperty("effect_value").GetInt32();
                        string imageUrl = itemElement.GetProperty("image_url").GetString() ?? "";
                        
                        string imageFileName = "";
                        if (!string.IsNullOrEmpty(imageUrl))
                        {
                            try
                            {
                                Uri uri = new Uri(imageUrl);
                                imageFileName = Path.GetFileName(uri.LocalPath);
                            }
                            catch
                            {
                                imageFileName = $"{name.Replace(" ", "_")}.jpg";
                            }
                        }

                        var itemTemplate = new ItemTemplate
                        {
                            Name = name,
                            Description = description,
                            Type = type,
                            EffectValue = effectValue,
                            ImageFileName = imageFileName
                        };

                        context.ItemTemplates.Add(itemTemplate);
                        addedCount++;
                    }

                    context.SaveChanges();
                    logger.LogInformation($"[Seeder] Successfully imported {addedCount} Item templates into SQLite database from '{jsonPath}'.");

                    // Give default profile (Id = 1) some quantities of all items
                    if (profile != null && !context.PlayerInventoryItems.Any(pi => pi.PlayerProfileId == profile.Id))
                    {
                        var items = context.ItemTemplates.ToList();
                        foreach (var item in items)
                        {
                            context.PlayerInventoryItems.Add(new PlayerInventoryItem
                            {
                                PlayerProfileId = profile.Id,
                                ItemTemplateId = item.Id,
                                Quantity = 50
                            });
                        }
                        context.SaveChanges();
                        logger.LogInformation($"[Seeder] Linked default inventory (50x of each item) to profile '{profile.Nickname}'.");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError($"[Seeder] Failed to seed items from JSON: {ex.Message}");
                SeedDefaultFallbackItems(context);
            }
        }

        private static void SeedDefaultFallbackItems(MwohDbContext context)
        {
            var defaultItems = new[]
            {
                new ItemTemplate { Name = "Energy Iso-8", Description = "Fully restores Energy.", Type = "EnergyRestorative", EffectValue = 100, ImageFileName = "item_energy_full.png" },
                new ItemTemplate { Name = "Energy Iso-8 (Half)", Description = "Restores 50% Energy.", Type = "EnergyRestorative", EffectValue = 50, ImageFileName = "item_energy_half.png" },
                new ItemTemplate { Name = "Attack Iso-8", Description = "Fully restores Attack Power.", Type = "AttackPowerRestorative", EffectValue = 100, ImageFileName = "item_attack_full.png" },
                new ItemTemplate { Name = "Attack Iso-8 (Half)", Description = "Restores 50% Attack Power.", Type = "AttackPowerRestorative", EffectValue = 50, ImageFileName = "item_attack_half.png" },
                new ItemTemplate { Name = "Defense Iso-8", Description = "Fully restores Defense Power.", Type = "DefensePowerRestorative", EffectValue = 100, ImageFileName = "item_defense_full.png" },
                new ItemTemplate { Name = "Defense Iso-8 (Half)", Description = "Restores 50% Defense Power.", Type = "DefensePowerRestorative", EffectValue = 50, ImageFileName = "item_defense_half.png" },
                new ItemTemplate { Name = "Mastery Iso-8", Description = "Increases card mastery by 10 points.", Type = "MasteryIso8", EffectValue = 10, ImageFileName = "item_mastery.png" }
            };

            foreach (var item in defaultItems)
            {
                context.ItemTemplates.Add(item);
            }
            context.SaveChanges();

            var profile = context.Profiles.FirstOrDefault(p => p.Id == 1);
            if (profile != null)
            {
                foreach (var item in context.ItemTemplates.ToList())
                {
                    context.PlayerInventoryItems.Add(new PlayerInventoryItem
                    {
                        PlayerProfileId = profile.Id,
                        ItemTemplateId = item.Id,
                        Quantity = 50
                    });
                }
                context.SaveChanges();
            }
        }

        private static readonly Dictionary<int, string> OperationTitles = new()
        {
            { 1, "Operation 1: Trouble in Mid-Town" },
            { 2, "Operation 2: HYDRA Hijinks" },
            { 3, "Operation 3: The Doctor's Revenge" },
            { 4, "Operation 4: Mean Streets" },
            { 5, "Operation 5: Buckets of Bullets" },
            { 6, "Operation 6: Mind of MODOK" },
            { 7, "Operation 7: Aiming Too High" },
            { 8, "Operation 8: Baron's Gambit" },
            { 9, "Operation 9: Might and Fury" },
            { 10, "Operation 10: Hunters" },
            { 11, "Operation 11: Vanity Vanquished" },
            { 12, "Operation 12: Day Walker" },
            { 13, "Operation 13: Put a Stake in it" },
            { 14, "Operation 14: The Break In" },
            { 15, "Operation 15: A Wider Conspiracy" },
            { 16, "Operation 16: Caged Fury!" },
            { 17, "Operation 17: My Fist... Your FACE!" },
            { 18, "Operation 18: Sentinel Search-and-Destroy" },
            { 19, "Operation 19: Scientific Mystique" },
            { 20, "Operation 20: Day at the Zoo" },
            { 21, "Operation 21: Taking AIM" },
            { 22, "Operation 22: All An Illusion" },
            { 23, "Operation 23: Tunnel Vision" },
            { 24, "Operation 24: Vampire Jailbreak" },
            { 25, "Operation 25: Lesson Learned" },
            { 26, "Operation 26: Crime and..." },
            { 27, "Operation 27: ...Punishment" },
            { 28, "Operation 28: Relics of Genosha I" },
            { 29, "Operation 29: Relics of Genosha II" }
        };

        public static void DownloadOperationBanners(ILogger logger)
        {
            logger.LogInformation("[Downloader] Starting background banner downloader...");
            string outputDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "operations");
            try
            {
                if (!Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }
            }
            catch (Exception ex)
            {
                logger.LogError($"[Downloader] Failed to create operations image directory: {ex.Message}");
                return;
            }

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");

            foreach (var kvp in OperationTitles)
            {
                int opId = kvp.Key;
                string title = kvp.Value;
                string filePath = Path.Combine(outputDir, $"operation_{opId}.jpg");

                if (File.Exists(filePath))
                {
                    continue;
                }

                logger.LogInformation($"[Downloader] Fetching banner image metadata for Operation {opId}...");
                try
                {
                    var parseUrl = $"https://marvel-war-of-heroes.fandom.com/api.php?action=parse&page={Uri.EscapeDataString(title)}&format=json";
                    var parseResponse = httpClient.GetStringAsync(parseUrl).GetAwaiter().GetResult();
                    using var doc = JsonDocument.Parse(parseResponse);
                    
                    if (doc.RootElement.TryGetProperty("parse", out var parseObj) && parseObj.TryGetProperty("images", out var imagesArr))
                    {
                        string? selectedImageName = null;
                        foreach (var imgElement in imagesArr.EnumerateArray())
                        {
                            string img = imgElement.GetString() ?? "";
                            if (img.Contains("operation", StringComparison.OrdinalIgnoreCase))
                            {
                                selectedImageName = img;
                                break;
                            }
                        }

                        if (selectedImageName == null)
                        {
                            foreach (var imgElement in imagesArr.EnumerateArray())
                            {
                                string img = imgElement.GetString() ?? "";
                                if (img.StartsWith("op", StringComparison.OrdinalIgnoreCase))
                                {
                                    selectedImageName = img;
                                    break;
                                }
                            }
                        }

                        if (selectedImageName == null && imagesArr.GetArrayLength() > 0)
                        {
                            selectedImageName = imagesArr[0].GetString();
                        }

                        if (!string.IsNullOrEmpty(selectedImageName))
                        {
                            var queryUrl = $"https://marvel-war-of-heroes.fandom.com/api.php?action=query&titles=File:{Uri.EscapeDataString(selectedImageName)}&prop=imageinfo&iiprop=url&format=json";
                            var queryResponse = httpClient.GetStringAsync(queryUrl).GetAwaiter().GetResult();
                            using var queryDoc = JsonDocument.Parse(queryResponse);
                            if (queryDoc.RootElement.TryGetProperty("query", out var queryObj) && queryObj.TryGetProperty("pages", out var pagesObj))
                            {
                                string? directUrl = null;
                                foreach (var pageProp in pagesObj.EnumerateObject())
                                {
                                    if (pageProp.Value.TryGetProperty("imageinfo", out var imageInfoArr) && imageInfoArr.GetArrayLength() > 0)
                                    {
                                        directUrl = imageInfoArr[0].GetProperty("url").GetString();
                                        break;
                                    }
                                }

                                if (!string.IsNullOrEmpty(directUrl))
                                {
                                    logger.LogInformation($"[Downloader] Downloading Operation {opId} banner from Fandom: {directUrl}");
                                    var imageBytes = httpClient.GetByteArrayAsync(directUrl).GetAwaiter().GetResult();
                                    File.WriteAllBytes(filePath, imageBytes);
                                    logger.LogInformation($"[Downloader] Successfully cached Operation {opId} banner.");
                                }
                                else
                                {
                                    logger.LogWarning($"[Downloader] Could not resolve direct download URL for {selectedImageName}.");
                                }
                            }
                        }
                        else
                        {
                            logger.LogWarning($"[Downloader] No banner image found for Operation {opId}.");
                        }
                    }
                    else
                    {
                        logger.LogWarning($"[Downloader] Parse node not found in Fandom API response for Operation {opId}.");
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError($"[Downloader] Error downloading banner for Operation {opId}: {ex.Message}");
                }
            }
            logger.LogInformation("[Downloader] Background banner download task completed successfully.");
        }
    }
}
