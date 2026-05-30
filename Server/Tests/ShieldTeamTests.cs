using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using MwohServer.Data;
using MwohServer.Models;
using MwohServer.Services;

namespace MwohServer.Tests
{
    public static class ShieldTeamTests
    {
        public static bool Run(IShieldTeamEngine engine, MwohDbContext context)
        {
            Console.WriteLine("\n==================================================");
            Console.WriteLine("🧪 INITIALIZING S.H.I.E.L.D. TEAM ENGINE TESTS");
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
                // Setup unique profiles for testing
                var user1 = new UserAccount { Username = "shield_agent1", PasswordHash = "hash" };
                var user2 = new UserAccount { Username = "shield_agent2", PasswordHash = "hash" };
                var user3 = new UserAccount { Username = "shield_agent3", PasswordHash = "hash" };
                context.Users.AddRange(user1, user2, user3);
                context.SaveChanges();

                var agent1 = new PlayerProfile
                {
                    UserAccountId = user1.Id,
                    Nickname = "AgentAlpha",
                    Level = 1,
                    EnergyMax = 100,
                    EnergyCurrent = 100,
                    AttackPower = 10,
                    DefensePower = 10,
                    RallyPoints = 0,
                    StatPoints = 0
                };
                var agent2 = new PlayerProfile
                {
                    UserAccountId = user2.Id,
                    Nickname = "AgentBeta",
                    Level = 1,
                    EnergyMax = 100,
                    EnergyCurrent = 100,
                    AttackPower = 10,
                    DefensePower = 10,
                    RallyPoints = 0,
                    StatPoints = 0
                };
                var agent3 = new PlayerProfile
                {
                    UserAccountId = user3.Id,
                    Nickname = "AgentGamma",
                    Level = 1,
                    EnergyMax = 100,
                    EnergyCurrent = 100,
                    AttackPower = 10,
                    DefensePower = 10,
                    RallyPoints = 0,
                    StatPoints = 0
                };
                context.Profiles.AddRange(agent1, agent2, agent3);
                context.SaveChanges();

                // --------------------------------------------------
                // 1. Rally Points Verification (Stranger)
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 1: Stranger Agent Rally Points ---");
                var r1 = engine.Rally(agent1.Id, agent2.Id);
                AssertTrue("Stranger Rally succeeds", r1.Success);
                AssertEquals("Sender gets +10 Rally Points", 10, agent1.RallyPoints);
                AssertEquals("Receiver gets +5 Rally Points", 5, agent2.RallyPoints);

                // --------------------------------------------------
                // 2. Rally Cooldown Verification
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 2: Agent Rally Cooldown ---");
                var r2 = engine.Rally(agent1.Id, agent2.Id);
                AssertTrue("Consecutive Stranger Rally fails", !r2.Success);
                AssertTrue("Correct cooldown message returned", r2.Message.Contains("Cooldown active"));

                // --------------------------------------------------
                // 3. Team Proposal & Capacity Guards
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 3: S.H.I.E.L.D. Team Proposal Capacity guards ---");
                
                // Proposal to self
                var p1 = engine.Propose(agent1.Id, agent1.Id);
                AssertTrue("Cannot propose team membership to self", !p1.Success);

                // Normal proposal
                var p2 = engine.Propose(agent1.Id, agent2.Id);
                AssertTrue("Team Proposal succeeds", p2.Success);

                // Duplicate proposal check
                var p3 = engine.Propose(agent1.Id, agent2.Id);
                AssertTrue("Duplicate Team Proposal fails", !p3.Success);

                // Max Capacity check (At level 1, limit is 5)
                // Seed 5 other accepted members for agent1
                for (int i = 0; i < 5; i++)
                {
                    var dummyUser = new UserAccount { Username = $"dummy_{i}", PasswordHash = "hash" };
                    context.Users.Add(dummyUser);
                    context.SaveChanges();

                    var dummyProfile = new PlayerProfile { UserAccountId = dummyUser.Id, Nickname = $"DummyAgent_{i}", Level = 1 };
                    context.Profiles.Add(dummyProfile);
                    context.SaveChanges();

                    var friendRelation = new ShieldTeamMember
                    {
                        ProfileId = agent1.Id,
                        MemberProfileId = dummyProfile.Id,
                        Status = "Accepted",
                        CreatedAt = DateTime.UtcNow
                    };
                    context.ShieldTeamMembers.Add(friendRelation);
                }
                context.SaveChanges();

                var p4 = engine.Propose(agent1.Id, agent3.Id);
                AssertTrue("Proposal blocked when sender capacity (5/5) is exceeded", !p4.Success);
                AssertTrue("Correct capacity error message", p4.Message.Contains("capacity has reached maximum limits"));

                // Clean up dummy relations/profiles to restore state
                var dummies = context.Profiles.Where(p => p.Nickname.StartsWith("DummyAgent_")).ToList();
                var dummyUserIds = dummies.Select(p => p.UserAccountId).ToList();
                var dummyUsers = context.Users.Where(u => dummyUserIds.Contains(u.Id)).ToList();
                var dummyRelations = context.ShieldTeamMembers.Where(m => m.ProfileId == agent1.Id && m.Status == "Accepted").ToList();

                context.ShieldTeamMembers.RemoveRange(dummyRelations);
                context.Profiles.RemoveRange(dummies);
                context.Users.RemoveRange(dummyUsers);
                context.SaveChanges();

                // --------------------------------------------------
                // 4. Acceptance & mutual point allocation
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 4: Mutual Acceptance Points ---");
                var initialAlphaStats = agent1.StatPoints;
                var initialBetaStats = agent2.StatPoints;

                var acceptResult = engine.Accept(agent2.Id, agent1.Id);
                AssertTrue("Mutual team proposal accepted", acceptResult.Success);
                AssertEquals("Proposer receives 5 Attribute points", initialAlphaStats + 5, agent1.StatPoints);
                AssertEquals("Acceptor receives 5 Attribute points", initialBetaStats + 5, agent2.StatPoints);

                // Verify they are now teammates in DB
                var teamRel = context.ShieldTeamMembers.FirstOrDefault(m => m.ProfileId == agent1.Id && m.MemberProfileId == agent2.Id);
                AssertTrue("Relationship status updated to Accepted", teamRel != null && teamRel.Status == "Accepted");

                // --------------------------------------------------
                // 5. Team Member Rally Points
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 5: Team Member Rally Points ---");
                // Clean up the cooldown rally log to allow rally
                var activeRallyLogs = context.RallyLogs.Where(l => l.SenderProfileId == agent1.Id && l.ReceiverProfileId == agent2.Id).ToList();
                context.RallyLogs.RemoveRange(activeRallyLogs);
                context.SaveChanges();

                var r3 = engine.Rally(agent1.Id, agent2.Id);
                AssertTrue("Teammate Rally succeeds", r3.Success);
                // Gained +20 points (Teammate bonus)
                AssertEquals("Sender gets +20 Rally Points", 30, agent1.RallyPoints);
                AssertEquals("Receiver gets +10 Rally Points", 15, agent2.RallyPoints);

                // --------------------------------------------------
                // 6. Dismissal Attribute Decrement & Cooldown Penalties
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 6: Dismissal Penalties & Stat Priority ---");

                // Set attacker profile parameters to specific levels for decrement checks
                // Setup: EnergyMax=100, AttackPower=10, DefensePower=10
                // Highest stat is EnergyMax (100) -> should be decremented first.
                agent1.EnergyMax = 100;
                agent1.AttackPower = 10;
                agent1.DefensePower = 10;

                agent2.EnergyMax = 100;
                agent2.AttackPower = 10;
                agent2.DefensePower = 10;
                context.SaveChanges();

                GameplaySettings.EnableFriendRemoval24HourPenalty = true;

                var removeResult1 = engine.Remove(agent1.Id, agent2.Id);
                AssertTrue("Dismissal succeeds and removes teammate", removeResult1.Success);
                
                // Agent1 dismissed Agent2:
                // Normal point penalty is 5 points.
                // EnergyMax was 100 (highest), decremented by 5 -> should be 95.
                AssertEquals("Dismisser EnergyMax decremented by 5 points (100 -> 95)", 95, agent1.EnergyMax);
                AssertEquals("Dismissed Agent EnergyMax decremented by 5 points (100 -> 95)", 95, agent2.EnergyMax);

                // Try subsequent removal (within 24 hours)
                // We'll create another friend relationship first
                var propAccept = new ShieldTeamMember { ProfileId = agent1.Id, MemberProfileId = agent3.Id, Status = "Accepted", CreatedAt = DateTime.UtcNow };
                context.ShieldTeamMembers.Add(propAccept);
                context.SaveChanges();

                // Setup stats for subsequent removal:
                // agent1: EnergyMax = 95, AttackPower = 10, DefensePower = 10.
                // EnergyMax (95) is still highest. Subsequent removal penalty is 6 points.
                var removeResult2 = engine.Remove(agent1.Id, agent3.Id);
                AssertTrue("Subsequent dismissal succeeds", removeResult2.Success);
                AssertTrue("Penalty alert correctly triggered in message", removeResult2.Message.Contains("DOUBLE DISMISSAL PENALTY ENFORCED"));

                // Decremented 6 points from agent1 EnergyMax (95 -> 89)
                AssertEquals("Dismisser EnergyMax decremented by 6 points on double dismissal penalty (95 -> 89)", 89, agent1.EnergyMax);
                // Decremented exactly 5 points from dismissed agent3 EnergyMax (100 -> 95)
                AssertEquals("Dismissed Agent3 EnergyMax decremented by exactly 5 points (100 -> 95)", 95, agent3.EnergyMax);

                // Clean up test data
                var testRels = context.ShieldTeamMembers.Where(m => m.ProfileId == agent1.Id || m.MemberProfileId == agent1.Id).ToList();
                var testRallies = context.RallyLogs.Where(l => l.SenderProfileId == agent1.Id || l.ReceiverProfileId == agent1.Id).ToList();
                context.ShieldTeamMembers.RemoveRange(testRels);
                context.RallyLogs.RemoveRange(testRallies);
                context.Profiles.RemoveRange(agent1, agent2, agent3);
                context.Users.RemoveRange(user1, user2, user3);
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
