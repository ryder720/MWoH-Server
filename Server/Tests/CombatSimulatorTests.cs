using System;
using System.Collections.Generic;
using System.Linq;
using MwohServer.Models;
using MwohServer.Services;

namespace MwohServer.Tests
{
    public static class CombatSimulatorTests
    {
        public static bool Run(ICombatSimulator simulator)
        {
            Console.WriteLine("\n==================================================");
            Console.WriteLine("🧪 INITIALIZING S.H.I.E.L.D. COMBAT SIMULATOR TESTS");
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
                // Helper to create an Alliance boost
                AllianceStatsBoost CreateDefaultBoost()
                {
                    return new AllianceStatsBoost
                    {
                        AtkModifier = 1.0,
                        DefModifier = 1.0,
                        Logs = ""
                    };
                }

                // --------------------------------------------------
                // Test Case 1: Simple Pure Combat (No Boosts, No Abilities, No Combos)
                // --------------------------------------------------
                Console.WriteLine("\n--- Case 1: Base Combat Power Resolution ---");

                var attackerCards = new List<PlayerCard>
                {
                    new PlayerCard
                    {
                        Id = 1,
                        CurrentAtk = 5000,
                        CurrentDef = 3000,
                        CardTemplate = new CardTemplate { Id = 101, Title = "Agent A", Alignment = "Speed" }
                    }
                };

                var defenderCards = new List<PlayerCard>
                {
                    new PlayerCard
                    {
                        Id = 2,
                        CurrentAtk = 3000,
                        CurrentDef = 4000,
                        CardTemplate = new CardTemplate { Id = 102, Title = "Agent B", Alignment = "Speed" }
                    }
                };

                var boostsA = new List<AllianceStatsBoost> { CreateDefaultBoost() };
                var boostsD = new List<AllianceStatsBoost> { CreateDefaultBoost() };

                var res1 = simulator.Simulate(
                    attackerCards,
                    defenderCards,
                    "AttackingAgent",
                    50,
                    150,
                    10,
                    "DefendingAgent",
                    50,
                    150,
                    10,
                    1.0, // scale
                    boostsA,
                    boostsD,
                    isSparring: true
                );

                // Attacker Final Power should be 5000 ATK
                // Defender Final Power should be 4000 DEF
                // 5000 > 4000 so Attacker wins
                AssertEquals("Attacker Final Power resolves to 5000", 5000, res1.AttackerFinalPower);
                AssertEquals("Defender Final Power resolves to 4000", 4000, res1.DefenderFinalPower);
                AssertTrue("Attacker wins (5000 ATK > 4000 DEF)", res1.AttackerWon);
                AssertEquals("Ability trigger count is 0 for no-ability templates", 0, res1.AttackerTriggerCount);

                // --------------------------------------------------
                // Test Case 2: Morale / Depletion Scaling
                // --------------------------------------------------
                Console.WriteLine("\n--- Case 2: Morale Depletion Scaling ---");

                var res2 = simulator.Simulate(
                    attackerCards,
                    defenderCards,
                    "AttackingAgent",
                    50,
                    150,
                    10,
                    "DefendingAgent",
                    50,
                    150,
                    10,
                    0.5, // 50% depleted scale
                    boostsA,
                    boostsD,
                    isSparring: true
                );

                // Defender has 4000 DEF base. Scaled to 50% = 2000 DEF
                AssertEquals("Defender Final Power is scaled down by 50% (4000 -> 2000)", 2000, res2.DefenderFinalPower);
                AssertTrue("Log contains depletion warning", res2.LogLines.Any(l => l.Contains("depleted") && l.Contains("50%")));

                // --------------------------------------------------
                // Test Case 3: Alliance Stats Boosts
                // --------------------------------------------------
                Console.WriteLine("\n--- Case 3: Alliance Boosts Application ---");

                var customBoostA = new AllianceStatsBoost
                {
                    AtkModifier = 1.25, // +25% boost
                    DefModifier = 1.0,
                    Logs = "Alliance Flag Level 5: +25% ATK!\n"
                };

                var res3 = simulator.Simulate(
                    attackerCards,
                    defenderCards,
                    "AttackingAgent",
                    50,
                    150,
                    10,
                    "DefendingAgent",
                    50,
                    150,
                    10,
                    1.0,
                    new List<AllianceStatsBoost> { customBoostA },
                    boostsD,
                    isSparring: true
                );

                // Attacker base ATK is 5000. Boosted to 5000 * 1.25 = 6250
                AssertEquals("Attacker Final Power with +25% Alliance boost (5000 -> 6250)", 6250, res3.AttackerFinalPower);
                AssertTrue("Alliance boost log is preserved in simulation logs", res3.LogLines.Any(l => l.Contains("Alliance Flag Level 5")));

                // --------------------------------------------------
                // Test Case 4: Special Combo Activation
                // --------------------------------------------------
                Console.WriteLine("\n--- Case 4: S.H.I.E.L.D. Special Combo Activation ---");

                // Let's create two cards that trigger a special combo: Spider-Man and Rocket Raccoon ("Hey Rocky!")
                var comboCardsAttacker = new List<PlayerCard>
                {
                    new PlayerCard
                    {
                        Id = 10,
                        CurrentAtk = 2000,
                        CurrentDef = 2000,
                        CardTemplate = new CardTemplate { Id = 1001, Title = "Spider-Man", Alignment = "Speed" }
                    },
                    new PlayerCard
                    {
                        Id = 11,
                        CurrentAtk = 2000,
                        CurrentDef = 2000,
                        CardTemplate = new CardTemplate { Id = 1002, Title = "Rocket Raccoon", Alignment = "Speed" }
                    }
                };

                var comboBoostsA = new List<AllianceStatsBoost> { CreateDefaultBoost(), CreateDefaultBoost() };

                var res4 = simulator.Simulate(
                    comboCardsAttacker,
                    defenderCards,
                    "AttackingAgent",
                    50,
                    150,
                    10,
                    "DefendingAgent",
                    50,
                    150,
                    10,
                    1.0,
                    comboBoostsA,
                    boostsD,
                    isSparring: true
                );

                // The combo "Hey Rocky!" grants +10% ATK to friendly cards
                // Verify the combo log is present
                bool heyRockyLogged = res4.LogLines.Any(l => l.Contains("Hey Rocky!") || l.Contains("Spider-Man") && l.Contains("Rocket Raccoon"));
                AssertTrue("Special combo 'Hey Rocky!' triggers and is logged in combat simulation", heyRockyLogged);

                // --------------------------------------------------
                // Test Case 5: Max Ability Trigger Cap
                // --------------------------------------------------
                Console.WriteLine("\n--- Case 5: Max Ability Trigger Limit ---");

                // Create a squad with 5 cards, all with abilities
                var maxAbilityCards = new List<PlayerCard>();
                for (int i = 0; i < 5; i++)
                {
                    maxAbilityCards.Add(new PlayerCard
                    {
                        Id = 100 + i,
                        CurrentAtk = 1000,
                        CurrentDef = 1000,
                        CardTemplate = new CardTemplate
                        {
                            Id = 200 + i,
                            Title = $"Hero {i}",
                            AbilityName = $"Ability {i}",
                            AbilityEffect = "Strengthen ATK."
                        }
                    });
                }

                // Run simulation multiple times to see if trigger count ever exceeds 3 (which it shouldn't)
                bool triggerLimitViolated = false;
                for (int run = 0; run < 10; run++)
                {
                    var res5 = simulator.Simulate(
                        maxAbilityCards,
                        defenderCards,
                        "Attacker",
                        50,
                        150,
                        10,
                        "Defender",
                        50,
                        150,
                        10,
                        1.0,
                        Enumerable.Repeat(CreateDefaultBoost(), 5).ToList(),
                        boostsD,
                        isSparring: true
                    );

                    if (res5.AttackerTriggerCount > 3)
                    {
                        triggerLimitViolated = true;
                    }
                }

                AssertTrue("Ability triggers are correctly capped at max 3 per combat squad", !triggerLimitViolated);
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
