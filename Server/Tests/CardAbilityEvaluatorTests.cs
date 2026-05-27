using System;
using System.Collections.Generic;
using MwohServer.Models;
using MwohServer.Services;

namespace MwohServer.Tests
{
    public static class CardAbilityEvaluatorTests
    {
        public static bool Run(ICardAbilityEvaluator evaluator)
        {
            Console.WriteLine("\n==================================================");
            Console.WriteLine("🧪 INITIALIZING S.H.I.E.L.D. CARD ABILITY TEST SUITE");
            Console.WriteLine("==================================================");

            int passed = 0;
            int failed = 0;

            void AssertEquals(string testName, int expected, int actual)
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
                // --------------------------------------------------
                // 1. Text Parsing Tests
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 1: Ability Text Parsing ---");

                var ab1 = evaluator.ParseEffect("Web-Slinger", "Strengthen Speed ATK.", 1);
                AssertTrue("Parse 'Strengthen Speed ATK.' - Not Null", ab1 != null);
                if (ab1 != null)
                {
                    AssertTrue("ab1.Intensity == Notably", ab1.Intensity == AbilityIntensity.Notably);
                    AssertTrue("ab1.Action == Strengthen", ab1.Action == AbilityAction.Strengthen);
                    AssertTrue("ab1.AffectedStat == Atk", ab1.AffectedStat == AbilityStat.Atk);
                    AssertTrue("ab1.Scope == AlignmentSpeed", ab1.Scope == AbilityScope.AlignmentSpeed);
                }

                var ab2 = evaluator.ParseEffect("I, Robot", "Significantly raise ATK/DEF of your Speeds.", 1);
                AssertTrue("Parse 'Significantly raise ATK/DEF of your Speeds.' - Not Null", ab2 != null);
                if (ab2 != null)
                {
                    AssertTrue("ab2.Intensity == Significantly", ab2.Intensity == AbilityIntensity.Significantly);
                    AssertTrue("ab2.AffectedStat == AtkDef", ab2.AffectedStat == AbilityStat.AtkDef);
                }

                var ab3 = evaluator.ParseEffect("Psionic Powers", "Extremely lower ATK/DEF of opposing heroes.", 1);
                AssertTrue("Parse 'Extremely lower ATK/DEF of opposing heroes.' - Not Null", ab3 != null);
                if (ab3 != null)
                {
                    AssertTrue("ab3.Action == Weaken", ab3.Action == AbilityAction.Weaken);
                    AssertTrue("ab3.Intensity == Extremely", ab3.Intensity == AbilityIntensity.Extremely);
                    AssertTrue("ab3.Scope == TeamEnemies", ab3.Scope == AbilityScope.TeamEnemies);
                }

                var ab4 = evaluator.ParseEffect("Omega Mimic", "Significantly harden DEF of team.", 1);
                AssertTrue("Parse 'Significantly harden DEF of team.' - Not Null", ab4 != null);
                if (ab4 != null)
                {
                    AssertTrue("ab4.AffectedStat == Def", ab4.AffectedStat == AbilityStat.Def);
                    AssertTrue("ab4.Scope == TeamAllies", ab4.Scope == AbilityScope.TeamAllies);
                }

                var ab5 = evaluator.ParseEffect("Indestructible", "Extremely strengthen ATK of your Bruisers/Tactics.", 1);
                AssertTrue("Parse 'Extremely strengthen ATK of your Bruisers/Tactics.' - Not Null", ab5 != null);
                if (ab5 != null)
                {
                    AssertTrue("ab5.Scope == AlignmentsDual", ab5.Scope == AbilityScope.AlignmentsDual);
                }

                // --------------------------------------------------
                // 2. Base Grid Table Resolver & Extrapolation Tests
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 2: Wiki Base Effectiveness & Extrapolations ---");

                // Test alignment single stats (notably, extremely, extraordinarily)
                var capAtk = new CardAbility { Scope = AbilityScope.AlignmentBruiser, AffectedStat = AbilityStat.Atk, Intensity = AbilityIntensity.Notably };
                AssertEquals("Notably Alignment Single (Wiki: 8%)", 8, evaluator.GetBaseEffectiveness(capAtk));

                var spiderAtk = new CardAbility { Scope = AbilityScope.AlignmentSpeed, AffectedStat = AbilityStat.Atk, Intensity = AbilityIntensity.Extremely };
                AssertEquals("Extremely Alignment Single (Wiki: 20%)", 20, evaluator.GetBaseEffectiveness(spiderAtk));

                var legAtk = new CardAbility { Scope = AbilityScope.AlignmentTactics, AffectedStat = AbilityStat.Atk, Intensity = AbilityIntensity.Extraordinarily };
                AssertEquals("Extraordinarily Alignment Single (Wiki: 23%)", 23, evaluator.GetBaseEffectiveness(legAtk));

                // Test Team dual stats
                var teamDualPartially = new CardAbility { Scope = AbilityScope.TeamAllies, AffectedStat = AbilityStat.AtkDef, Intensity = AbilityIntensity.Partially };
                AssertEquals("Partially Team Dual (Wiki: 2%)", 2, evaluator.GetBaseEffectiveness(teamDualPartially));

                var teamDualExtremely = new CardAbility { Scope = AbilityScope.TeamAllies, AffectedStat = AbilityStat.AtkDef, Intensity = AbilityIntensity.Extremely };
                AssertEquals("Extremely Team Dual (Wiki: 12%)", 12, evaluator.GetBaseEffectiveness(teamDualExtremely));

                // Test extrapolated cells
                var selfSingleExtraordinarily = new CardAbility { Scope = AbilityScope.Self, AffectedStat = AbilityStat.Atk, Intensity = AbilityIntensity.Extraordinarily };
                AssertEquals("Extraordinarily Self Single (Extrapolated: 72%)", 72, evaluator.GetBaseEffectiveness(selfSingleExtraordinarily));

                var factionSingleNotably = new CardAbility { Scope = AbilityScope.FactionSuperHero, AffectedStat = AbilityStat.Atk, Intensity = AbilityIntensity.Notably };
                AssertEquals("Notably Faction Single (Extrapolated: 10%)", 10, evaluator.GetBaseEffectiveness(factionSingleNotably));

                // --------------------------------------------------
                // 3. Level-Up Scaling Tests
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 3: Skill Level Scaling ---");

                var spiderLevel1 = new CardAbility { Scope = AbilityScope.AlignmentSpeed, AffectedStat = AbilityStat.Atk, Intensity = AbilityIntensity.Extremely, AbilityLevel = 1 };
                AssertEquals("Spider-Man Extremely Lvl 1 (20%)", 20, evaluator.GetCurrentEffectiveness(spiderLevel1));

                var spiderLevel2 = new CardAbility { Scope = AbilityScope.AlignmentSpeed, AffectedStat = AbilityStat.Atk, Intensity = AbilityIntensity.Extremely, AbilityLevel = 2 };
                AssertEquals("Spider-Man Extremely Lvl 2 (21%)", 21, evaluator.GetCurrentEffectiveness(spiderLevel2));

                var spiderLevel9 = new CardAbility { Scope = AbilityScope.AlignmentSpeed, AffectedStat = AbilityStat.Atk, Intensity = AbilityIntensity.Extremely, AbilityLevel = 9 };
                AssertEquals("Spider-Man Extremely Lvl 9 (28%)", 28, evaluator.GetCurrentEffectiveness(spiderLevel9));

                var spiderLevel10 = new CardAbility { Scope = AbilityScope.AlignmentSpeed, AffectedStat = AbilityStat.Atk, Intensity = AbilityIntensity.Extremely, AbilityLevel = 10 };
                AssertEquals("Spider-Man Extremely Lvl 10 (Double boost: 30%)", 30, evaluator.GetCurrentEffectiveness(spiderLevel10));

                // --------------------------------------------------
                // 4. Deck Battle Evaluation Tests
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 4: Deck Battle Buffs & Debuffs ---");

                // Set up friendly deck
                var spiderTemplate = new CardTemplate { Id = 1, Title = "Spider-Man", Alignment = "Speed", Faction = "Super Hero", AbilityName = "Web-Slinger", AbilityEffect = "Strengthen Speed ATK." };
                var capTemplate = new CardTemplate { Id = 2, Title = "Captain America", Alignment = "Bruiser", Faction = "Super Hero" };
                
                var spiderCard = new PlayerCard { Id = 101, CurrentAtk = 5000, CurrentDef = 4500, AbilityLevel = 1, CardTemplate = spiderTemplate, CardTemplateId = 1 };
                var capCard = new PlayerCard { Id = 102, CurrentAtk = 5200, CurrentDef = 6200, AbilityLevel = 1, CardTemplate = capTemplate, CardTemplateId = 2 };

                var friendlyDeck = new List<PlayerCard> { spiderCard, capCard };

                // Set up opposing deck
                var modokTemplate = new CardTemplate { Id = 3, Title = "MODOK", Alignment = "Bruiser", Faction = "Villain", AbilityName = "Psionic Powers", AbilityEffect = "Extremely lower ATK/DEF of opposing heroes." };
                var modokCard = new PlayerCard { Id = 201, CurrentAtk = 8000, CurrentDef = 8000, AbilityLevel = 1, CardTemplate = modokTemplate, CardTemplateId = 3 };

                var opposingDeck = new List<PlayerCard> { modokCard };

                // Evaluate friendly deck in battle context (Attacking)
                var friendlyResult = evaluator.EvaluateDeck(friendlyDeck, opposingDeck, true);

                // Spider-Man should have buff from own card (+8% ATK Notably Lvl 1) but also debuff from MODOK (-12% ATK/DEF Extremely dual Lvl 1)
                var resultSpider = friendlyResult.First(r => r.Card.Id == 101);
                AssertEquals("Spider-Man Buff ATK percentage (+8%)", 8, resultSpider.ActiveBuffPercentageAtk);
                AssertEquals("Spider-Man Debuff ATK percentage (-12%)", 12, resultSpider.ActiveDebuffPercentageAtk);
                AssertEquals("Spider-Man Buff DEF percentage (+0%)", 0, resultSpider.ActiveBuffPercentageDef);
                AssertEquals("Spider-Man Debuff DEF percentage (-12%)", 12, resultSpider.ActiveDebuffPercentageDef);

                // Captain America is a Bruiser, so should NOT get Spider-Man's Speed ATK buff (+0% ATK), but gets MODOK's debuff (-12% ATK/DEF)
                var resultCap = friendlyResult.First(r => r.Card.Id == 102);
                AssertEquals("Captain America Buff ATK percentage (+0%)", 0, resultCap.ActiveBuffPercentageAtk);
                AssertEquals("Captain America Debuff ATK percentage (-12%)", 12, resultCap.ActiveDebuffPercentageAtk);

                // Check final calculated combat stats
                // Spider-Man ATK: 5000 * (1 + (8 - 12)/100) = 5000 * 0.96 = 4800
                // Spider-Man DEF: 4500 * (1 + (0 - 12)/100) = 4500 * 0.88 = 3960
                AssertEquals("Spider-Man final combat ATK (4800)", 4800, resultSpider.FinalAtk);
                AssertEquals("Spider-Man final combat DEF (3960)", 3960, resultSpider.FinalDef);

                // Captain America ATK: 5200 * (1 + (0 - 12)/100) = 5200 * 0.88 = 4576
                AssertEquals("Captain America final combat ATK (4576)", 4576, resultCap.FinalAtk);
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
