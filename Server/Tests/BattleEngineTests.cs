using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using MwohServer.Data;
using MwohServer.Models;
using MwohServer.Services;

namespace MwohServer.Tests
{
    public static class BattleEngineTests
    {
        public static bool Run(IBattleEngine battleEngine, MwohDbContext context)
        {
            Console.WriteLine("\n==================================================");
            Console.WriteLine("🧪 INITIALIZING S.H.I.E.L.D. PVP BATTLE TEST SUITE");
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
                // Ensure card templates are present in DB
                var cardTemplates = context.CardTemplates.ToList();
                if (!cardTemplates.Any())
                {
                    Console.WriteLine("[TEST STUB] Seeding templates for unit tests...");
                    // Seeding fallback templates directly
                    var t1 = new CardTemplate { Title = "Spider-Man", Alignment = "Speed", Rarity = "Rare", PowerRequirement = 10, BaseAtk = 2000, BaseDef = 1800, MaxAtk = 5000, MaxDef = 4500, MaxMastery = 40, AbilityName = "Web-Slinger", AbilityEffect = "Strengthen Speed ATK." };
                    var t2 = new CardTemplate { Title = "Hulk", Alignment = "Bruiser", Rarity = "Legendary", PowerRequirement = 15, BaseAtk = 3000, BaseDef = 2500, MaxAtk = 8000, MaxDef = 6000, MaxMastery = 50, AbilityName = "Gamma Slam", AbilityEffect = "Massively Strengthen Bruiser ATK." };
                    context.CardTemplates.Add(t1);
                    context.CardTemplates.Add(t2);
                    context.SaveChanges();
                    cardTemplates = context.CardTemplates.ToList();
                }

                // --------------------------------------------------
                // 1. Dynamic Points Recovery Tests
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 1: Dynamic Lazy Points Recovery ---");

                var tempProfile = new PlayerProfile
                {
                    Nickname = "RecoveryAgent",
                    AttackPower = 100,
                    AttackPowerCurrent = 20, // depleted
                    DefensePower = 100,
                    DefensePowerCurrent = 20, // depleted
                    LastBattlePowerRecoveryTime = DateTime.UtcNow.AddMinutes(-9) // 9 minutes ago
                };

                // With GameplaySettings interval of 180s (3m) and amount of 1:
                // 9 minutes elapsed / 3m interval = 3 intervals * 1 amount = +3 points
                GameplaySettings.AttackRecoveryIntervalSeconds = 180;
                GameplaySettings.AttackRecoveryAmount = 1;
                GameplaySettings.DefenseRecoveryIntervalSeconds = 180;
                GameplaySettings.DefenseRecoveryAmount = 1;

                battleEngine.RestoreBattlePower(tempProfile);
                AssertEquals("Lazy Recovery ATK Current (20 + 3 = 23)", 23, tempProfile.AttackPowerCurrent);
                AssertEquals("Lazy Recovery DEF Current (20 + 3 = 23)", 23, tempProfile.DefensePowerCurrent);

                // --------------------------------------------------
                // 2. Setup Attacker and Defender in Db
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 2: Combat Resolution Setup ---");

                var attackerUser = new UserAccount { Username = "atk_test", PasswordHash = "pwd" };
                var defenderUser = new UserAccount { Username = "def_test", PasswordHash = "pwd" };
                context.Users.AddRange(attackerUser, defenderUser);
                context.SaveChanges();

                var attacker = new PlayerProfile
                {
                    UserAccountId = attackerUser.Id,
                    Nickname = "AttackerAgent",
                    Level = 50,
                    AttackPower = 150,
                    AttackPowerCurrent = 150,
                    DefensePower = 150,
                    DefensePowerCurrent = 150,
                    SilverBalance = 10000
                };
                var defender = new PlayerProfile
                {
                    UserAccountId = defenderUser.Id,
                    Nickname = "DefenderAgent",
                    Level = 50,
                    AttackPower = 150,
                    AttackPowerCurrent = 150,
                    DefensePower = 150,
                    DefensePowerCurrent = 150,
                    SilverBalance = 20000
                };
                context.Profiles.AddRange(attacker, defender);
                context.SaveChanges();

                // Give them cards
                var template = cardTemplates.FirstOrDefault(t => t.PowerRequirement > 0 && t.PowerRequirement <= 15) ?? cardTemplates.First();
                template.PowerRequirement = 10; // Guarantee standard power requirement for test consistency
                var card1 = new PlayerCard { PlayerProfileId = attacker.Id, IsInAttackDeck = true };
                card1.InitializeStats(template, 100);
                var card2 = new PlayerCard { PlayerProfileId = defender.Id, IsInDefenseDeck = true };
                card2.InitializeStats(template, 100);

                attacker.Cards.Add(card1);
                defender.Cards.Add(card2);

                context.PlayerCards.AddRange(card1, card2);
                context.SaveChanges();

                // --------------------------------------------------
                // 3. AP Limits Cost Guard Tests
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 3: AP Clearance Cost Guard ---");

                // Deplete Attacker AP below the card power requirement (10)
                attacker.AttackPowerCurrent = 5;
                context.SaveChanges();

                var apResult = battleEngine.ResolveBattle(attacker.Id, defender.Id, isSparring: true);
                AssertTrue("Rejection when AP (5) < squad clearance cost (10)", !apResult.Success);
                AssertTrue("Correct error message for clearance power deficit", apResult.Message.Contains("Clearance Power"));

                // Restore Attacker AP
                attacker.AttackPowerCurrent = 150;
                context.SaveChanges();

                // --------------------------------------------------
                // 4. Daily Attack Limits Tests
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 4: Daily Attack Limits ---");

                // Simulate 3 battles today
                var timeNow = DateTime.UtcNow;
                for (int i = 0; i < 3; i++)
                {
                    var record = new BattleRecord
                    {
                        AttackerProfileId = attacker.Id,
                        DefenderProfileId = defender.Id,
                        WinnerProfileId = attacker.Id,
                        BattleTime = timeNow
                    };
                    context.BattleRecords.Add(record);
                }
                context.SaveChanges();

                var limitResult = battleEngine.ResolveBattle(attacker.Id, defender.Id, isSparring: true);
                AssertTrue("Rejection when daily attack limit reached (3 attacks)", !limitResult.Success);
                AssertTrue("Correct error message for limit blocks", limitResult.Message.Contains("limit of 3"));

                // Clean up dummy battle records to allow engagement tests
                var recordsToRemove = context.BattleRecords.Where(r => r.AttackerProfileId == attacker.Id).ToList();
                context.BattleRecords.RemoveRange(recordsToRemove);
                context.SaveChanges();

                // --------------------------------------------------
                // 5. Battle Resolution & Silver Exchange
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 5: Engagement & Rewards ---");

                // Ensure attacker wins: set attacker card to ultra stats, defender card to zero
                card1.CurrentAtk = 99999;
                card2.CurrentDef = 1;
                context.SaveChanges();

                var initAtkSilver = attacker.SilverBalance;
                var initDefSilver = defender.SilverBalance;

                var combatResult = battleEngine.ResolveBattle(attacker.Id, defender.Id, isSparring: false);
                Console.WriteLine($"  [DIAGNOSTIC] Success={combatResult.Success}, Message={combatResult.Message}");
                if (combatResult.LogLines.Any())
                {
                    Console.WriteLine("  [DIAGNOSTIC] Log lines:");
                    foreach (var l in combatResult.LogLines)
                    {
                        Console.WriteLine($"    {l}");
                    }
                }
                AssertTrue("Combat completes successfully", combatResult.Success);
                AssertTrue("Attacker wins battle", combatResult.AttackerWon);
                AssertTrue("Transferred silver > 0", combatResult.SilverExchanged > 0);
                AssertEquals("Silver added to attacker balance", initAtkSilver + combatResult.SilverExchanged, attacker.SilverBalance);
                AssertEquals("Silver deducted from defender balance", initDefSilver - combatResult.SilverExchanged, defender.SilverBalance);

                // Verify mastery rewards
                // Defender level (50) == Attacker level (50), should give +3 mastery
                AssertEquals("Mastery earned equals +3", 3, combatResult.MasteryEarned);
                
                // Clean up tests data to keep DB clean
                context.PlayerCards.RemoveRange(card1, card2);
                context.Profiles.RemoveRange(attacker, defender);
                context.Users.RemoveRange(attackerUser, defenderUser);
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
