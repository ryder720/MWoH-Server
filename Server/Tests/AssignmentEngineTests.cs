using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using MwohServer.Data;
using MwohServer.Models;
using MwohServer.Services;

namespace MwohServer.Tests
{
    public static class AssignmentEngineTests
    {
        public static bool Run(IAssignmentEngine engine, MwohDbContext context)
        {
            Console.WriteLine("\n==================================================");
            Console.WriteLine("🧪 INITIALIZING S.H.I.E.L.D. ASSIGNMENTS TESTS");
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
                // Helper to cleanly wipe test data
                void CleanupTestData()
                {
                    var testUserIds = context.Users
                        .Where(u => u.Username.StartsWith("AssignmentTestAgent"))
                        .Select(u => u.Id)
                        .ToList();

                    var testProfileIds = context.Profiles
                        .Where(p => testUserIds.Contains(p.UserAccountId) || p.Nickname.StartsWith("TestAgent"))
                        .Select(p => p.Id)
                        .ToList();

                    // Remove inventory items
                    var items = context.PlayerInventoryItems.Where(i => testProfileIds.Contains(i.PlayerProfileId)).ToList();
                    context.PlayerInventoryItems.RemoveRange(items);

                    // Remove cards
                    var cards = context.PlayerCards.Where(c => testProfileIds.Contains(c.PlayerProfileId)).ToList();
                    context.PlayerCards.RemoveRange(cards);

                    // Remove assignment progress
                    var progress = context.PlayerAssignmentProgress.Where(p => testProfileIds.Contains(p.PlayerProfileId)).ToList();
                    context.PlayerAssignmentProgress.RemoveRange(progress);

                    // Remove profiles & users
                    var profiles = context.Profiles.Where(p => testProfileIds.Contains(p.Id)).ToList();
                    context.Profiles.RemoveRange(profiles);

                    var users = context.Users.Where(u => testUserIds.Contains(u.Id)).ToList();
                    context.Users.RemoveRange(users);

                    context.SaveChanges();
                }

                // Initial cleanup
                CleanupTestData();

                // --------------------------------------------------
                // SETUP SECTOR: Seed Test Agent Profile
                // --------------------------------------------------
                var testUser = new UserAccount { Username = "AssignmentTestAgent", PasswordHash = "password" };
                context.Users.Add(testUser);
                context.SaveChanges();

                var testProfile = new PlayerProfile
                {
                    UserAccountId = testUser.Id,
                    Nickname = "TestAgentAlpha",
                    Level = 5,
                    SilverBalance = 50000,
                    RallyPoints = 1000,
                    MobaCoinBalance = 0,
                    MaxCardCapacity = 100,
                    PlayerIdString = "889911"
                };
                context.Profiles.Add(testProfile);
                context.SaveChanges();

                // Seed baseline templates for tests if missing in db
                if (!context.ItemTemplates.Any(t => t.Id == 1))
                {
                    context.ItemTemplates.Add(new ItemTemplate { Id = 1, Name = "Energy Iso-8 (L)", Description = "Restores energy.", Type = "EnergyRestorative", ImageFileName = "energy.png" });
                }
                if (!context.CardTemplates.Any(t => t.Id == 1001))
                {
                    context.CardTemplates.Add(new CardTemplate { Id = 1001, Title = "Leopardess Tigra", VisualTitle = "[Leopardess] Tigra", Alignment = "Speed", BaseAtk = 1000, BaseDef = 1000 });
                }
                context.SaveChanges();

                var profileId = testProfile.Id;
                engine.ReloadTemplates();

                // --------------------------------------------------
                // 1. Quest Progression Accumulation Tests
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 1: Progressive Quest Accumulation ---");

                // Trigger a battle (target is 1)
                engine.RecordEvent(profileId, GoalType.PvpBattle, 1);
                var progressList = engine.GetPlayerProgress(profileId);
                var battleQuest = progressList.FirstOrDefault(q => q.Template.Id == "init_fight_battle");

                AssertTrue("Initial PVP Quest exists in active set", battleQuest != null);
                if (battleQuest != null)
                {
                    AssertEquals("Quest progress incremented to 1", 1, battleQuest.CurrentProgress);
                    AssertTrue("Quest successfully completed", battleQuest.IsCompleted);
                    AssertTrue("Quest is not claimed yet", !battleQuest.IsClaimed);
                }

                // --------------------------------------------------
                // 2. Clearance Level Milestones Auto-Evaluation
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 2: Level Milestone Auto-Evaluation ---");

                // Profile level is currently 5, let's bump it to 15 directly
                testProfile.Level = 15;
                context.SaveChanges();
                context.ChangeTracker.Clear();

                progressList = engine.GetPlayerProgress(profileId);
                var lvl10Quest = progressList.FirstOrDefault(q => q.Template.Id == "lvl_10");
                var lvl15Quest = progressList.FirstOrDefault(q => q.Template.Id == "lvl_15");
                var lvl20Quest = progressList.FirstOrDefault(q => q.Template.Id == "lvl_20");

                AssertTrue("Clearance Level 10 quest detected", lvl10Quest != null);
                if (lvl10Quest != null)
                {
                    AssertTrue("Clearance Level 10 quest auto-completed", lvl10Quest.IsCompleted);
                }

                AssertTrue("Clearance Level 15 quest detected", lvl15Quest != null);
                if (lvl15Quest != null)
                {
                    AssertTrue("Clearance Level 15 quest auto-completed", lvl15Quest.IsCompleted);
                }

                AssertTrue("Clearance Level 20 quest detected", lvl20Quest != null);
                if (lvl20Quest != null)
                {
                    AssertTrue("Clearance Level 20 quest remains incomplete", !lvl20Quest.IsCompleted);
                }

                // --------------------------------------------------
                // 3. Date Restricted Events & Bypasses
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 3: Date Restricted Filters & Bypass ---");

                // Toggle Date Restriction off
                GameplaySettings.IgnoreAssignmentDates = false;
                var allTimeTemplates = engine.GetActiveTemplates();
                bool hasSpecialEvent = allTimeTemplates.Any(t => t.GroupName.StartsWith("Special Assignment"));
                
                // Thanksgiving/Christmas dates are far in Nov/Dec 2026. If current time is not in that window, they should be filtered out
                var now = DateTime.UtcNow;
                bool shouldHaveSpecial = now >= new DateTime(2026, 11, 22) && now <= new DateTime(2026, 11, 27);
                AssertEquals("Special quests filtered based on real date", shouldHaveSpecial ? 1 : 0, hasSpecialEvent ? 1 : 0);

                // Enable dev date bypass
                GameplaySettings.IgnoreAssignmentDates = true;
                var bypassTemplates = engine.GetActiveTemplates();
                bool hasBypassSpecial = bypassTemplates.Any(t => t.GroupName.StartsWith("Special Assignment"));
                AssertTrue("Bypass ignore_assignment_dates overrides holiday dates successfully", hasBypassSpecial);

                // --------------------------------------------------
                // 4. Reward Claiming Mechanics
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 4: Reward Claiming Mechanics ---");

                // Re-fetch profile to ensure latest DB synchronization
                var freshProfile = context.Profiles.Find(profileId)!;
                var prevSilver = freshProfile.SilverBalance;

                // Let's claim Level 15 (Reward: CardStock capacity expansion +5)
                var prevSlots = freshProfile.MaxCardCapacity;
                var claimResult = engine.ClaimReward(profileId, "lvl_15");
                AssertTrue("Clearance Level 15 slots expansion claimed successfully", claimResult.Success);
                
                freshProfile = context.Profiles.Find(profileId)!;
                AssertEquals("Hero slots capacity raised by 5", prevSlots + 5, freshProfile.MaxCardCapacity);

                // Claim battleQuest (Reward: Item template 3 / Attack Iso-8 / Attack Restorative)
                var pQuest = engine.GetPlayerProgress(profileId).First(q => q.Template.Id == "init_fight_battle");
                AssertTrue("Fight battle quest is completed and unclaimed", pQuest.IsCompleted && !pQuest.IsClaimed);
                
                var itemClaimResult = engine.ClaimReward(profileId, "init_fight_battle");
                AssertTrue("Quest item reward claimed successfully", itemClaimResult.Success);

                // Ensure item added to inventory
                var hasItem = context.PlayerInventoryItems.Any(i => i.PlayerProfileId == profileId && i.Quantity > 0);
                AssertTrue("Reward item exists in agent's dossier inventory", hasItem);

                // --------------------------------------------------
                // 5. Sequential Batch Completion Bonuses
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 5: Sequential Batch Bonus Triggering ---");

                // Let's test Special Event Batch 1
                // It has 5 regular quests and 1 completion bonus quest
                var specialQuests = engine.GetPlayerProgress(profileId)
                    .Where(q => q.Template.GroupName == "Special Assignment 1" && q.Template.Batch == 1)
                    .ToList();

                AssertTrue("Special Assignment 1 Batch 1 exists in context", specialQuests.Count > 0);

                var regularSpecialQuests = specialQuests.Where(q => !q.Template.IsCompletionBonus).ToList();
                var bonusSpecialQuest = specialQuests.FirstOrDefault(q => q.Template.IsCompletionBonus);

                AssertEquals("Batch contains exactly 5 regular assignments", 5, regularSpecialQuests.Count);
                AssertTrue("Batch contains 1 Completion Bonus quest", bonusSpecialQuest != null);

                // Artificially complete all 5 regular quests
                foreach (var q in regularSpecialQuests)
                {
                    var rec = context.PlayerAssignmentProgress.FirstOrDefault(r => r.PlayerProfileId == profileId && r.AssignmentId == q.Template.Id);
                    if (rec == null)
                    {
                        rec = new PlayerAssignmentProgress { PlayerProfileId = profileId, AssignmentId = q.Template.Id, CurrentProgress = q.Template.GoalTarget, IsCompleted = true };
                        context.PlayerAssignmentProgress.Add(rec);
                    }
                    else
                    {
                        rec.CurrentProgress = q.Template.GoalTarget;
                        rec.IsCompleted = true;
                    }
                }
                context.SaveChanges();

                // Claim the first 4 quests
                for (int i = 0; i < 4; i++)
                {
                    engine.ClaimReward(profileId, regularSpecialQuests[i].Template.Id);
                }

                // Verify bonus NOT unlocked yet
                if (bonusSpecialQuest != null)
                {
                    var bonusRec = context.PlayerAssignmentProgress.FirstOrDefault(r => r.PlayerProfileId == profileId && r.AssignmentId == bonusSpecialQuest.Template.Id);
                    AssertTrue("Completion Bonus remains locked when only 4/5 quests are claimed", bonusRec == null || !bonusRec.IsClaimed);
                }

                // Claim the 5th and final quest
                var finalQuest = regularSpecialQuests[4];
                var finalClaimResult = engine.ClaimReward(profileId, finalQuest.Template.Id);
                
                AssertTrue("Final 5th batch quest claimed successfully", finalClaimResult.Success);
                AssertTrue("Claiming all 5 triggers automatic Batch Completion Bonus message", finalClaimResult.Message.Contains("BATCH COMPLETION BONUS UNLOCKED"));

                // Verify bonus card marked completed and claimed
                if (bonusSpecialQuest != null)
                {
                    var bonusRec = context.PlayerAssignmentProgress.FirstOrDefault(r => r.PlayerProfileId == profileId && r.AssignmentId == bonusSpecialQuest.Template.Id);
                    AssertTrue("Completion Bonus quest marked completed in DB", bonusRec != null && bonusRec.IsCompleted);
                    AssertTrue("Completion Bonus quest marked claimed in DB", bonusRec != null && bonusRec.IsClaimed);
                }

                // Cleanup
                CleanupTestData();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"💥 EXCEPTION OCCURRED DURING TESTS: {ex.Message}\n{ex.StackTrace}");
                Console.ResetColor();
                return false;
            }

            Console.WriteLine("==================================================");
            Console.WriteLine($"🧪 ASSIGNMENTS TESTS: {passed} PASSED // {failed} FAILED");
            Console.WriteLine("==================================================");

            return failed == 0;
        }
    }
}
