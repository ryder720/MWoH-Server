using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using MwohServer.Data;
using MwohServer.Models;
using MwohServer.Services;

namespace MwohServer.Tests
{
    public static class ResourceVaultTests
    {
        public static bool Run(IResourceVaultEngine engine, MwohDbContext context)
        {
            Console.WriteLine("\n==================================================");
            Console.WriteLine("🧪 INITIALIZING S.H.I.E.L.D. RESOURCE VAULT TESTS");
            Console.WriteLine("==================================================");

            int passed = 0;
            int failed = 0;

            void AssertEquals(string testName, long expected, long actual)
            {
                if (expected == actual)
                {
                    passed++;
                    Console.WriteLine($"  ✅ [PASS] {testName} (Expected: {expected}, Actual: {actual})");
                }
                else
                {
                    failed++;
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"  ❌ [FAIL] {testName} (Expected: {expected}, Actual: {actual})");
                    Console.ResetColor();
                }
            }

            void AssertTrue(string testName, bool condition)
            {
                if (condition)
                {
                    passed++;
                    Console.WriteLine($"  ✅ [PASS] {testName}");
                }
                else
                {
                    failed++;
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"  ❌ [FAIL] {testName}");
                    Console.ResetColor();
                }
            }

            try
            {
                // Setup test profile
                var user = new UserAccount { Username = "vault_test_agent", PasswordHash = "hash" };
                context.Users.Add(user);
                context.SaveChanges();

                var profile = new PlayerProfile
                {
                    UserAccountId = user.Id,
                    Nickname = "VaultAgent",
                    Level = 1,
                    SilverBalance = 10000,
                    MaxCardCapacity = 10,
                    ResourceRedemptionsJson = "{}"
                };
                context.Profiles.Add(profile);
                context.SaveChanges();

                // Query templates from database (ItemTemplates & CardTemplates)
                // We need the 6 Storm's Cape resource item templates
                var itemTemplates = context.ItemTemplates
                    .Where(t => t.Type == "Resource" && t.Name.Contains("Storm's") && t.Name.Contains("Cape"))
                    .ToList();

                if (itemTemplates.Count < 6)
                {
                    string[] colors = { "Red", "Blue", "Green", "Yellow", "Purple", "Emerald" };
                    foreach (var color in colors)
                    {
                        var name = $"Storm's {color} Cape";
                        var temp = context.ItemTemplates.FirstOrDefault(t => t.Name == name);
                        if (temp == null)
                        {
                            temp = new ItemTemplate
                            {
                                Name = name,
                                Description = "S.H.I.E.L.D. Tactical Resource item collected from operations.",
                                Type = "Resource",
                                EffectValue = 2000,
                                ImageFileName = $"Storms_{color}_Cape.jpg"
                            };
                            context.ItemTemplates.Add(temp);
                        }
                    }
                    context.SaveChanges();

                    itemTemplates = context.ItemTemplates
                        .Where(t => t.Type == "Resource" && t.Name.Contains("Storm's") && t.Name.Contains("Cape"))
                        .ToList();
                }

                // Get standard LevelUpSerum template
                var serumTemplate = context.ItemTemplates.FirstOrDefault(t => t.Type == "LevelUpSerum");
                if (serumTemplate == null)
                {
                    serumTemplate = new ItemTemplate
                    {
                        Name = "Level Up ISO-8 Serum",
                        Description = "ISO-8 serum.",
                        Type = "LevelUpSerum",
                        EffectValue = 3,
                        ImageFileName = "LevelUpISO8Serum.jpg"
                    };
                    context.ItemTemplates.Add(serumTemplate);
                    context.SaveChanges();
                }

                // Add or retrieve card template Queen of Lightning Storm as reward
                var cardTemplate = context.CardTemplates.FirstOrDefault(t => t.Title == "Queen of Lightning Storm");
                if (cardTemplate == null)
                {
                    cardTemplate = new CardTemplate
                    {
                        Title = "Queen of Lightning Storm",
                        VisualTitle = "Queen_Storm",
                        Alignment = "Speed",
                        Rarity = "Rare",
                        PowerRequirement = 10,
                        BaseAtk = 2000,
                        BaseDef = 1800,
                        MaxAtk = 5000,
                        MaxDef = 4500
                    };
                    context.CardTemplates.Add(cardTemplate);
                    context.SaveChanges();
                }

                // --------------------------------------------------
                // 1. Set Incomplete Check
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 1: Incomplete Set Validation ---");
                // Seed only 5 items in inventory
                for (int i = 0; i < 5; i++)
                {
                    var pItem = new PlayerInventoryItem
                    {
                        PlayerProfileId = profile.Id,
                        ItemTemplateId = itemTemplates[i].Id,
                        Quantity = 1
                    };
                    context.PlayerInventoryItems.Add(pItem);
                }
                context.SaveChanges();

                var r1 = engine.Redeem(profile.Id, "StormsCape");
                AssertTrue("Redeem fails when set is incomplete", !r1.Success);
                AssertTrue("Correct set incomplete message", r1.Message.Contains("SET INCOMPLETE"));

                // --------------------------------------------------
                // 2. Set 1: Hero Card Reward
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 2: Set 1 Hero Card Reward ---");
                // Add the 6th item to complete the set
                var pItem6 = new PlayerInventoryItem
                {
                    PlayerProfileId = profile.Id,
                    ItemTemplateId = itemTemplates[5].Id,
                    Quantity = 1
                };
                context.PlayerInventoryItems.Add(pItem6);
                context.SaveChanges();

                var r2 = engine.Redeem(profile.Id, "StormsCape");
                AssertTrue("Redeem completes successfully for 1st complete set", r2.Success);
                AssertTrue("Awards Hero Card", r2.Message.Contains("Queen") && r2.Message.Contains("Storm"));
                
                // Assert quantities are decremented
                var currentItemsCount = context.PlayerInventoryItems
                    .Where(pi => pi.PlayerProfileId == profile.Id && itemTemplates.Select(t => t.Id).Contains(pi.ItemTemplateId))
                    .Sum(pi => pi.Quantity);
                AssertEquals("Resource items decremented to 0", 0, currentItemsCount);

                // Assert redemption dictionary was updated
                AssertTrue("Redemption count updated to 1", r2.Redemptions.ContainsKey("StormsCape") && r2.Redemptions["StormsCape"] == 1);

                // Assert card was added
                var cardCount = context.PlayerCards.Count(pc => pc.PlayerProfileId == profile.Id);
                AssertEquals("1 Card added to player cards roster", 1, cardCount);

                // --------------------------------------------------
                // 3. Set 2: Restorative Item Reward
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 3: Set 2 ISO-8 Restorative Reward ---");
                // Add 6 more items to inventory to make a second set
                foreach (var temp in itemTemplates)
                {
                    var pItem = context.PlayerInventoryItems.FirstOrDefault(pi => pi.PlayerProfileId == profile.Id && pi.ItemTemplateId == temp.Id);
                    if (pItem != null)
                    {
                        pItem.Quantity = 1;
                    }
                    else
                    {
                        context.PlayerInventoryItems.Add(new PlayerInventoryItem { PlayerProfileId = profile.Id, ItemTemplateId = temp.Id, Quantity = 1 });
                    }
                }
                context.SaveChanges();

                var r3 = engine.Redeem(profile.Id, "StormsCape");
                AssertTrue("Redeem completes successfully for 2nd set", r3.Success);
                AssertTrue("Awards Level-Up ISO-8 Serums", r3.Message.Contains("ISO-8 Serums") || r3.Message.Contains("ISO-8"));

                // Verify 3 serums added to player inventory
                var serumItem = context.PlayerInventoryItems.FirstOrDefault(pi => pi.PlayerProfileId == profile.Id && pi.ItemTemplateId == serumTemplate.Id);
                AssertTrue("Level-Up Serums awarded", serumItem != null && serumItem.Quantity == 3);

                // --------------------------------------------------
                // 4. Set 3 & Max Capacity Safeguards
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 4: Set 3 & Max capacity safeguards ---");
                // 3rd complete set
                foreach (var temp in itemTemplates)
                {
                    var pItem = context.PlayerInventoryItems.FirstOrDefault(pi => pi.PlayerProfileId == profile.Id && pi.ItemTemplateId == temp.Id);
                    if (pItem != null) pItem.Quantity = 1;
                }
                context.SaveChanges();

                var r4 = engine.Redeem(profile.Id, "StormsCape");
                AssertTrue("Redeem completes successfully for 3rd set", r4.Success);

                // Try 4th redemption (Blocks max redemptions cap)
                foreach (var temp in itemTemplates)
                {
                    var pItem = context.PlayerInventoryItems.FirstOrDefault(pi => pi.PlayerProfileId == profile.Id && pi.ItemTemplateId == temp.Id);
                    if (pItem != null) pItem.Quantity = 1;
                }
                context.SaveChanges();

                var r5 = engine.Redeem(profile.Id, "StormsCape");
                AssertTrue("Redeem fails on 4th attempt (exceeds cap limit of 3)", !r5.Success);
                AssertTrue("Correct cap reached error message", r5.Message.Contains("MAXIMUM REDEMPTIONS REACHED"));

                // --------------------------------------------------
                // 5. Excess Resource Donations
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 5: Excess Resource Donations ---");
                // Add resources for donation (3 of each colors)
                foreach (var temp in itemTemplates)
                {
                    var pItem = context.PlayerInventoryItems.FirstOrDefault(pi => pi.PlayerProfileId == profile.Id && pi.ItemTemplateId == temp.Id);
                    if (pItem != null) pItem.Quantity = 3;
                }
                context.SaveChanges();

                var initialSilver = profile.SilverBalance; // should be 10000
                var r6 = engine.Donate(profile.Id, "StormsCape");
                
                AssertTrue("Donation completed successfully", r6.Success);
                // Silver gained: 6 color categories * 3 qty * 2000 value = +36000 Silver
                AssertEquals("Silver balance incremented (+36,000)", initialSilver + 36000, profile.SilverBalance);

                // Quantities reset to 0
                var donatedItemsCount = context.PlayerInventoryItems
                    .Where(pi => pi.PlayerProfileId == profile.Id && itemTemplates.Select(t => t.Id).Contains(pi.ItemTemplateId))
                    .Sum(pi => pi.Quantity);
                AssertEquals("Resource item quantities reset to 0", 0, donatedItemsCount);

                // Clean up database test entries
                var userRelations = context.PlayerInventoryItems.Where(pi => pi.PlayerProfileId == profile.Id).ToList();
                var userCards = context.PlayerCards.Where(pc => pc.PlayerProfileId == profile.Id).ToList();
                context.PlayerInventoryItems.RemoveRange(userRelations);
                context.PlayerCards.RemoveRange(userCards);
                context.Profiles.Remove(profile);
                context.Users.Remove(user);
                context.SaveChanges();
            }
            catch (Exception ex)
            {
                failed++;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n❌ EXCEPTION THROWN DURING TEST EXECUTION: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                Console.ResetColor();
            }

            Console.WriteLine("\n==================================================");
            Console.WriteLine($"🏁 TEST RUN COMPLETED // PASSED: {passed} | FAILED: {failed}");
            Console.WriteLine("==================================================");

            return failed == 0;
        }
    }
}
