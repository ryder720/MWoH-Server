using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using MwohServer.Data;
using MwohServer.Models;
using MwohServer.Services;

namespace MwohServer.Tests
{
    public static class ProfileManagerTests
    {
        public static bool Run(IProfileManager manager, MwohDbContext context)
        {
            Console.WriteLine("\n==================================================");
            Console.WriteLine("🧪 INITIALIZING S.H.I.E.L.D. PROFILE MANAGER TESTS");
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
                var user = new UserAccount { Username = "profile_test_agent", PasswordHash = "hash" };
                context.Users.Add(user);
                context.SaveChanges();

                var profile = new PlayerProfile
                {
                    UserAccountId = user.Id,
                    Nickname = "ProfileAgent",
                    Level = 1,
                    StatPoints = 15,
                    EnergyMax = 100,
                    EnergyCurrent = 100,
                    AttackPower = 10,
                    DefensePower = 10
                };
                context.Profiles.Add(profile);
                context.SaveChanges();

                // Setup test cards for leader designations
                var cardTemplate1 = new CardTemplate
                {
                    Title = "Profile Test Hero 1",
                    VisualTitle = "Hero_1",
                    Alignment = "Speed",
                    Rarity = "Rare",
                    PowerRequirement = 10,
                    BaseAtk = 2000,
                    BaseDef = 1800,
                    MaxAtk = 5000,
                    MaxDef = 4500
                };
                var cardTemplate2 = new CardTemplate
                {
                    Title = "Profile Test Hero 2",
                    VisualTitle = "Hero_2",
                    Alignment = "Tactics",
                    Rarity = "Rare",
                    PowerRequirement = 12,
                    BaseAtk = 2100,
                    BaseDef = 1900,
                    MaxAtk = 5200,
                    MaxDef = 4600
                };
                context.CardTemplates.AddRange(cardTemplate1, cardTemplate2);
                context.SaveChanges();

                var card1 = new PlayerCard { PlayerProfileId = profile.Id, IsLeader = false };
                card1.InitializeStats(cardTemplate1, GameplaySettings.DefaultMasteryPercentage);
                var card2 = new PlayerCard { PlayerProfileId = profile.Id, IsLeader = true };
                card2.InitializeStats(cardTemplate2, GameplaySettings.DefaultMasteryPercentage);

                context.PlayerCards.AddRange(card1, card2);
                context.SaveChanges();

                // --------------------------------------------------
                // 1. Stat Points Allocation Validation
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 1: Stat Points Allocation Guards ---");
                
                // Zero points allocation
                var r1 = manager.AllocateStatPoints(profile.Id, 0, 0, 0);
                AssertTrue("Allocation fails when total allocated is zero", !r1.Success);

                // Exceeds available unallocated points (profile has 15, trying to allocate 16)
                var r2 = manager.AllocateStatPoints(profile.Id, 5, 5, 6);
                AssertTrue("Allocation fails when exceeding available S.H.I.E.L.D. points", !r2.Success);
                AssertTrue("Correct unallocated points cap error message", r2.Message.Contains("exceed available unallocated"));

                // Missing profile validation
                var r3 = manager.AllocateStatPoints(99999, 1, 1, 1);
                AssertTrue("Allocation fails for non-existent profile ID", !r3.Success);

                // --------------------------------------------------
                // 2. Successful Stat Points Allocation
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 2: Successful Parameter Increments ---");
                // Allocate 10 points (3 Energy, 4 Attack, 3 Defense)
                var r4 = manager.AllocateStatPoints(profile.Id, 3, 4, 3);
                AssertTrue("Allocation completes successfully", r4.Success);
                AssertEquals("StatPoints decremented by 10 (15 -> 5)", 5, profile.StatPoints);
                AssertEquals("EnergyMax incremented by 3 (100 -> 103)", 103, profile.EnergyMax);
                AssertEquals("EnergyCurrent incremented by 3 (100 -> 103)", 103, profile.EnergyCurrent);
                AssertEquals("AttackPower incremented by 4 (10 -> 14)", 14, profile.AttackPower);
                AssertEquals("DefensePower incremented by 3 (10 -> 13)", 13, profile.DefensePower);

                // --------------------------------------------------
                // 3. Representative Leader Designation
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 3: Leader Designation & Sync ---");

                // Target card not belonging to player profile
                var r5 = manager.DesignateLeader(profile.Id, 99999);
                AssertTrue("DesignateLeader fails when card ID does not belong to profile", !r5.Success);

                // Designate card1 as new leader (card2 is currently leader)
                var r6 = manager.DesignateLeader(profile.Id, card1.Id);
                AssertTrue("DesignateLeader completes successfully for valid own card", r6.Success);
                
                // Assert leader status updated on DB level
                AssertTrue("Designated card1 set to IsLeader = true", card1.IsLeader);
                AssertTrue("Previous leader card2 set to IsLeader = false", !card2.IsLeader);

                // Clean up database test entries
                var userCards = context.PlayerCards.Where(pc => pc.PlayerProfileId == profile.Id).ToList();
                context.PlayerCards.RemoveRange(userCards);
                context.CardTemplates.RemoveRange(cardTemplate1, cardTemplate2);
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
