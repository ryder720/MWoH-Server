using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using MwohServer.Data;
using MwohServer.Models;
using MwohServer.Services;

namespace MwohServer.Tests
{
    public static class GachaSummonerTests
    {
        public static bool Run(IGachaSummoner gachaSummoner, MwohDbContext context)
        {
            Console.WriteLine("\n==================================================");
            Console.WriteLine("🧪 INITIALIZING S.H.I.E.L.D. GACHA SUMMONER TESTS");
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
                // Helper to cleanly wipe test data from database
                void CleanupTestData()
                {
                    var testUserIds = context.Users
                        .Where(u => u.Username.StartsWith("GachaTestUser"))
                        .Select(u => u.Id)
                        .ToList();

                    var testProfileIds = context.Profiles
                        .Where(p => testUserIds.Contains(p.UserAccountId) || p.Nickname.StartsWith("GachaTestAgent"))
                        .Select(p => p.Id)
                        .ToList();

                    var profiles = context.Profiles.Where(p => testProfileIds.Contains(p.Id)).ToList();
                    context.Profiles.RemoveRange(profiles);
                    context.SaveChanges();

                    var users = context.Users.Where(u => testUserIds.Contains(u.Id)).ToList();
                    context.Users.RemoveRange(users);
                    context.SaveChanges();
                }

                // Initial cleanup
                CleanupTestData();

                // --------------------------------------------------
                // 1. Setup Test Profile
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 1: Test Agent Seeding ---");

                var testUser = new UserAccount { Username = "GachaTestUser", PasswordHash = "pwd" };
                context.Users.Add(testUser);
                context.SaveChanges();

                var testProfile = new PlayerProfile
                {
                    UserAccountId = testUser.Id,
                    Nickname = "GachaTestAgent",
                    Level = 50,
                    SilverBalance = 10000,
                    MobaCoinBalance = 1000,
                    RallyPoints = 500,
                    MaxCardCapacity = 50,
                    PlayerIdString = "889977"
                };
                context.Profiles.Add(testProfile);
                context.SaveChanges();

                // Ensure card templates exist in DB
                var cardTemplates = context.CardTemplates.ToList();
                if (!cardTemplates.Any())
                {
                    var t1 = new CardTemplate { Title = "Spider-Man", Alignment = "Speed", Rarity = "Rare", PowerRequirement = 10, BaseAtk = 2000, BaseDef = 1800, MaxAtk = 5000, MaxDef = 4500, MaxMastery = 40, AbilityName = "Web-Slinger", AbilityEffect = "Strengthen Speed ATK." };
                    var t2 = new CardTemplate { Title = "Hulk", Alignment = "Bruiser", Rarity = "Legendary", PowerRequirement = 15, BaseAtk = 3000, BaseDef = 2500, MaxAtk = 8000, MaxDef = 6000, MaxMastery = 50, AbilityName = "Gamma Slam", AbilityEffect = "Massively Strengthen Bruiser ATK." };
                    context.CardTemplates.AddRange(t1, t2);
                    context.SaveChanges();
                    cardTemplates = context.CardTemplates.ToList();
                }

                // Retrieve or seed standard ticket template with ID = 2
                bool wasTicketTemplateSeeded = false;
                var ticketTemplate = context.ItemTemplates.Find(2);
                if (ticketTemplate == null)
                {
                    ticketTemplate = new ItemTemplate
                    {
                        Id = 2,
                        Name = "Ultimate Card Pack Ticket",
                        Type = "GachaTicket",
                        Description = "Recruits a Hero."
                    };
                    context.ItemTemplates.Add(ticketTemplate);
                    context.SaveChanges();
                    wasTicketTemplateSeeded = true;
                }

                var invTicket = new PlayerInventoryItem
                {
                    PlayerProfileId = testProfile.Id,
                    ItemTemplateId = ticketTemplate.Id,
                    Quantity = 5
                };
                context.PlayerInventoryItems.Add(invTicket);
                context.SaveChanges();

                AssertTrue("Test profile and inventory setup successful", testProfile.Id > 0);

                // --------------------------------------------------
                // 2. Ultimate Ticket Pull Tests (Configured Packs)
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 2: Ultimate Ticket Recruitment ---");

                var ultimateResult = gachaSummoner.PullViaTicket(testProfile.Id, "Ultimate Card Pack Ticket");
                AssertTrue("Ultimate ticket pull succeeds", ultimateResult.Success);
                AssertTrue("Ultimate ticket pull yields exactly 1 card", ultimateResult.PulledCards.Count == 1);
                
                // Re-fetch ticket inventory item to verify quantity was decremented by GachaSummoner
                context.Entry(invTicket).Reload();
                AssertEquals("Inventory ticket quantity is decremented to 4", 4, invTicket.Quantity);

                // --------------------------------------------------
                // 3. Custom Faction Ticket Filtering Tests
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 3: Alignment-Specific Ticket Filters ---");

                // We pull via "Bruiser Ticket"
                var bruiserResult = gachaSummoner.PullViaTicket(testProfile.Id, "Special Bruiser Recruitment Ticket");
                AssertTrue("Special Bruiser ticket pull succeeds", bruiserResult.Success);
                
                var pulledCard = bruiserResult.PulledCards.FirstOrDefault();
                AssertTrue("Pulled card is not null", pulledCard != null);
                if (pulledCard != null)
                {
                    AssertEquals("Bruiser Ticket yields a Bruiser card", 1, string.Equals(pulledCard.CardTemplate?.Alignment, "Bruiser", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
                }

                // --------------------------------------------------
                // 4. Capacity Limit Enforcement Tests
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 4: Max Capacity Constraints ---");

                // Restrict capacity to current cards count
                int currentCardsCount = context.PlayerCards.Count(c => c.PlayerProfileId == testProfile.Id);
                testProfile.MaxCardCapacity = currentCardsCount;
                context.SaveChanges();

                var capResult = gachaSummoner.PullViaTicket(testProfile.Id, "Ultimate Card Pack Ticket");
                AssertTrue("Rejection when squad is already at max card capacity", !capResult.Success);
                AssertTrue("Correct capacity error message", capResult.Message.Contains("capacity"));

                // Restore capacity
                testProfile.MaxCardCapacity = 50;
                context.SaveChanges();

                // --------------------------------------------------
                // 5. Cleanup
                // --------------------------------------------------
                CleanupTestData();
                if (wasTicketTemplateSeeded && ticketTemplate != null)
                {
                    context.ItemTemplates.Remove(ticketTemplate);
                    context.SaveChanges();
                }
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
