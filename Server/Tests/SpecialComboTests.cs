using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using MwohServer.Data;
using MwohServer.Models;
using MwohServer.Services;

namespace MwohServer.Tests
{
    public static class SpecialComboTests
    {
        public static bool Run(ISpecialComboEngine comboEngine, MwohDbContext context)
        {
            Console.WriteLine("\n==================================================");
            Console.WriteLine("🧪 INITIALIZING S.H.I.E.L.D. SPECIAL COMBOS TESTS");
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
                        .Where(u => u.Username.StartsWith("ComboAttackerUser") || 
                                    u.Username.StartsWith("ComboDefenderUser"))
                        .Select(u => u.Id)
                        .ToList();

                    var testProfileIds = context.Profiles
                        .Where(p => testUserIds.Contains(p.UserAccountId) ||
                                    p.Nickname.StartsWith("ComboAttacker") ||
                                    p.Nickname.StartsWith("ComboDefender"))
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
                // 1. Character Name Resolving Tests
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 1: Canonical Character Resolving ---");

                AssertEquals("Steve Rogers maps to Captain America", 1, string.Equals(SpecialComboEngine.GetCharacterName("Steve Rogers"), "Captain America", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
                AssertEquals("Sentinel of Liberty Cap maps to Captain America", 1, string.Equals(SpecialComboEngine.GetCharacterName("[Sentinel of Liberty] Captain America"), "Captain America", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
                AssertEquals("Logan maps to Wolverine", 1, string.Equals(SpecialComboEngine.GetCharacterName("Logan"), "Wolverine", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
                AssertEquals("Tony Stark maps to Iron Man", 1, string.Equals(SpecialComboEngine.GetCharacterName("Tony Stark"), "Iron Man", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
                AssertEquals("Hulkbuster maps to Iron Man", 1, string.Equals(SpecialComboEngine.GetCharacterName("Hulkbuster"), "Iron Man", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
                AssertEquals("Void maps to Sentry", 1, string.Equals(SpecialComboEngine.GetCharacterName("[The Dark Void] Sentry"), "Sentry", StringComparison.OrdinalIgnoreCase) ? 1 : 0);

                // --------------------------------------------------
                // 2. Combination Identification & Matching Tests
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 2: Combination Matching ---");

                // Case A: 2-Card Combo: Spider-Man + Rocket Raccoon ("Hey Rocky!")
                var spidey = new PlayerCard { CardTemplate = new CardTemplate { Title = "Spider-Man", Alignment = "Speed" } };
                var rocket = new PlayerCard { CardTemplate = new CardTemplate { Title = "Rocket Raccoon", Alignment = "Speed" } };
                var other1 = new PlayerCard { CardTemplate = new CardTemplate { Title = "Maria Hill", Alignment = "Tactics" } };
                var other2 = new PlayerCard { CardTemplate = new CardTemplate { Title = "Nick Fury", Alignment = "Tactics" } };
                var other3 = new PlayerCard { CardTemplate = new CardTemplate { Title = "Black Widow", Alignment = "Speed" } };

                var deckA = new List<PlayerCard> { spidey, rocket, other1, other2, other3 };
                var resultsA = comboEngine.ProcessDeckCombos(deckA, isAttacking: true);

                var heyRocky = resultsA.FirstOrDefault(r => r.ComboId == "SCHeyRocky");
                AssertTrue("Detects 'Hey Rocky!' combo when Spider-Man and Rocket Raccoon are present", heyRocky != null);

                // Case B: Wildcard check: 5 Females ("Dangerous Beauties" / "Glamorous Guardians")
                var female1 = new PlayerCard { CardTemplate = new CardTemplate { Title = "Black Widow", Gender = "Female" } };
                var female2 = new PlayerCard { CardTemplate = new CardTemplate { Title = "Gamora", Gender = "Female" } };
                var female3 = new PlayerCard { CardTemplate = new CardTemplate { Title = "Scarlet Witch", Gender = "Female" } };
                var female4 = new PlayerCard { CardTemplate = new CardTemplate { Title = "Emma Frost", Gender = "Female" } };
                var female5 = new PlayerCard { CardTemplate = new CardTemplate { Title = "Medusa", Gender = "Female" } };

                var deckB = new List<PlayerCard> { female1, female2, female3, female4, female5 };
                var resultsB = comboEngine.ProcessDeckCombos(deckB, isAttacking: true);
                var dangerousBeauties = resultsB.FirstOrDefault(r => r.ComboId == "SCDangerousBeauties");
                AssertTrue("Detects 'Dangerous Beauties' combo with 5 female cards in attack", dangerousBeauties != null);

                var resultsBDef = comboEngine.ProcessDeckCombos(deckB, isAttacking: false);
                var glamorousGuardians = resultsBDef.FirstOrDefault(r => r.ComboId == "SCGlamorousGuardians");
                AssertTrue("Detects 'Glamorous Guardians' defense combo with 5 female cards on defense", glamorousGuardians != null);

                // --------------------------------------------------
                // 3. Battle Engine Math Buffs Integration Tests
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 3: Battle Engine Buffs Integration ---");

                // Seed dummy accounts
                var attackerUser = new UserAccount { Username = "ComboAttackerUser", PasswordHash = "password" };
                var defenderUser = new UserAccount { Username = "ComboDefenderUser", PasswordHash = "password" };
                context.Users.AddRange(attackerUser, defenderUser);
                context.SaveChanges();

                var attackerProfile = new PlayerProfile
                {
                    UserAccountId = attackerUser.Id,
                    Nickname = "ComboAttacker",
                    Level = 50,
                    SilverBalance = 100000,
                    PlayerIdString = "990091"
                };

                var defenderProfile = new PlayerProfile
                {
                    UserAccountId = defenderUser.Id,
                    Nickname = "ComboDefender",
                    Level = 50,
                    SilverBalance = 100000,
                    PlayerIdString = "990092"
                };
                context.Profiles.AddRange(attackerProfile, defenderProfile);
                context.SaveChanges();

                // Setup specific combo cards for Attacker: Cap & Wolverine ("Super Soldiers" -> 8% ATK boost to Cap/Wolverine, trigger chance 60%)
                var capTemplate = new CardTemplate
                {
                    Title = "Captain America",
                    VisualTitle = "Captain America",
                    Alignment = "Bruiser",
                    BaseAtk = 2000,
                    BaseDef = 2000,
                    MaxAtk = 5000,
                    MaxDef = 5000
                };
                context.CardTemplates.Add(capTemplate);

                var wolvTemplate = new CardTemplate
                {
                    Title = "Wolverine",
                    VisualTitle = "Wolverine",
                    Alignment = "Bruiser",
                    BaseAtk = 2000,
                    BaseDef = 2000,
                    MaxAtk = 5000,
                    MaxDef = 5000
                };
                context.CardTemplates.Add(wolvTemplate);
                context.SaveChanges();

                // Add a defender card so the battle doesn't abort with "no valid cards in squads"
                var pcCap = new PlayerCard { PlayerProfileId = attackerProfile.Id, CardTemplateId = capTemplate.Id, IsInAttackDeck = true, CurrentAtk = 1000, CurrentDef = 1000 };
                var pcWolv = new PlayerCard { PlayerProfileId = attackerProfile.Id, CardTemplateId = wolvTemplate.Id, IsInAttackDeck = true, CurrentAtk = 1000, CurrentDef = 1000 };
                // Defender needs at least one defense card
                var pcDefCard = new PlayerCard { PlayerProfileId = defenderProfile.Id, CardTemplateId = capTemplate.Id, IsInDefenseDeck = true, CurrentAtk = 500, CurrentDef = 500 };
                
                context.PlayerCards.AddRange(pcCap, pcWolv, pcDefCard);
                context.SaveChanges();
                context.ChangeTracker.Clear();

                // Build a battle engine instance with special combo processing
                var evaluator = new CardAbilityEvaluator();
                var allianceEngine = new AllianceEngine(null!, context);
                var combatSimulator = new CombatSimulator(evaluator, comboEngine);
                var battleEngine = new BattleEngine(null!, context, allianceEngine, combatSimulator);

                // Run a spar battle!
                var result = battleEngine.ResolveBattle(attackerProfile.Id, defenderProfile.Id, isSparring: true);
                
                // Assert that the combo was detected (logged — regardless of whether the usage roll triggered it)
                bool superSoldiersLogged = result.LogLines.Any(l => l.Contains("Super Soldiers"));
                AssertTrue("Special combo 'Super Soldiers' is detected and logged in battle records", superSoldiersLogged);

                // Cleanup — re-fetch by ID since ChangeTracker.Clear() detached these entities
                CleanupTestData();
                var capToRemove = context.CardTemplates.Find(capTemplate.Id);
                var wolvToRemove = context.CardTemplates.Find(wolvTemplate.Id);
                if (capToRemove != null) context.CardTemplates.Remove(capToRemove);
                if (wolvToRemove != null) context.CardTemplates.Remove(wolvToRemove);
                context.SaveChanges();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"💥 EXCEPTION OCCURRED DURING TESTS: {ex.Message}\n{ex.StackTrace}");
                Console.ResetColor();
                return false;
            }

            Console.WriteLine("==================================================");
            Console.WriteLine($"🧪 SPECIAL COMBO TESTS: {passed} PASSED // {failed} FAILED");
            Console.WriteLine("==================================================");

            return failed == 0;
        }
    }
}
