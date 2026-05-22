using System;
using System.IO;
using System.Linq;
using System.Text.Json;
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
                            int baseAtk = GetIntStat(stats, "base_atk");
                            int baseDef = GetIntStat(stats, "base_def");
                            
                            // Support fallback mapping keys for fused variant structures
                            int maxAtk = GetIntStat(stats, "max_atk", GetIntStat(stats, "catalog_atk"));
                            int maxDef = GetIntStat(stats, "max_def", GetIntStat(stats, "catalog_def"));
                            
                            int masteryAtk = GetIntStat(stats, "mastery_bonus_atk");
                            int masteryDef = GetIntStat(stats, "mastery_bonus_def");

                            // Image filename mapping (clean special chars to match scraper titles)
                            string safeTitle = new string(title.Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray()).Replace(' ', '_');
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
            context.SaveChanges();
        }

        private static void SeedPlayerStarterInventory(MwohDbContext context, ILogger logger)
        {
            var profile = context.Profiles.FirstOrDefault(p => p.Id == 1);
            if (profile != null && !context.PlayerCards.Any(pc => pc.PlayerProfileId == profile.Id))
            {
                // Assign first 2 templates as owned starter cards
                var templates = context.CardTemplates.Take(2).ToList();
                foreach (var temp in templates)
                {
                    context.PlayerCards.Add(new PlayerCard
                    {
                        PlayerProfileId = profile.Id,
                        CardTemplateId = temp.Id,
                        CurrentLevel = 1,
                        CurrentMastery = 0,
                        CurrentAtk = temp.BaseAtk,
                        CurrentDef = temp.BaseDef
                    });
                }
                context.SaveChanges();
                logger.LogInformation($"[Seeder] Seeded starter cards for default profile '{profile.Nickname}'.");
            }
        }
    }
}
