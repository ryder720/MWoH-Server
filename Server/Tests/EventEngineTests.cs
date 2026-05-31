using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using MwohServer.Data;
using MwohServer.Models;
using MwohServer.Services;

namespace MwohServer.Tests
{
    public static class EventEngineTests
    {
        public static bool Run(IEventEngine engine, MwohDbContext context)
        {
            Console.WriteLine("\n==================================================");
            Console.WriteLine("🧪 INITIALIZING S.H.I.E.L.D. EVENT PORTAL TESTS");
            Console.WriteLine("==================================================");

            int passed = 0;
            int failed = 0;

            void AssertEquals(string testName, long expected, long actual)
            {
                if (expected == actual)
                {
                    passed++;
                    Console.WriteLine($"  Base sync: ✅ [PASS] {testName} (Expected: {expected}, Actual: {actual})");
                }
                else
                {
                    failed++;
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"  Base sync: ❌ [FAIL] {testName} (Expected: {expected}, Actual: {actual})");
                    Console.ResetColor();
                }
            }

            void AssertTrue(string testName, bool condition)
            {
                if (condition)
                {
                    passed++;
                    Console.WriteLine($"  Base sync: ✅ [PASS] {testName}");
                }
                else
                {
                    failed++;
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"  Base sync: ❌ [FAIL] {testName}");
                    Console.ResetColor();
                }
            }

            try
            {
                // Helper to cleanly wipe test data
                void CleanupTestData()
                {
                    var testUserIds = context.Users
                        .Where(u => u.Username.StartsWith("EventTestAgent"))
                        .Select(u => u.Id)
                        .ToList();

                    var testProfileIds = context.Profiles
                        .Where(p => testUserIds.Contains(p.UserAccountId))
                        .Select(p => p.Id)
                        .ToList();

                    // Remove event progress
                    var progresses = context.PlayerEventProgresses
                        .Where(ep => testProfileIds.Contains(ep.PlayerProfileId))
                        .ToList();
                    context.PlayerEventProgresses.RemoveRange(progresses);

                    // Remove inventory items
                    var items = context.PlayerInventoryItems
                        .Where(i => testProfileIds.Contains(i.PlayerProfileId))
                        .ToList();
                    context.PlayerInventoryItems.RemoveRange(items);

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
                // SETUP SECTOR: Seed Test Agents
                // --------------------------------------------------
                var userAlpha = new UserAccount { Username = "EventTestAgentAlpha", PasswordHash = "pwd" };
                var userBeta = new UserAccount { Username = "EventTestAgentBeta", PasswordHash = "pwd" };
                var userGamma = new UserAccount { Username = "EventTestAgentGamma", PasswordHash = "pwd" };
                
                context.Users.AddRange(userAlpha, userBeta, userGamma);
                context.SaveChanges();

                var profileAlpha = new PlayerProfile { UserAccountId = userAlpha.Id, Nickname = "AgentAlpha", Level = 10, SilverBalance = 10000, MobaCoinBalance = 100, RallyPoints = 10 };
                var profileBeta = new PlayerProfile { UserAccountId = userBeta.Id, Nickname = "AgentBeta", Level = 12, SilverBalance = 10000 };
                var profileGamma = new PlayerProfile { UserAccountId = userGamma.Id, Nickname = "AgentGamma", Level = 15, SilverBalance = 10000 };

                context.Profiles.AddRange(profileAlpha, profileBeta, profileGamma);
                context.SaveChanges();

                // Ensure item template and card template exist for rewards seeding
                if (!context.ItemTemplates.Any(t => t.Id == 1))
                {
                    context.ItemTemplates.Add(new ItemTemplate { Id = 1, Name = "Energy ISO-8 (L)", Description = "Restores Energy", Type = "EnergyRestorative", ImageFileName = "iso.png" });
                }
                var template1001 = context.CardTemplates.Find(1001);
                if (template1001 == null)
                {
                    template1001 = new CardTemplate { Id = 1001, Title = "Leopardess Tigra", VisualTitle = "[Leopardess] Tigra", Alignment = "Speed", PowerRequirement = 10, BaseAtk = 100, BaseDef = 100 };
                    context.CardTemplates.Add(template1001);
                }
                else
                {
                    template1001.PowerRequirement = 10;
                }
                context.SaveChanges();

                // Seed custom card for profileAlpha so they have an active attack deck card with dynamic power requirement
                var alphaCard = new PlayerCard
                {
                    PlayerProfileId = profileAlpha.Id,
                    CardTemplateId = 1001,
                    CardTemplate = context.CardTemplates.Find(1001),
                    CurrentAtk = 500,
                    CurrentDef = 500,
                    IsInAttackDeck = true
                };
                context.PlayerCards.Add(alphaCard);
                context.SaveChanges();

                engine.ReloadTemplates();

                var activeEvent = engine.GetActiveEvent();
                AssertTrue("Active event loaded from blueprints", activeEvent != null);

                if (activeEvent != null)
                {
                    var eventId = activeEvent.Id;

                    // --------------------------------------------------
                    // 1. Lazy Progress Registration
                    // --------------------------------------------------
                    Console.WriteLine("\n--- Phase 1: Lazy Registration Check ---");
                    
                    var progressAlpha = engine.GetPlayerProgress(profileAlpha.Id, eventId);
                    AssertTrue("Event progress record successfully auto-initialized", progressAlpha != null);
                    if (progressAlpha != null)
                    {
                        AssertEquals("Initial event points set to 0", 0, progressAlpha.Points);
                        AssertEquals("Initial claimed milestone mask is 0", 0, progressAlpha.TierClaimed);
                        AssertTrue("Initial calculation rewards claimed is false", !progressAlpha.RankRewardsClaimed);
                    }

                    // --------------------------------------------------
                    // 2. Event State transitions
                    // --------------------------------------------------
                    Console.WriteLine("\n--- Phase 2: State Transitions Check ---");
                    
                    var state = engine.GetEventState(activeEvent);
                    AssertEquals("Default mocked event resolves to Active state", 1, state == "Active" ? 1 : 0);

                    // Mock dates for Upcoming check
                    var upcomingEvent = new EventTemplate
                    {
                        Id = "temp_upcoming",
                        StartDate = DateTime.UtcNow.AddDays(1),
                        EndDate = DateTime.UtcNow.AddDays(3),
                        ResultDate = DateTime.UtcNow.AddDays(4)
                    };
                    AssertEquals("Scheduled future event resolves to Upcoming state", 1, engine.GetEventState(upcomingEvent) == "Upcoming" ? 1 : 0);

                    // Mock dates for Calculating check
                    var calcEvent = new EventTemplate
                    {
                        Id = "temp_calc",
                        StartDate = DateTime.UtcNow.AddDays(-3),
                        EndDate = DateTime.UtcNow.AddDays(-1),
                        ResultDate = DateTime.UtcNow.AddDays(1)
                    };
                    AssertEquals("Active event past EndDate resolves to Calculating state (Event Maintenance)", 1, engine.GetEventState(calcEvent) == "Calculating" ? 1 : 0);

                    // --------------------------------------------------
                    // 3. Scoring & Relative Rankings
                    // --------------------------------------------------
                    Console.WriteLine("\n--- Phase 3: Scoring & Comparative Rankings ---");

                    // Seed points
                    engine.RecordEventPoints(profileBeta.Id, eventId, 15000);  // High
                    engine.RecordEventPoints(profileAlpha.Id, eventId, 8000);  // Middle
                    engine.RecordEventPoints(profileGamma.Id, eventId, 2000);  // Low

                    var rankBeta = engine.GetPlayerRank(profileBeta.Id, eventId);
                    var rankAlpha = engine.GetPlayerRank(profileAlpha.Id, eventId);
                    var rankGamma = engine.GetPlayerRank(profileGamma.Id, eventId);

                    AssertEquals("Top points scorer holds global Rank #1", 1, rankBeta);
                    AssertEquals("Middle points scorer holds global Rank #2", 2, rankAlpha);
                    AssertEquals("Lowest points scorer holds global Rank #3", 3, rankGamma);

                    var scoreboard = engine.GetLeaderboard(eventId, 3);
                    AssertEquals("Leaderboard returns top 3 operative ranks", 3, scoreboard.Count);
                    if (scoreboard.Count >= 3)
                    {
                        AssertEquals("First leaderboard spot nickname is AgentBeta", 1, scoreboard[0].Nickname == "AgentBeta" ? 1 : 0);
                        AssertEquals("Third leaderboard spot points is 2,000", 2000, scoreboard[2].Points);
                    }

                    // --------------------------------------------------
                    // 4. Milestone Reward Claims
                    // --------------------------------------------------
                    Console.WriteLine("\n--- Phase 4: Milestone Claims Mechanics ---");

                    // Milestone 0: 1000 PTS -> Energy ISO-8 x2
                    // Milestone 1: 5000 PTS -> 1000 MobaCoins
                    // Milestone 2: 10000 PTS -> [Leopardess] Tigra Card

                    // Agent Gamma has 2000 points. Can claim tier 0, but not tier 1
                    var claimGamma0 = engine.ClaimMilestoneReward(profileGamma.Id, eventId, 0);
                    AssertTrue("Agent Gamma claims completed Milestone 0 (1000 PTS threshold)", claimGamma0.Success);
                    
                    var checkItem = context.PlayerInventoryItems.FirstOrDefault(ii => ii.PlayerProfileId == profileGamma.Id && ii.ItemTemplateId == 1);
                    AssertTrue("Agent Gamma dossier inventory now contains Energy ISO-8 items", checkItem != null && checkItem.Quantity == 2);

                    var claimGamma1 = engine.ClaimMilestoneReward(profileGamma.Id, eventId, 1);
                    AssertTrue("Agent Gamma claim Milestone 1 rejected (points insufficient)", !claimGamma1.Success);

                    // Agent Beta has 15000 points. Can claim tier 2 (Legendary Card)
                    var prevCardsCount = context.PlayerCards.Count(c => c.PlayerProfileId == profileBeta.Id);
                    var claimBeta2 = engine.ClaimMilestoneReward(profileBeta.Id, eventId, 2);
                    
                    AssertTrue("Agent Beta claims Completed Milestone 2 (10000 PTS threshold)", claimBeta2.Success);
                    var cardClaimed = context.PlayerCards.FirstOrDefault(c => c.PlayerProfileId == profileBeta.Id && c.CardTemplateId == 1001);
                    AssertTrue("Agent Beta card roster now contains [Leopardess] Tigra", cardClaimed != null);

                    // Double claim check
                    var claimBeta2Repeat = engine.ClaimMilestoneReward(profileBeta.Id, eventId, 2);
                    AssertTrue("Agent Beta repeat claim of Milestone 2 rejected (double claiming locked)", !claimBeta2Repeat.RepeatCheck(claimBeta2Repeat));

                    // --------------------------------------------------
                    // 5. Result Calculations & Rank Reward Dispatch
                    // --------------------------------------------------
                    Console.WriteLine("\n--- Phase 5: Ranking Rewards Dispatch ---");

                    // Ranks brackets defined in events_config.json:
                    // Rank 1-3: Card template 1001 x3
                    // Rank 4-10: Card template 1001 x1
                    // Rank 11-100: 250,000 Silver

                    // Execute final ranks calculation
                    var prevAlphaCoins = context.Profiles.Find(profileAlpha.Id)!.MobaCoinBalance;
                    var res = engine.CalculateAndDispatchRewards(eventId);
                    
                    AssertTrue("Automated calculations dispatch completes successfully", res.Success);
                    AssertTrue("Calculations processed at least 3 active operatives", res.AgentsProcessed >= 3);

                    // Check Rank #1 (Agent Beta) reward: Card template 1001 x3. Since they already got 1 card from milestones, total cards should be 4
                    var totalBetaCards = context.PlayerCards.Count(c => c.PlayerProfileId == profileBeta.Id && c.CardTemplateId == 1001);
                    AssertEquals("Rank #1 Agent Beta correctly credited 3x Legendary Cards", 4, totalBetaCards);

                    // Double calculation check
                    var resRepeat = engine.CalculateAndDispatchRewards(eventId);
                    AssertEquals("Repeat calculations process 0 operatives (dispatched flags logged)", 0, resRepeat.AgentsProcessed);

                    // --------------------------------------------------
                    // 6. Raid Event System Integration
                    // --------------------------------------------------
                    Console.WriteLine("\n--- Phase 6: Raid Event Integration & Combat ---");

                    // Seed Attack Power and leaders
                    var pAlpha = context.Profiles.Find(profileAlpha.Id)!;
                    pAlpha.AttackPowerCurrent = 200;
                    
                    var pBeta = context.Profiles.Find(profileBeta.Id)!;
                    var betaLeader = context.PlayerCards.FirstOrDefault(c => c.PlayerProfileId == profileBeta.Id);
                    if (betaLeader != null)
                    {
                        betaLeader.IsLeader = true;
                    }
                    context.SaveChanges();

                    // Get Raid State Initialization
                    var raidState = engine.GetRaidState(profileAlpha.Id, eventId);
                    AssertTrue("Easy Target initialized successfully", raidState.EasyTarget.IsInitialized);
                    AssertTrue("Medium Target initialized successfully", raidState.MediumTarget.IsInitialized);
                    AssertTrue("Hard Target initialized successfully", raidState.HardTarget.IsInitialized);
                    
                    AssertTrue("Easy level scales dynamically", raidState.EasyTarget.Level >= 1);
                    AssertTrue("Easy Body part is generated", !string.IsNullOrEmpty(raidState.EasyTarget.BodyPartName));
                    AssertEquals("Easy Body part HP is 30% of Main Core", (long)(raidState.EasyTarget.MainHpMax * 0.3), raidState.EasyTarget.BodyPartHpMax);

                    // Get cooperative helpers list
                    var helpers = engine.GetAvailableHelpers(profileAlpha.Id, 5);
                    AssertTrue("Helper list returns seeded Beta operative", helpers.Any(h => h.Nickname == "AgentBeta"));

                    // Bind cooperative helper
                    engine.SelectRaidHelper(profileAlpha.Id, eventId, profileBeta.Id);
                    var raidStateWithHelper = engine.GetRaidState(profileAlpha.Id, eventId);
                    AssertEquals("Cooperative helper successfully bound to Alpha", profileBeta.Id, raidStateWithHelper.HelperProfileId ?? 0);

                    // Resolve standard combat
                    var startEasyMainHp = raidStateWithHelper.EasyTarget.MainHpCurrent;
                    var battleRes = engine.ResolveRaidBattle(profileAlpha.Id, eventId, "Easy");
                    
                    AssertTrue("Raid Battle engaged successfully", battleRes.Success);
                    AssertEquals("Standard strike deducted deck AP (10)", 190, pAlpha.AttackPowerCurrent);
                    AssertTrue("Damage dealt is strictly positive", battleRes.PlayerDamage > 0);
                    AssertTrue("Net damage scales after defense cut", battleRes.NetDamage > 0);

                    // Verify partial damage save vs clear
                    var rawProgress = context.PlayerEventProgresses.FirstOrDefault(ep => ep.PlayerProfileId == profileAlpha.Id && ep.EventId == eventId);
                    var stateAfterStrikeRaw = System.Text.Json.JsonSerializer.Deserialize<RaidProgressState>(rawProgress?.CustomProgressJson ?? "{}") ?? new RaidProgressState();

                    var stateAfterStrike = engine.GetRaidState(profileAlpha.Id, eventId);
                    if (battleRes.VictoryType == "Defeat")
                    {
                        AssertEquals("Current Main HP decreased accurately by net damage", startEasyMainHp - battleRes.NetDamage, stateAfterStrike.EasyTarget.MainHpCurrent);
                        AssertEquals("Cooperative helper remains linked on defeat", profileBeta.Id, stateAfterStrike.HelperProfileId ?? 0);
                    }
                    else
                    {
                        AssertTrue("Clearance resets Boss initialization state", !stateAfterStrikeRaw.EasyTarget.IsInitialized);
                        AssertTrue("Cooperative helper is auto-cleared on victory", !stateAfterStrikeRaw.HelperProfileId.HasValue);
                    }

                    // Reset HP manually to low values to force One-Shot clear
                    pAlpha.AttackPowerCurrent = 150;
                    var stateForceOneShot = engine.GetRaidState(profileAlpha.Id, eventId);
                    stateForceOneShot.EasyTarget.MainHpCurrent = 500;
                    stateForceOneShot.EasyTarget.BodyPartHpCurrent = 150;
                    
                    var progressAlphaRecord = engine.GetPlayerProgress(profileAlpha.Id, eventId);
                    progressAlphaRecord.CustomProgressJson = System.Text.Json.JsonSerializer.Serialize(stateForceOneShot);
                    context.SaveChanges();

                    // Re-bind helper
                    engine.SelectRaidHelper(profileAlpha.Id, eventId, profileBeta.Id);

                    // Standard Strike One-Shot test
                    var battleRes2 = engine.ResolveRaidBattle(profileAlpha.Id, eventId, "Easy");
                    AssertTrue("Standard strike engaged successfully", battleRes2.Success);
                    AssertEquals("Standard strike consumed dynamic AP (10)", 140, pAlpha.AttackPowerCurrent);
                    AssertEquals("Combat resolved with legendary One-Shot Victory type", 1, battleRes2.VictoryType == "OneShot" ? 1 : 0);

                    var statePostOneShot = engine.GetRaidState(profileAlpha.Id, eventId);
                    AssertTrue("One-shot triggers boss target regeneration", !statePostOneShot.EasyTarget.IsInitialized || statePostOneShot.EasyTarget.MainHpCurrent > 500);
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

        // Mini extension helper for boolean checking in test flow
        private static bool RepeatCheck(this ClaimMilestoneResult claim, ClaimMilestoneResult claim2)
        {
            return claim.Success;
        }
    }
}
