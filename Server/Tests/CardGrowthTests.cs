using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using MwohServer.Data;
using MwohServer.Models;
using MwohServer.Services;

namespace MwohServer.Tests
{
    public static class CardGrowthTests
    {
        public static bool Run(ICardGrowthEngine growthEngine, IItemLedger itemLedger, MwohDbContext context)
        {
            Console.WriteLine("\n==================================================");
            Console.WriteLine("🧪 INITIALIZING S.H.I.E.L.D. CARD GROWTH TESTS");
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
                var user = new UserAccount { Username = "growth_test_agent", PasswordHash = "hash" };
                context.Users.Add(user);
                context.SaveChanges();

                var profile = new PlayerProfile
                {
                    UserAccountId = user.Id,
                    Nickname = "GrowthAgent",
                    Level = 1,
                    StatPoints = 0,
                    SilverBalance = 10000,
                    MobaCoinBalance = 1000,
                    EnergyMax = 100,
                    EnergyCurrent = 100,
                    AttackPower = 10,
                    DefensePower = 10
                };
                context.Profiles.Add(profile);
                context.SaveChanges();

                // Setup test card templates
                var cardTemplateNormal = new CardTemplate
                {
                    Title = "Growth Test Normal Hero",
                    VisualTitle = "NormalHero",
                    Alignment = "Speed",
                    Rarity = "Normal",
                    PowerRequirement = 10,
                    BaseAtk = 1000,
                    BaseDef = 1000,
                    MaxAtk = 3000,
                    MaxDef = 3000,
                    MaxMastery = 100
                };
                var cardTemplateSuper = new CardTemplate
                {
                    Title = "Growth Test Super Hero",
                    VisualTitle = "SuperHero",
                    Alignment = "Tactics",
                    Rarity = "Legendary", // Legendary has a max level cap of 90
                    PowerRequirement = 12,
                    BaseAtk = 2000,
                    BaseDef = 2000,
                    MaxAtk = 5000,
                    MaxDef = 5000,
                    MaxMastery = 100
                };
                context.CardTemplates.AddRange(cardTemplateNormal, cardTemplateSuper);
                context.SaveChanges();

                // Fetch seeded Level Up Serum templates dynamically
                var standardSerumTemplate = context.ItemTemplates.FirstOrDefault(t => t.Type == "LevelUpSerum" && !t.Name.Contains("Super"));
                var superSerumTemplate = context.ItemTemplates.FirstOrDefault(t => t.Type == "LevelUpSerum" && t.Name.Contains("Super"));

                if (standardSerumTemplate == null)
                {
                    standardSerumTemplate = new ItemTemplate
                    {
                        Name = "Level Up ISO-8 Serum",
                        Description = "Standard level up serum.",
                        Type = "LevelUpSerum",
                        EffectValue = 3
                    };
                    context.ItemTemplates.Add(standardSerumTemplate);
                    context.SaveChanges();
                }
                if (superSerumTemplate == null)
                {
                    var id36Exists = context.ItemTemplates.Any(t => t.Id == 36);
                    superSerumTemplate = new ItemTemplate
                    {
                        Name = "Super Level Up ISO-8 Serum",
                        Description = "Super level up serum.",
                        Type = "LevelUpSerum",
                        EffectValue = 10
                    };
                    if (!id36Exists) superSerumTemplate.Id = 36;
                    context.ItemTemplates.Add(superSerumTemplate);
                    context.SaveChanges();
                }

                // Add inventory quantities
                var invItemStandard = new PlayerInventoryItem
                {
                    PlayerProfileId = profile.Id,
                    ItemTemplateId = standardSerumTemplate.Id,
                    Quantity = 5
                };
                var invItemSuper = new PlayerInventoryItem
                {
                    PlayerProfileId = profile.Id,
                    ItemTemplateId = superSerumTemplate.Id,
                    Quantity = 2
                };
                context.PlayerInventoryItems.AddRange(invItemStandard, invItemSuper);
                context.SaveChanges();

                // Setup cards
                var card1 = new PlayerCard { PlayerProfileId = profile.Id, CurrentLevel = 1 };
                card1.InitializeStats(cardTemplateNormal, 0); // Start at level 1 with 0 mastery
                var card2 = new PlayerCard { PlayerProfileId = profile.Id, CurrentLevel = 1 };
                card2.InitializeStats(cardTemplateSuper, 0); // Start at level 1 with 0 mastery

                context.PlayerCards.AddRange(card1, card2);
                context.SaveChanges();

                // --------------------------------------------------
                // 1. Single Serum Enhancement - XP & Free Silver Cost Validation
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 1: Single Serum Enhancement ---");
                long initialSilver = profile.SilverBalance;

                // Enhance card1 using exactly 1 standard serum
                var r1 = growthEngine.Enhance(profile.Id, card1.Id, "serum", new List<int> { standardSerumTemplate.Id });
                
                AssertTrue("Enhancement completed successfully", r1.Success);
                AssertEquals("Silver balance remains unchanged (Zero Silver Fee)", initialSilver, profile.SilverBalance);
                AssertEquals("Standard serum quantity decremented by 1 (5 -> 4)", 4, invItemStandard.Quantity);
                // Standard serum template gives 1000 exp (10 levels)
                AssertEquals("Card level increased by 10 (1 -> 11)", 11, card1.CurrentLevel);
                AssertTrue("Card stats recalculated correctly", card1.CurrentAtk > 1000);

                // --------------------------------------------------
                // 2. Multi-Serum Enhancement - Cumulative XP & Free Silver
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 2: Multi-Serum Enhancement ---");
                
                // Enhance card2 using 2 standard serums and 1 super serum simultaneously (materialIds: standard standard super)
                var r2 = growthEngine.Enhance(profile.Id, card2.Id, "serum", new List<int> { standardSerumTemplate.Id, standardSerumTemplate.Id, superSerumTemplate.Id });
                
                AssertTrue("Multi-serum enhancement completed successfully", r2.Success);
                AssertEquals("Silver balance remains unchanged (Zero Silver Fee)", initialSilver, profile.SilverBalance);
                AssertEquals("Standard serum quantity decremented by 2 (4 -> 2)", 2, invItemStandard.Quantity);
                AssertEquals("Super serum quantity decremented by 1 (2 -> 1)", 1, invItemSuper.Quantity);
                
                // standard serum = 1000 exp, super serum = 5000 exp. Total exp = 1000 + 1000 + 5000 = 7000 exp (70 levels)
                AssertEquals("Card level increased by 70 (1 -> 71)", 71, card2.CurrentLevel);

                // --------------------------------------------------
                // 3. Insufficient Serum Stock Guard
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 3: Insufficient Serum Stock Guard ---");
                
                // Attempt to enhance card2 with 3 standard serums (only 2 left in depot)
                var r3 = growthEngine.Enhance(profile.Id, card2.Id, "serum", new List<int> { standardSerumTemplate.Id, standardSerumTemplate.Id, standardSerumTemplate.Id });
                
                AssertTrue("Enhancement fails due to insufficient quantity", !r3.Success);
                AssertTrue("Stock guard returns correct warning message", r3.Message.Contains("Insufficient"));
                AssertEquals("Standard serum stock remains unchanged (2)", 2, invItemStandard.Quantity);

                // --------------------------------------------------
                // 4. Max Level Clearance Cap Safeguards
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 4: Max Level Clearance Cap Safeguards ---");
                
                // card1 has Normal rarity. Max level is 30. It is currently level 11.
                // Enhance card1 with remaining 2 standard serums (gives 2000 exp / 20 levels)
                // New level should hit max cap of 30, not overflow to 31
                var r4 = growthEngine.Enhance(profile.Id, card1.Id, "serum", new List<int> { standardSerumTemplate.Id, standardSerumTemplate.Id });
                
                AssertTrue("Enhancement completes", r4.Success);
                AssertEquals("Card level is capped at 30", 30, card1.CurrentLevel);
                AssertEquals("Standard serum quantity decremented by 2 (2 -> 0)", 0, invItemStandard.Quantity);

                // Now that card1 is at max clearance level, attempting further enhancement with serum should fail
                var r5 = growthEngine.Enhance(profile.Id, card1.Id, "serum", new List<int> { superSerumTemplate.Id });
                AssertTrue("Enhancement fails at maximum level cap", !r5.Success);
                AssertTrue("Returns maximum clearance level limit message", r5.Message.Contains("maximum clearance capacity"));
                AssertEquals("Super serum stock remains unchanged (1)", 1, invItemSuper.Quantity);

                // --------------------------------------------------
                // 5. Item Ledger Direct Delegation Validation
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 5: Item Ledger Direct Delegation ---");
                
                // Reset card2's level to 1 for this test case
                card2.CurrentLevel = 1;
                card2.RecalculateStats();
                context.SaveChanges();

                // Use a Super Level Up Serum directly via ItemLedger (it should delegate to CardGrowthEngine under the hood)
                var r6 = itemLedger.UseItem(profile.Id, superSerumTemplate.Id, card2.Id);
                
                AssertTrue("ItemLedger delegates successfully", r6.Success);
                AssertEquals("Super serum stock decremented by 1 (1 -> 0)", 0, invItemSuper.Quantity);
                // Gained 5000 exp (50 levels)
                AssertEquals("Card level increased by 50 (1 -> 51)", 51, card2.CurrentLevel);

                // --------------------------------------------------
                // 6. Cosmic Cube Fusion Validation
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 6: Cosmic Cube Fusion Validation ---");
                
                // Add silver to afford legendary fusion cost (120,000)
                profile.SilverBalance = 200000;
                
                // Seed Tactics L Cosmic Cube and evolved template
                var cubeTemplate = new CardTemplate
                {
                    Title = "Tactics L Cosmic Cube",
                    VisualTitle = "[Tactics] L Cosmic Cube",
                    Alignment = "Tactics",
                    Rarity = "Legendary",
                    Faction = "Super Hero",
                    Gender = "None",
                    PowerRequirement = 999,
                    BaseAtk = 50,
                    BaseDef = 50,
                    MaxAtk = 50,
                    MaxDef = 50,
                    MaxMastery = 0
                };
                
                var cardTemplateSuperFused = new CardTemplate
                {
                    Title = "Growth Test Super Hero",
                    VisualTitle = "SuperHero+",
                    Alignment = "Tactics",
                    Rarity = "Legendary",
                    PowerRequirement = 12,
                    BaseAtk = 2400,
                    BaseDef = 2400,
                    MaxAtk = 6000,
                    MaxDef = 6000,
                    MaxMastery = 200,
                    VariantName = "Base+"
                };
                
                context.CardTemplates.AddRange(cubeTemplate, cardTemplateSuperFused);
                context.SaveChanges();
                
                // Set variant name on original test template to match fusion expectation
                cardTemplateSuper.VariantName = "Base";
                context.SaveChanges();
                
                // Setup base card (Legendary level 90, maxed mastery 100)
                var baseCardInstance = new PlayerCard { PlayerProfileId = profile.Id };
                baseCardInstance.InitializeStats(cardTemplateSuper, 100);
                baseCardInstance.CurrentLevel = 90;
                baseCardInstance.RecalculateStats();
                
                // Setup Cosmic Cube partner card (level 1, 0 mastery)
                var cubeCardInstance = new PlayerCard { PlayerProfileId = profile.Id };
                cubeCardInstance.InitializeStats(cubeTemplate, 0);
                
                context.PlayerCards.AddRange(baseCardInstance, cubeCardInstance);
                context.SaveChanges();
                
                int expectedInheritedAtk = (int)Math.Round((baseCardInstance.CurrentAtk * 2) * 0.10);
                
                var fusionResult = growthEngine.Fuse(profile.Id, baseCardInstance.Id, cubeCardInstance.Id);
                
                AssertTrue("Cosmic Cube fusion completed successfully", fusionResult.Success);
                AssertEquals("Partner Cosmic Cube was consumed/removed", 0, context.PlayerCards.Count(pc => pc.Id == cubeCardInstance.Id));
                AssertEquals("Base card has evolved to next variant template ID", cardTemplateSuperFused.Id, baseCardInstance.CardTemplateId);
                AssertEquals("Optimal Max Fusion bonus applied (10%)", expectedInheritedAtk, baseCardInstance.FusionBonusAtk);
                AssertEquals("MasteryContrib carry-over applied (90 + 90 = 180)", 180, baseCardInstance.CurrentMastery);
                AssertEquals("Silver balance correctly decremented by Legendary fee (120,000)", 80000, profile.SilverBalance);

                // Clean up database test entries
                var userCards = context.PlayerCards.Where(pc => pc.PlayerProfileId == profile.Id).ToList();
                context.PlayerCards.RemoveRange(userCards);
                var userInventory = context.PlayerInventoryItems.Where(pi => pi.PlayerProfileId == profile.Id).ToList();
                context.PlayerInventoryItems.RemoveRange(userInventory);
                context.CardTemplates.RemoveRange(cardTemplateNormal, cardTemplateSuper);
                
                var extraTemplates = context.CardTemplates.Where(t => t.Title == "Tactics L Cosmic Cube" || t.Title == "Growth Test Super Hero").ToList();
                context.CardTemplates.RemoveRange(extraTemplates);
                
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
