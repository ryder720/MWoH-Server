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

                            // Dynamic healing of missing stats
                            if (baseAtk == 0 && maxAtk == 0)
                            {
                                int rarityMultiplier = rarity switch
                                {
                                    "Ultimate Legendary" => 280,
                                    "Legendary" => 250,
                                    "Super Rare" => 180,
                                    "Rare" => 140,
                                    "High Normal" => 100,
                                    _ => 80 // Normal
                                };
                                baseAtk = power * rarityMultiplier;
                                baseDef = (int)(baseAtk * 0.9);
                                maxAtk = baseAtk * 3;
                                maxDef = baseDef * 3;
                            }
                            else if (baseAtk == 0 && maxAtk > 0)
                            {
                                baseAtk = (int)(maxAtk * 0.35);
                                baseDef = (int)(maxDef * 0.35);
                            }
                            else if (maxAtk == 0 && baseAtk > 0)
                            {
                                maxAtk = baseAtk * 3;
                                maxDef = baseDef * 3;
                            }

                            // Propagate mastery bonus stats if they are zero
                            if (masteryAtk == 0) masteryAtk = (int)(maxAtk * 0.12);
                            if (masteryDef == 0) masteryDef = (int)(maxDef * 0.12);

                            int maxMastery = 100;
                            if (varEl.TryGetProperty("max_mastery", out var maxMProp))
                            {
                                if (maxMProp.ValueKind == JsonValueKind.Number) maxMastery = maxMProp.GetInt32();
                                else if (maxMProp.ValueKind == JsonValueKind.String && maxMProp.GetString() is string str && int.TryParse(str, out int parsed)) maxMastery = parsed;
                            }
                            if (maxMastery <= 0) maxMastery = 100;

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
                                MaxMastery = maxMastery,
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
                MasteryBonusAtk = 600,
                MasteryBonusDef = 540,
                MaxMastery = 40,
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
                MasteryBonusAtk = 800,
                MasteryBonusDef = 760,
                MaxMastery = 45,
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
                MasteryBonusAtk = 660,
                MasteryBonusDef = 780,
                MaxMastery = 40,
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
                MasteryBonusAtk = 400,
                MasteryBonusDef = 360,
                MaxMastery = 30,
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
                MasteryBonusAtk = 1200,
                MasteryBonusDef = 900,
                MaxMastery = 50,
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
                MasteryBonusAtk = 1150,
                MasteryBonusDef = 1000,
                MaxMastery = 50,
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

                    var card = new PlayerCard
                    {
                        PlayerProfileId = profile.Id,
                        IsLeader = isLeader,
                        IsInAttackDeck = inDecks,
                        IsInDefenseDeck = inDecks
                    };
                    card.InitializeStats(temp, GameplaySettings.DefaultMasteryPercentage);
                    context.PlayerCards.Add(card);
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
                        string name = (itemElement.GetProperty("name").GetString() ?? "").Replace("[]", "").Trim();
                        string description = itemElement.GetProperty("description").GetString() ?? "";
                        string type = itemElement.GetProperty("type").GetString() ?? "General";
                        int effectValue = itemElement.GetProperty("effect_value").GetInt32();

                        // Override scraped classifications to enable interactive item features
                        if (name.Contains("Level Up ISO-8 Serum", StringComparison.OrdinalIgnoreCase))
                        {
                            type = "LevelUpSerum";
                            effectValue = name.Contains("Super", StringComparison.OrdinalIgnoreCase) ? 10 : 3;
                        }
                        else if (name.Equals("Mastery Iso-8", StringComparison.OrdinalIgnoreCase))
                        {
                            type = "MasteryIso8";
                            effectValue = 10;
                        }
                        else if (name.Equals("Cosmic Canister", StringComparison.OrdinalIgnoreCase))
                        {
                            type = "MasteryIso8";
                            effectValue = 1;
                        }
                        else if (name.Contains("Card Stock", StringComparison.OrdinalIgnoreCase))
                        {
                            type = "InventoryExpansion";
                            effectValue = 5;
                        }
                        else if (name.Contains("Ticket", StringComparison.OrdinalIgnoreCase))
                        {
                            type = "GachaTicket";
                            effectValue = 0;
                        }

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

                    // Programmatically seed missing specialized tickets from the polish todo checklist
                    var targetTickets = new List<(string Name, string Description)>
                    {
                        ("Gacha Ticket", "Can be exchanged for a standard Gacha pull from S.H.I.E.L.D. archives."),
                        ("Half-Anniversary Ticket", "A rare commendation ticket celebrating the S.H.I.E.L.D. Half-Anniversary. Guarantees Rare+ recruitments."),
                        ("Super Hero Pack Ticket", "A high-priority ticket that recruits a random Super Hero faction combatant."),
                        ("Bruiser Ticket", "Recruits a random combat asset with Bruiser alignment."),
                        ("Tactics Ticket", "Recruits a random combat asset with Tactics alignment."),
                        ("Speed Ticket", "Recruits a random combat asset with Speed alignment.")
                    };

                    foreach (var ticket in targetTickets)
                    {
                        if (!context.ItemTemplates.Any(t => t.Name == ticket.Name))
                        {
                            var imgFile = $"{ticket.Name.Replace(" ", "")}.jpg";
                            context.ItemTemplates.Add(new ItemTemplate
                            {
                                Name = ticket.Name,
                                Description = ticket.Description,
                                Type = "GachaTicket",
                                EffectValue = 0,
                                ImageFileName = imgFile
                            });
                            addedCount++;
                        }
                    }

                    context.SaveChanges();
                    logger.LogInformation($"[Seeder] Successfully imported {addedCount} Item templates into SQLite database (scraped and programmatically resolved).");

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

            SeedTacticalResources(context, logger);
        }

        private static void SeedTacticalResources(MwohDbContext context, ILogger logger)
        {
            if (context.ItemTemplates.Any(x => x.Type == "Resource"))
            {
                logger.LogInformation("[Seeder] Tactical resources already seeded.");
                return;
            }

            logger.LogInformation("[Seeder] Seeding S.H.I.E.L.D. Tactical Resources...");

            var resourceGroups = new[]
            {
                new { GroupName = "Storm's Cape", BaseName = "Storm's Cape", Colors = new[] { "Red", "Blue", "Green", "Yellow", "Purple", "Emerald" }, Donation = 2000 },
                new { GroupName = "Suitcase", BaseName = "Suitcase", Colors = new[] { "Red", "Blue", "Green", "Yellow", "Purple", "Emerald" }, Donation = 2500 },
                new { GroupName = "Sword of Proficiency", BaseName = "Sword of Proficiency", Colors = new[] { "Red", "Blue", "Green", "Yellow", "Purple", "Emerald" }, Donation = 3000 },
                new { GroupName = "Assassin's Choker", BaseName = "Assassin's Choker", Colors = new[] { "Red", "Blue", "Green", "Yellow", "Purple", "Emerald" }, Donation = 3500 },
                new { GroupName = "Chain Belt", BaseName = "Chain Belt", Colors = new[] { "Red", "Blue", "Green", "Yellow", "Purple", "Cyan" }, Donation = 4000 },
                new { GroupName = "Geirr", BaseName = "Geirr", Colors = new[] { "Crimson", "Cobalt", "Emerald", "Amber", "Violet", "Aqua" }, Donation = 4500 },
                new { GroupName = "Projectile Array", BaseName = "Projectile Array", Colors = new[] { "Red", "Blue", "Green", "Yellow", "Violet", "Aqua" }, Donation = 5000 }
            };

            int resourceAddedCount = 0;
            foreach (var group in resourceGroups)
            {
                foreach (var color in group.Colors)
                {
                    string name;
                    string imgFile;
                    if (group.BaseName == "Storm's Cape")
                    {
                        name = $"Storm's {color} Cape";
                        imgFile = $"Storms_{color}_Cape.jpg";
                    }
                    else if (group.BaseName == "Assassin's Choker")
                    {
                        name = $"Assassin's {color} Choker";
                        imgFile = $"Assassins_{color}_Choker.jpg";
                    }
                    else if (group.BaseName == "Geirr")
                    {
                        name = $"{color} Geirr";
                        imgFile = $"{color}_Geirr.jpg";
                    }
                    else
                    {
                        name = $"{color} {group.BaseName}";
                        imgFile = $"{color}_{group.BaseName.Replace(" ", "_")}.jpg";
                    }

                    var temp = new ItemTemplate
                    {
                        Name = name,
                        Description = $"S.H.I.E.L.D. Tactical Resource item collected from operations. Collect a complete set of six colors to redeem premium card awards.",
                        Type = "Resource",
                        EffectValue = group.Donation,
                        ImageFileName = imgFile
                    };

                    context.ItemTemplates.Add(temp);
                    resourceAddedCount++;
                }
            }

            context.SaveChanges();
            logger.LogInformation($"[Seeder] Successfully seeded {resourceAddedCount} Tactical Resource templates into SQLite database.");

            // Give the default profile (Id = 1) some starting resources to play with
            var profile = context.Profiles.FirstOrDefault(p => p.Id == 1);
            if (profile != null)
            {
                var resourceItems = context.ItemTemplates.Where(x => x.Type == "Resource").ToList();
                foreach (var item in resourceItems)
                {
                    if (!context.PlayerInventoryItems.Any(pi => pi.PlayerProfileId == profile.Id && pi.ItemTemplateId == item.Id))
                    {
                        context.PlayerInventoryItems.Add(new PlayerInventoryItem
                        {
                            PlayerProfileId = profile.Id,
                            ItemTemplateId = item.Id,
                            Quantity = 2
                        });
                    }
                }
                context.SaveChanges();
                logger.LogInformation($"[Seeder] Seeded 2 of each Tactical Resource in starter inventory for default profile '{profile.Nickname}'.");
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

        public static void SeedRivals(MwohDbContext context, ILogger logger)
        {
            if (context.Users.Any(u => u.Username == "coulson"))
            {
                logger.LogInformation("[Seeder] S.H.I.E.L.D. Rivals already seeded.");
                return;
            }

            logger.LogInformation("[Seeder] Seeding S.H.I.E.L.D. Rival Agents...");

            var cardTemplates = context.CardTemplates.ToList();
            if (!cardTemplates.Any())
            {
                logger.LogWarning("[Seeder] No card templates found. Cannot seed rivals.");
                return;
            }

            var rivalsData = new List<(string Username, string Nickname, int Level, int AP, int DP, long Silver, string MainCardTitle)>
            {
                ("coulson", "Agent Coulson", 45, 120, 120, 45000, "Spider-Man"),
                ("may", "Agent May", 55, 140, 140, 60000, "Secret Agent Black Widow"),
                ("fury", "Director Fury", 99, 250, 250, 200000, "Thor"),
                ("hill", "Agent Hill", 60, 150, 150, 75000, "Iron Man"),
                ("widow", "Agent Romanoff", 75, 180, 180, 110000, "Captain America")
            };

            int nextUserId = context.Users.Any() ? context.Users.Max(u => u.Id) + 1 : 1;
            int nextProfileId = context.Profiles.Any() ? context.Profiles.Max(p => p.Id) + 1 : 1;

            foreach (var r in rivalsData)
            {
                var user = new UserAccount
                {
                    Id = nextUserId++,
                    Username = r.Username,
                    PasswordHash = "shield_secure_pwd",
                    CreatedAt = DateTime.UtcNow,
                    ActiveToken = $"token_{r.Username}"
                };

                var profile = new PlayerProfile
                {
                    Id = nextProfileId++,
                    UserAccountId = user.Id,
                    Nickname = r.Nickname,
                    Level = r.Level,
                    Experience = r.Level * 1000,
                    AttackPower = r.AP,
                    AttackPowerCurrent = r.AP,
                    DefensePower = r.DP,
                    DefensePowerCurrent = r.DP,
                    MobaCoinBalance = 10000,
                    SilverBalance = r.Silver,
                    PlayerIdString = (100000 + nextProfileId).ToString(),
                    SessionId = $"session_{r.Username}",
                    StatPoints = 0,
                    LastEnergyRecoveryTime = DateTime.UtcNow,
                    LastBattlePowerRecoveryTime = DateTime.UtcNow
                };

                context.Users.Add(user);
                context.Profiles.Add(profile);

                var selectedTemplates = new List<CardTemplate>();
                var mainTemp = cardTemplates.FirstOrDefault(t => t.Title.Equals(r.MainCardTitle, StringComparison.OrdinalIgnoreCase)) 
                            ?? cardTemplates.FirstOrDefault();
                if (mainTemp != null) selectedTemplates.Add(mainTemp);

                var remaining = cardTemplates.Where(t => mainTemp == null || t.Id != mainTemp.Id).OrderBy(t => Guid.NewGuid()).Take(4).ToList();
                selectedTemplates.AddRange(remaining);

                int cardIdx = 0;
                foreach (var temp in selectedTemplates)
                {
                    var pc = new PlayerCard
                    {
                        PlayerProfileId = profile.Id,
                        IsLeader = (cardIdx == 0),
                        IsInAttackDeck = true,
                        IsInDefenseDeck = true,
                        CurrentLevel = Math.Max(1, r.Level - 10),
                        AbilityLevel = 1
                    };
                    pc.InitializeStats(temp, GameplaySettings.DefaultMasteryPercentage);
                    context.PlayerCards.Add(pc);
                    cardIdx++;
                }

                logger.LogInformation($"[Seeder] Seeded Agent '{r.Nickname}' with {selectedTemplates.Count} cards in active squad.");
            }

            context.SaveChanges();
            logger.LogInformation("[Seeder] Finished seeding S.H.I.E.L.D. Rivals.");
        }

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

