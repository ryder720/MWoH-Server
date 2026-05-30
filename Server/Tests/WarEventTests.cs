using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using MwohServer.Data;
using MwohServer.Models;
using MwohServer.Services;

namespace MwohServer.Tests
{
    public static class WarEventTests
    {
        public static bool Run(IWarEventEngine engine, MwohDbContext context)
        {
            Console.WriteLine("\n==================================================");
            Console.WriteLine("🧪 INITIALIZING S.H.I.E.L.D. WAR EVENT PORTAL TESTS");
            Console.WriteLine("==================================================");

            int passed = 0;
            int failed = 0;

            void AssertEquals(string testName, long expected, long actual)
            {
                if (expected == actual)
                {
                    passed++;
                    Console.WriteLine($"  War sync: ✅ [PASS] {testName} (Expected: {expected}, Actual: {actual})");
                }
                else
                {
                    failed++;
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"  War sync: ❌ [FAIL] {testName} (Expected: {expected}, Actual: {actual})");
                    Console.ResetColor();
                }
            }

            void AssertTrue(string testName, bool condition)
            {
                if (condition)
                {
                    passed++;
                    Console.WriteLine($"  War sync: ✅ [PASS] {testName}");
                }
                else
                {
                    failed++;
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"  War sync: ❌ [FAIL] {testName}");
                    Console.ResetColor();
                }
            }

            try
            {
                // Helper to cleanly wipe test data
                void CleanupTestData()
                {
                    var testBattles = context.AllianceWarBattles
                        .Where(b => b.EventId == "event_war_test")
                        .ToList();
                    context.AllianceWarBattles.RemoveRange(testBattles);

                    var testAlliances = context.Alliances
                        .Where(a => a.Name.StartsWith("WarTestAlliance") || a.Name.Contains("HYDRA Vanguard") || a.Name.Contains("A.I.M."))
                        .ToList();

                    var testAllianceIds = testAlliances.Select(a => a.Id).ToList();

                    var profilesToModify = context.Profiles
                        .Where(p => p.AllianceId.HasValue && testAllianceIds.Contains(p.AllianceId.Value))
                        .ToList();

                    foreach (var p in profilesToModify)
                    {
                        p.AllianceId = null;
                        p.AllianceRole = null;
                    }
                    context.SaveChanges();

                    context.Alliances.RemoveRange(testAlliances);
                    context.SaveChanges();

                    var testUserIds = context.Users
                        .Where(u => u.Username.StartsWith("WarTestAgent"))
                        .Select(u => u.Id)
                        .ToList();

                    var testProfileIds = context.Profiles
                        .Where(p => testUserIds.Contains(p.UserAccountId))
                        .Select(p => p.Id)
                        .ToList();

                    // Remove event progresses
                    var progresses = context.PlayerEventProgresses
                        .Where(ep => testProfileIds.Contains(ep.PlayerProfileId))
                        .ToList();
                    context.PlayerEventProgresses.RemoveRange(progresses);

                    // Remove cards
                    var cards = context.PlayerCards
                        .Where(c => testProfileIds.Contains(c.PlayerProfileId))
                        .ToList();
                    context.PlayerCards.RemoveRange(cards);

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
                // SETUP SECTOR: Seed Test Alliances & Agents
                // --------------------------------------------------
                var userAlpha = new UserAccount { Username = "WarTestAgentAlpha", PasswordHash = "pwd" };
                context.Users.Add(userAlpha);
                context.SaveChanges();

                var profileAlpha = new PlayerProfile
                {
                    UserAccountId = userAlpha.Id,
                    Nickname = "WarAgentAlpha",
                    Level = 35,
                    AttackPower = 200,
                    AttackPowerCurrent = 200,
                    DefensePower = 150,
                    DefensePowerCurrent = 150
                };
                context.Profiles.Add(profileAlpha);
                context.SaveChanges();

                // Seed card for profileAlpha
                if (!context.CardTemplates.Any(t => t.Id == 1001))
                {
                    context.CardTemplates.Add(new CardTemplate { Id = 1001, Title = "Leopardess Tigra", Alignment = "Speed", PowerRequirement = 12, BaseAtk = 200, BaseDef = 200 });
                }
                context.SaveChanges();

                var cardAlpha = new PlayerCard
                {
                    PlayerProfileId = profileAlpha.Id,
                    CardTemplateId = 1001,
                    CardTemplate = context.CardTemplates.Find(1001),
                    CurrentAtk = 800,
                    CurrentDef = 800,
                    IsInAttackDeck = true
                };
                context.PlayerCards.Add(cardAlpha);
                context.SaveChanges();

                // Create Test Alliance
                var alliance = new Alliance
                {
                    Name = "WarTestAllianceAlpha",
                    LeaderProfileId = profileAlpha.Id,
                    Level = 5,
                    Rating = 1200,
                    CreatedAt = DateTime.UtcNow
                };
                context.Alliances.Add(alliance);
                context.SaveChanges();

                profileAlpha.AllianceId = alliance.Id;
                profileAlpha.AllianceRole = "Leader";
                context.SaveChanges();

                // --------------------------------------------------
                // 1. Non-blocking Matchmaking Queue Entries
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 1: Asynchronous Non-Blocking Queue ---");
                engine.EnterMatchmakingQueue(alliance.Id);

                // Fetch alliance from context to inspect state
                var queueAlliance = context.Alliances.Find(alliance.Id)!;
                AssertTrue("Alliance successfully flagged in war queue database", queueAlliance.IsQueuedForWar);
                AssertTrue("Matchmaking timestamp registered successfully", queueAlliance.WarQueueJoinedAt.HasValue);

                // --------------------------------------------------
                // 2. 10-Minute Timeout & AI Fallback Matchmaking
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 2: 10-Minute Timeout AI Fallback ---");
                
                // Cheat matchmaking timer: set join time to 12 minutes in the past
                queueAlliance.WarQueueJoinedAt = DateTime.UtcNow.AddMinutes(-12);
                context.SaveChanges();

                var activeBattle = engine.CheckOrMatchmakeAlliance(alliance.Id, "event_war_test");
                
                AssertTrue("Matchmaking timeout triggered successfully", activeBattle != null);
                if (activeBattle != null)
                {
                    AssertEquals("Initialized battle marked active", 1, activeBattle.Status == "Active" ? 1 : 0);
                    AssertTrue("Matchup correctly registered as AI opponent", activeBattle.IsAiOpponent);
                    AssertTrue("Alliance queue flag is successfully cleared", !queueAlliance.IsQueuedForWar);

                    long expectedHp = 10000 + alliance.Rating * 5;
                    AssertEquals("Core HP scales dynamically with rating", expectedHp, activeBattle.AllianceAHealthMax);
                    AssertEquals("Rival Core HP initialized fully", activeBattle.AllianceBHealthMax, activeBattle.AllianceBHealthCurrent);

                    var leadersA = System.Text.Json.JsonSerializer.Deserialize<List<WarDefensiveLeaderState>>(activeBattle.AllianceADefensiveLeadersJson)!;
                    var leadersB = System.Text.Json.JsonSerializer.Deserialize<List<WarDefensiveLeaderState>>(activeBattle.AllianceBDefensiveLeadersJson)!;

                    AssertEquals("Alliance A defensive leaders generated", 3, leadersA.Count);
                    AssertEquals("AI opponent defensive leaders generated", 3, leadersB.Count);

                    var commanderA = leadersA[0];
                    AssertEquals("Primary defensive leader is Alliance Leader", 1, commanderA.Role == "Leader" ? 1 : 0);
                    AssertEquals("Primary leader profile ID is correct", profileAlpha.Id, commanderA.ProfileId);
                    AssertEquals("Shield capacity matches profile Defense Power", profileAlpha.DefensePower, commanderA.DefPowerMax);

                    // --------------------------------------------------
                    // 3. Core Shield Protections
                    // --------------------------------------------------
                    Console.WriteLine("\n--- Phase 3: Core Headquarters Shields Lock ---");
                    
                    var failedAssault = engine.ResolveWarEngagement(profileAlpha.Id, "event_war_test", 0, isCoreAttack: true);
                    AssertTrue("Assault on Headquarters Core blocked while defenders stand", !failedAssault.Success);
                    AssertTrue("Blocked attack returned correct warning details", failedAssault.Message.Contains("Core Shields lock"));

                    // Verify AP was not deducted on failed core attack
                    var pAlpha = context.Profiles.Find(profileAlpha.Id)!;
                    AssertEquals("Attack Power was not deducted on failed strike", 200, pAlpha.AttackPowerCurrent);

                    // --------------------------------------------------
                    // 4. Defensive Commander Strikes
                    // --------------------------------------------------
                    Console.WriteLine("\n--- Phase 4: Defensive Commander Engagements ---");
                    
                    var targetId = leadersB[0].ProfileId; // Target the first AI leader
                    var strikeRes = engine.ResolveWarEngagement(profileAlpha.Id, "event_war_test", targetId, isCoreAttack: false);

                    AssertTrue("Defensive leader engagement resolved successfully", strikeRes.Success);
                    AssertTrue("Attack Power correctly deducted", pAlpha.AttackPowerCurrent < 200);
                    AssertTrue("Opposing commander sustained defensive power damage", strikeRes.TargetDefPowerAfter < strikeRes.TargetDefPowerBefore);
                    AssertTrue("Operative earned Valor points", strikeRes.PointsEarned > 0);
                    AssertTrue("No overdrive indicators present in calculations", !strikeRes.CombatLogs.Any(log => log.Contains("Overdrive") || log.Contains("3.0x")));

                    // --------------------------------------------------
                    // 5. Headquarters direct assaults & Victory
                    // --------------------------------------------------
                    Console.WriteLine("\n--- Phase 5: Shields Down direct Core assault & Victory ---");

                    // Cheat: Manually neutralize all AI defensive leaders in database
                    var battleRecord = context.AllianceWarBattles.Find(activeBattle.Id)!;
                    var leadersListB = System.Text.Json.JsonSerializer.Deserialize<List<WarDefensiveLeaderState>>(battleRecord.AllianceBDefensiveLeadersJson)!;
                    foreach (var l in leadersListB)
                    {
                        l.DefPowerCurrent = 0;
                    }
                    battleRecord.AllianceBDefensiveLeadersJson = System.Text.Json.JsonSerializer.Serialize(leadersListB);
                    context.SaveChanges();

                    // Restore AP for test profiling
                    pAlpha.AttackPowerCurrent = 200;
                    context.SaveChanges();

                    // Strike Headquarters directly
                    var hqAssault = engine.ResolveWarEngagement(profileAlpha.Id, "event_war_test", 0, isCoreAttack: true);
                    AssertTrue("Direct assault on Core Headquarters succeeded with shields down", hqAssault.Success);
                    AssertTrue("Dealt substantial Core HP damage", hqAssault.DamageDealt > 0);

                    var battleState = context.AllianceWarBattles.Find(activeBattle.Id)!;
                    AssertTrue("Direct Core damage serialized to database", battleState.AllianceBHealthCurrent < battleState.AllianceBHealthMax);

                    // Trigger direct core annihilation (victory condition)
                    battleState.AllianceBHealthCurrent = 10;
                    context.SaveChanges();

                    pAlpha.AttackPowerCurrent = 200;
                    context.SaveChanges();

                    var finalAssault = engine.ResolveWarEngagement(profileAlpha.Id, "event_war_test", 0, isCoreAttack: true);
                    AssertTrue("Final assault succeeded", finalAssault.Success);
                    
                    var endedBattle = context.AllianceWarBattles.Find(activeBattle.Id)!;
                    AssertEquals("Core HP reduced to 0", 0, endedBattle.AllianceBHealthCurrent);
                    AssertEquals("Match concluded successfully", 1, endedBattle.Status == "Concluded" ? 1 : 0);
                    AssertEquals("Winner Alliance ID is player Alliance", alliance.Id, endedBattle.WinnerAllianceId ?? 0);
                    AssertTrue("Victory logged with full system bonus points", finalAssault.PointsEarned >= 2000);
                }

                // Cleanup test data
                CleanupTestData();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"💥 EXCEPTION OCCURRED DURING PORTAL TESTS: {ex.Message}\n{ex.StackTrace}");
                Console.ResetColor();
                return false;
            }

            Console.WriteLine("==================================================");
            Console.WriteLine($"🧪 PORTAL TESTS COMPLETED: {passed} PASSED // {failed} FAILED");
            Console.WriteLine("==================================================");

            return failed == 0;
        }
    }
}
