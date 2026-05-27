using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using MwohServer.Data;
using MwohServer.Models;
using MwohServer.Services;

namespace MwohServer.Tests
{
    public static class AllianceEngineTests
    {
        public static bool Run(IAllianceEngine allianceEngine, MwohDbContext context)
        {
            Console.WriteLine("\n==================================================");
            Console.WriteLine("🧪 INITIALIZING S.H.I.E.L.D. ALLIANCE TEST SUITE");
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
                        .Where(u => u.Username.StartsWith("CadetAgentUser") || 
                                    u.Username.StartsWith("EliteAgentUser") || 
                                    u.Username.StartsWith("AllyAgentUser"))
                        .Select(u => u.Id)
                        .ToList();

                    var testProfileIds = context.Profiles
                        .Where(p => testUserIds.Contains(p.UserAccountId) ||
                                    p.Nickname.StartsWith("CadetAgent") ||
                                    p.Nickname.StartsWith("EliteAgent") ||
                                    p.Nickname.StartsWith("AllyAgent"))
                        .Select(p => p.Id)
                        .ToList();

                    var profiles = context.Profiles.Where(p => testProfileIds.Contains(p.Id)).ToList();
                    foreach (var p in profiles)
                    {
                        p.AllianceId = null;
                        p.AllianceRole = null;
                    }
                    context.SaveChanges();

                    var reqs = context.AllianceJoinRequests.Where(r => testProfileIds.Contains(r.PlayerProfileId)).ToList();
                    context.AllianceJoinRequests.RemoveRange(reqs);

                    var items = context.PlayerInventoryItems.Where(pi => testProfileIds.Contains(pi.PlayerProfileId)).ToList();
                    context.PlayerInventoryItems.RemoveRange(items);

                    var teamMembers = context.ShieldTeamMembers.Where(t => testProfileIds.Contains(t.ProfileId) || testProfileIds.Contains(t.MemberProfileId)).ToList();
                    context.ShieldTeamMembers.RemoveRange(teamMembers);

                    var alliances = context.Alliances.Where(a => testProfileIds.Contains(a.LeaderProfileId) || a.Name == "COMMISSIONED-COALITION" || a.Name == "LOW-LEVEL-DIVISION" || a.Name == "NO-ALLIES-DIVISION").ToList();
                    context.Alliances.RemoveRange(alliances);
                    context.SaveChanges();

                    profiles = context.Profiles.Where(p => testProfileIds.Contains(p.Id)).ToList();
                    context.Profiles.RemoveRange(profiles);
                    context.SaveChanges();

                    var users = context.Users.Where(u => testUserIds.Contains(u.Id)).ToList();
                    context.Users.RemoveRange(users);
                    context.SaveChanges();
                }

                // Initial cleanup of any residual dirty state
                CleanupTestData();

                int lowLvlProfileId = 0;
                int eliteProfileId = 0;
                int recruitProfileId = 0;

                // --------------------------------------------------
                // 1. Formation Guards Tests
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 1: Alliance Formation Constraints ---");

                // Case A: Level under 20
                var lowLvlUser = new UserAccount { Username = "CadetAgentUser", PasswordHash = "password" };
                context.Users.Add(lowLvlUser);
                context.SaveChanges();

                var lowLvlProfile = new PlayerProfile
                {
                    UserAccountId = lowLvlUser.Id,
                    Nickname = "CadetAgent",
                    Level = 15,
                    SilverBalance = 100000,
                    PlayerIdString = "999991",
                    SessionId = "session_991"
                };
                context.Profiles.Add(lowLvlProfile);
                context.SaveChanges();
                lowLvlProfileId = lowLvlProfile.Id;

                var res1 = allianceEngine.CreateAlliance(lowLvlProfileId, "LOW-LEVEL-DIVISION", "Assemble!");
                AssertTrue("Creation fails if Clearances Level < 20", !res1.Success);

                // Case B: Lacks 10 allies
                var eligibleLvlUser = new UserAccount { Username = "EliteAgentUser", PasswordHash = "password" };
                context.Users.Add(eligibleLvlUser);
                context.SaveChanges();

                var eligibleLvlProfile = new PlayerProfile
                {
                    UserAccountId = eligibleLvlUser.Id,
                    Nickname = "EliteAgent",
                    Level = 25,
                    SilverBalance = 100000,
                    PlayerIdString = "999992",
                    SessionId = "session_992"
                };
                context.Profiles.Add(eligibleLvlProfile);
                context.SaveChanges();
                eliteProfileId = eligibleLvlProfile.Id;

                var res2 = allianceEngine.CreateAlliance(eliteProfileId, "NO-ALLIES-DIVISION", "Assemble!");
                AssertTrue("Creation fails if accepted S.H.I.E.L.D. allies count < 10", !res2.Success);

                // Add 10 allies for EliteAgent (eliteProfileId)
                for (int i = 1; i <= 10; i++)
                {
                    var allyUser = new UserAccount { Username = $"AllyAgentUser{i}", PasswordHash = "password" };
                    context.Users.Add(allyUser);
                    context.SaveChanges();

                    var ally = new PlayerProfile
                    {
                        UserAccountId = allyUser.Id,
                        Nickname = $"AllyAgent{i}",
                        Level = 20,
                        SilverBalance = 50000,
                        PlayerIdString = $"99900{i}"
                    };
                    context.Profiles.Add(ally);
                    context.SaveChanges();

                    if (i == 1)
                    {
                        recruitProfileId = ally.Id;
                    }

                    var relation = new ShieldTeamMember
                    {
                        ProfileId = eliteProfileId,
                        MemberProfileId = ally.Id,
                        Status = "Accepted",
                        CreatedAt = DateTime.UtcNow
                    };
                    context.ShieldTeamMembers.Add(relation);
                }
                context.SaveChanges();

                // Case C: Success Creation
                var res3 = allianceEngine.CreateAlliance(eliteProfileId, "COMMISSIONED-COALITION", "Strategic Operations.");
                AssertTrue("Creation succeeds with clearance level 20+ and 10+ allies", res3.Success);
                AssertEquals("Founder becomes Leader", 1, res3.Alliance != null && res3.Alliance.LeaderProfileId == eliteProfileId ? 1 : 0);

                var allianceId = res3.Alliance?.Id ?? 0;

                // --------------------------------------------------
                // 2. Donation & Rating Progression Tests
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 2: Donations & Rating Progression ---");

                // Silver Contribution
                var res4 = allianceEngine.DonateSilver(eliteProfileId, 15000);
                AssertTrue("Silver donation successful", res4.Success);
                AssertEquals("Rating equals total silver / 1000 (15000 / 1000 = 15)", 15, res4.NewAllianceRating);
                AssertEquals("Alliance level increases to 3 (requires 15 rating)", 3, res4.NewAllianceLevel);

                // Resource drop Contribution (Seed Storm's Cape and donate)
                var stormItem = context.ItemTemplates.FirstOrDefault(it => it.Name == "Storm's Cape Red");
                if (stormItem == null)
                {
                    stormItem = new ItemTemplate
                    {
                        Id = 9999,
                        Name = "Storm's Cape Red",
                        Description = "Classified Storm resource.",
                        Type = "Resource",
                        EffectValue = 2000
                    };
                    context.ItemTemplates.Add(stormItem);
                    context.SaveChanges();
                }

                var playerItem = new PlayerInventoryItem
                {
                    PlayerProfileId = eliteProfileId,
                    ItemTemplateId = stormItem.Id,
                    Quantity = 5 // 5 * 2000 = 10,000 Silver value
                };
                context.PlayerInventoryItems.Add(playerItem);
                context.SaveChanges();

                var res5 = allianceEngine.DonateResourceGroup(eliteProfileId, "StormsCape");
                AssertTrue("Resource drop donation successful", res5.Success);
                AssertEquals("Bank balance credited (+10,000 silver)", 25000, res5.NewAllianceDonatedSilver);
                AssertEquals("Rating increases from 15 to 25", 25, res5.NewAllianceRating);

                // --------------------------------------------------
                // 3. Command Role Assignments Tests
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 3: Roster Role Delegations ---");

                var recruitProfile = context.Profiles.FirstOrDefault(p => p.Id == recruitProfileId);
                AssertTrue("Recruit exists in database", recruitProfile != null);

                // Member requests to join
                bool reqCreated = allianceEngine.CreateJoinRequest(recruitProfileId, allianceId);
                AssertTrue("Join request successfully submitted to Commissioned Division", reqCreated);

                var pendingReq = context.AllianceJoinRequests.FirstOrDefault(r => r.AllianceId == allianceId && r.PlayerProfileId == recruitProfileId && r.Status == "Pending");
                AssertTrue("Join request successfully logged in database", pendingReq != null);

                // Leader accepts request
                bool accepted = allianceEngine.ProcessJoinRequest(eliteProfileId, pendingReq!.Id, true);
                AssertTrue("Leader accepts recruit join request successfully", accepted);

                // Leader promotes recruit to Offense Leader
                bool roleAssigned = allianceEngine.AssignMemberRole(eliteProfileId, recruitProfileId, "Offense-Leader");
                AssertTrue("Leader successfully delegates Offense Leader role to recruit", roleAssigned);
                AssertEquals("Recruit role updated to Offense-Leader", 1, recruitProfile!.AllianceRole == "Offense-Leader" ? 1 : 0);

                // --------------------------------------------------
                // 4. Combat Modifiers Calculation Tests
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 4: Battle Modifiers Calculations ---");

                // Purchase adaptor upgrade (Speed adaptor level 1)
                var res6 = allianceEngine.PurchaseUpgrade(eliteProfileId, "SpeedAdaptor"); // Wait, does the alliance bank have enough silver?
                // Alliance bank donated silver has 25,000 donated from player, but costs 3,000,000 silver!
                // Let's cheat and add direct silver to alliance bank for testing upgrades
                var allianceInDb = context.Alliances.FirstOrDefault(a => a.Id == allianceId);
                if (allianceInDb != null)
                {
                    allianceInDb.DonatedSilver = 5000000;
                    context.SaveChanges();
                }

                var res7 = allianceEngine.PurchaseUpgrade(eliteProfileId, "SpeedAdaptor");
                AssertTrue("Purchase Speed Adaptor Lv 1 successful (costs 3M)", res7.Success);

                // Build Protection Wall segment
                var res8 = allianceEngine.PurchaseUpgrade(eliteProfileId, "ProtectionWall");
                AssertTrue("Purchase Protection Wall segment 1 successful (costs 50K)", res8.Success);

                // Attacker (Leader eliteProfileId) combat boosts for Bruiser Alignment Card
                var leaderBruiserBoost = allianceEngine.GetAllianceCombatBoosts(eliteProfileId, "Bruiser");
                // Expected Atk boost: 1.10 (Leader 10% ATK)
                // Expected Def boost: 1.10 (Leader 10% DEF) + 1% (Protection Wall segment 1) = 1.11
                AssertEquals("Leader Bruiser Boost ATK Modifier is 1.10", 110, (long)Math.Round(leaderBruiserBoost.AtkModifier * 100));
                AssertEquals("Leader Bruiser Boost DEF Modifier is 1.11", 111, (long)Math.Round(leaderBruiserBoost.DefModifier * 100));

                // Attacker (Leader eliteProfileId) combat boosts for Speed Alignment Card (Speed adaptor level 1 active)
                var leaderSpeedBoost = allianceEngine.GetAllianceCombatBoosts(eliteProfileId, "Speed");
                // Expected Atk boost: 1.10 (Leader 10%) + 5% (Speed Adaptor Lv 1) = 1.15
                // Expected Def boost: 1.10 (Leader 10%) + 1% (Wall) + 5% (Speed Adaptor Lv 1) = 1.16
                AssertEquals("Leader Speed Boost ATK Modifier is 1.15", 115, (long)Math.Round(leaderSpeedBoost.AtkModifier * 100));
                AssertEquals("Leader Speed Boost DEF Modifier is 1.16", 116, (long)Math.Round(leaderSpeedBoost.DefModifier * 100));
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"💥 EXCEPTION OCCURRED DURING TESTS: {ex.Message}\n{ex.StackTrace}");
                Console.ResetColor();
                return false;
            }
            finally
            {
                try
                {
                    // Clean up database so it's fully isolated
                    var testUserIds = context.Users
                        .Where(u => u.Username.StartsWith("CadetAgentUser") || 
                                    u.Username.StartsWith("EliteAgentUser") || 
                                    u.Username.StartsWith("AllyAgentUser"))
                        .Select(u => u.Id)
                        .ToList();

                    var testProfileIds = context.Profiles
                        .Where(p => testUserIds.Contains(p.UserAccountId) ||
                                    p.Nickname.StartsWith("CadetAgent") ||
                                    p.Nickname.StartsWith("EliteAgent") ||
                                    p.Nickname.StartsWith("AllyAgent"))
                        .Select(p => p.Id)
                        .ToList();

                    var profiles = context.Profiles.Where(p => testProfileIds.Contains(p.Id)).ToList();
                    foreach (var p in profiles)
                    {
                        p.AllianceId = null;
                        p.AllianceRole = null;
                    }
                    context.SaveChanges();

                    var reqs = context.AllianceJoinRequests.Where(r => testProfileIds.Contains(r.PlayerProfileId)).ToList();
                    context.AllianceJoinRequests.RemoveRange(reqs);

                    var items = context.PlayerInventoryItems.Where(pi => testProfileIds.Contains(pi.PlayerProfileId)).ToList();
                    context.PlayerInventoryItems.RemoveRange(items);

                    var teamMembers = context.ShieldTeamMembers.Where(t => testProfileIds.Contains(t.ProfileId) || testProfileIds.Contains(t.MemberProfileId)).ToList();
                    context.ShieldTeamMembers.RemoveRange(teamMembers);

                    var alliances = context.Alliances.Where(a => testProfileIds.Contains(a.LeaderProfileId) || a.Name == "COMMISSIONED-COALITION" || a.Name == "LOW-LEVEL-DIVISION" || a.Name == "NO-ALLIES-DIVISION").ToList();
                    context.Alliances.RemoveRange(alliances);
                    context.SaveChanges();

                    profiles = context.Profiles.Where(p => testProfileIds.Contains(p.Id)).ToList();
                    context.Profiles.RemoveRange(profiles);
                    context.SaveChanges();

                    var users = context.Users.Where(u => testUserIds.Contains(u.Id)).ToList();
                    context.Users.RemoveRange(users);
                    context.SaveChanges();
                }
                catch (Exception cleanupEx)
                {
                    Console.WriteLine($"⚠️ Cleanup exception in finally: {cleanupEx.Message}");
                }
            }

            Console.WriteLine("==================================================");
            Console.WriteLine($"🧪 ALLIANCE ENGINE UNIT TESTS RESULTS: {passed} PASSED // {failed} FAILED");
            Console.WriteLine("==================================================");

            return failed == 0;
        }
    }
}
