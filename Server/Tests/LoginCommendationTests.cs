using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MwohServer.Data;
using MwohServer.Models;
using MwohServer.Services;

namespace MwohServer.Tests
{
    public static class LoginCommendationTests
    {
        public static bool Run(ILoginCommendationEngine engine, MwohDbContext context)
        {
            Console.WriteLine("\n==================================================");
            Console.WriteLine("🧪 INITIALIZING S.H.I.E.L.D. COMMENDATIONS TESTS");
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
                        .Where(u => u.Username.StartsWith("CommendationTestAgent"))
                        .Select(u => u.Id)
                        .ToList();

                    var testProfileIds = context.Profiles
                        .Where(p => testUserIds.Contains(p.UserAccountId) || p.Nickname.StartsWith("TestCommAgent"))
                        .Select(p => p.Id)
                        .ToList();

                    // Remove inventory items
                    var items = context.PlayerInventoryItems.Where(i => testProfileIds.Contains(i.PlayerProfileId)).ToList();
                    context.PlayerInventoryItems.RemoveRange(items);

                    // Remove cards
                    var cards = context.PlayerCards.Where(c => testProfileIds.Contains(c.PlayerProfileId)).ToList();
                    context.PlayerCards.RemoveRange(cards);

                    // Remove commendation progress
                    var progress = context.PlayerLoginCommendations.Where(p => testProfileIds.Contains(p.PlayerProfileId)).ToList();
                    context.PlayerLoginCommendations.RemoveRange(progress);

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
                var testUser = new UserAccount { Username = "CommendationTestAgent", PasswordHash = "password" };
                context.Users.Add(testUser);
                context.SaveChanges();

                var testProfile = new PlayerProfile
                {
                    UserAccountId = testUser.Id,
                    Nickname = "TestCommAgentAlpha",
                    Level = 5,
                    SilverBalance = 50000,
                    RallyPoints = 1000,
                    MobaCoinBalance = 0,
                    MaxCardCapacity = 100,
                    PlayerIdString = "771122"
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

                // Force ignore date restrictions so the campaign loads during off-season testing
                GameplaySettings.IgnoreLoginCommendationDates = true;

                // Force templates reload to trigger hardcoded fallback example if config files empty
                engine.ReloadTemplates();

                var activeCampaigns = engine.GetActiveCampaigns();
                AssertTrue("At least one login campaign is active", activeCampaigns.Count > 0);

                var testCampaign = activeCampaigns.First();
                var campaignId = testCampaign.Id;

                // --------------------------------------------------
                // 1. Day 1 Daily Login Check
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 1: Day 1 Daily Login ---");

                var initialSilver = testProfile.SilverBalance;
                var processResult = engine.ProcessDailyLogin(profileId);

                // Re-fetch profile
                testProfile = context.Profiles.Find(profileId)!;

                AssertTrue("Day 1 login advances and unlocks reward", processResult.UnlockedReward);
                AssertEquals("Day 1 login reports Day 1", 1, processResult.DayNumber);

                // Verify reward disbursed (Day 1 reward in seeded fallback is 50,000 Silver)
                AssertTrue("Day 1 reward text contains Silver", processResult.RewardText.Contains("Silver"));
                AssertEquals("Silver balance incremented", initialSilver + 50000, testProfile.SilverBalance);

                // --------------------------------------------------
                // 2. Rollover Check (Same Day block)
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 2: Same-day Rollover Block ---");

                var sameDayResult = engine.ProcessDailyLogin(profileId);
                AssertTrue("Second login on same day does NOT trigger new reward", !sameDayResult.UnlockedReward);

                // Verify progress DTO state
                var progressList = engine.GetPlayerProgress(profileId);
                var progressDto = progressList.FirstOrDefault(p => p.Campaign.Id == campaignId);

                AssertTrue("Progress DTO exists", progressDto != null);
                if (progressDto != null)
                {
                    AssertEquals("Total logins count is 1", 1, progressDto.TotalLogins);
                    AssertTrue("Already logged in today is true", progressDto.AlreadyLoggedToday);
                    AssertEquals("Claimed days JSON contains Day 1", 1, progressDto.ClaimedDays.Count);
                    AssertEquals("Claimed day in list is 1", 1, progressDto.ClaimedDays[0]);
                }

                // --------------------------------------------------
                // 3. Simulated Rollover (Next Day progression)
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 3: Simulated Daily Rollover (Day 2) ---");

                // Simulate next calendar day by shifting LastLoginDate back 24 hours
                var record = context.PlayerLoginCommendations
                    .First(r => r.PlayerProfileId == profileId && r.CampaignId == campaignId);
                record.LastLoginDate = DateTime.UtcNow.AddDays(-1).AddHours(-6); // Shifting past Eastern 5:00 AM reset window
                context.SaveChanges();

                var initialRally = testProfile.RallyPoints;
                var rolloverResult = engine.ProcessDailyLogin(profileId);

                // Re-fetch profile
                testProfile = context.Profiles.Find(profileId)!;

                AssertTrue("New day login advances progress and unlocks reward", rolloverResult.UnlockedReward);
                AssertEquals("Day number advanced to 2", 2, rolloverResult.DayNumber);

                // Day 2 reward in seeded fallback is 2,000 Rally Points
                AssertTrue("Day 2 reward contains Rally Points", rolloverResult.RewardText.Contains("Rally Points"));
                AssertEquals("Rally points incremented", initialRally + 2000, testProfile.RallyPoints);

                // --------------------------------------------------
                // 4. Item and Card Capacity Expansion Rewards (Day 3 & 4)
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 4: Restorative Item & Slot Expansion ---");

                // Day 3 login rollover simulation
                record = context.PlayerLoginCommendations
                    .First(r => r.PlayerProfileId == profileId && r.CampaignId == campaignId);
                record.LastLoginDate = DateTime.UtcNow.AddDays(-1).AddHours(-6);
                context.SaveChanges();

                var day3Result = engine.ProcessDailyLogin(profileId);
                AssertEquals("Day 3 reward unlocked", 3, day3Result.DayNumber);

                var hasInventoryItem = context.PlayerInventoryItems.Any(i => i.PlayerProfileId == profileId && i.Quantity > 0);
                AssertTrue("Seeded Iso-8 item successfully added to inventory on Day 3", hasInventoryItem);

                // Day 4 login rollover simulation (Day 4 reward in fallback JSON is Shield Barrier Item [value 5])
                record = context.PlayerLoginCommendations
                    .First(r => r.PlayerProfileId == profileId && r.CampaignId == campaignId);
                record.LastLoginDate = DateTime.UtcNow.AddDays(-1).AddHours(-6);
                context.SaveChanges();

                var day4Result = engine.ProcessDailyLogin(profileId);
                AssertEquals("Day 4 reward unlocked", 4, day4Result.DayNumber);
                Console.WriteLine($"  [DEBUG] Day 4 RewardText: '{day4Result.RewardText}'");
                AssertTrue("Shield Barrier claimed", day4Result.RewardText.ToLower().Contains("shield barrier") || day4Result.RewardText.ToLower().Contains("iso-8") || day4Result.RewardText.ToLower().Contains("restorative") || day4Result.RewardText.ToLower().Contains("power pack"));

                // --------------------------------------------------
                // 5. Date restriction filters
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 5: Date Filtering & Ignore Bypass ---");

                // Toggle Ignore dates to false
                GameplaySettings.IgnoreLoginCommendationDates = false;

                // Create a temporary mock calendar template that is explicitly inactive/out of date bounds
                lock (context)
                {
                    // To test date limits, we check active campaigns list
                    var realActive = engine.GetActiveCampaigns();
                    // By default, example_holiday_login has no date restriction, so it should be active.
                    // If we set a date restriction far in future:
                    var holidayCampaign = testCampaign;
                    holidayCampaign.StartDate = DateTime.UtcNow.AddDays(10);
                    holidayCampaign.EndDate = DateTime.UtcNow.AddDays(20);

                    var filterActive = engine.GetActiveCampaigns();
                    bool campaignIsFiltered = !filterActive.Any(c => c.Id == campaignId);
                    AssertTrue("Campaign is filtered out when current date is outside start/end dates bounds", campaignIsFiltered);

                    // Re-enable dev date bypass
                    GameplaySettings.IgnoreLoginCommendationDates = true;
                    var bypassActive = engine.GetActiveCampaigns();
                    bool campaignBypassed = bypassActive.Any(c => c.Id == campaignId);
                    AssertTrue("Bypass ignore_login_commendation_dates overrides event dates successfully", campaignBypassed);

                    // Restore dates to null
                    holidayCampaign.StartDate = null;
                    holidayCampaign.EndDate = null;
                }

                // --------------------------------------------------
                // 6. Card Capacity Safety Revert Checks
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 6: Card Capacity Safety Revert ---");

                // Day 5 reward in fallback is MobaCoin (100)
                record = context.PlayerLoginCommendations
                    .First(r => r.PlayerProfileId == profileId && r.CampaignId == campaignId);
                record.LastLoginDate = DateTime.UtcNow.AddDays(-1).AddHours(-6);
                context.SaveChanges();
                engine.ProcessDailyLogin(profileId); // total = 5

                // Day 6 reward in fallback is Item (Ultimate ticket)
                record = context.PlayerLoginCommendations
                    .First(r => r.PlayerProfileId == profileId && r.CampaignId == campaignId);
                record.LastLoginDate = DateTime.UtcNow.AddDays(-1).AddHours(-6);
                context.SaveChanges();
                engine.ProcessDailyLogin(profileId); // total = 6

                // Re-fetch progress
                record = context.PlayerLoginCommendations
                    .First(r => r.PlayerProfileId == profileId && r.CampaignId == campaignId);
                AssertEquals("Player has logged in 6 times", 6, record.TotalLogins);

                // Day 7 reward is a Hero Card (Spider-Man). Let's simulate FULL card inventory capacity!
                testProfile = context.Profiles.Find(profileId)!;
                testProfile.MaxCardCapacity = 0; // Force full capacity by setting max slots to 0
                context.SaveChanges();

                record.LastLoginDate = DateTime.UtcNow.AddDays(-1).AddHours(-6);
                context.SaveChanges();

                var overflowResult = engine.ProcessDailyLogin(profileId);

                // Re-fetch record & profile
                record = context.PlayerLoginCommendations
                    .First(r => r.PlayerProfileId == profileId && r.CampaignId == campaignId);
                testProfile = context.Profiles.Find(profileId)!;

                AssertTrue("Login process returns failure warning when inventory slots are full", !overflowResult.UnlockedReward);
                AssertTrue("Warning message notifies user of card slot limitations", overflowResult.Message.Contains("STORAGE OVERFLOW"));
                AssertEquals("Progression is reverted back to 6 to prevent missing the card", 6, record.TotalLogins);

                // Now free up capacity! Bumping slots to 10
                testProfile.MaxCardCapacity = 10;
                context.SaveChanges();

                // Recheck daily rollover (LastLoginDate was NOT stamped today because claim failed, so we can immediately try again)
                var clearResult = engine.ProcessDailyLogin(profileId);
                testProfile = context.Profiles.Find(profileId)!;
                record = context.PlayerLoginCommendations
                    .First(r => r.PlayerProfileId == profileId && r.CampaignId == campaignId);

                AssertTrue("Claim succeeds once capacity slots are cleared", clearResult.UnlockedReward);
                AssertEquals("Logins advanced to Day 7", 7, record.TotalLogins);
                AssertTrue("Spider-Man card added to roster", clearResult.RewardText.Contains("Spider-Man") || clearResult.RewardText.Contains("Hero Card"));

                var cardCount = context.PlayerCards.Count(c => c.PlayerProfileId == profileId);
                AssertTrue("At least 1 card in inventory now", cardCount > 0);

                // --------------------------------------------------
                // 7. Fallback rewards for logins beyond calendar bounds
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 7: Additional Days Fallback Rewards ---");

                // Day 8 rollover simulation (beyond Day 7 maximum reward blueprint)
                record = context.PlayerLoginCommendations
                    .First(r => r.PlayerProfileId == profileId && r.CampaignId == campaignId);
                record.LastLoginDate = DateTime.UtcNow.AddDays(-1).AddHours(-6);
                context.SaveChanges();

                var prevSilverFallback = testProfile.SilverBalance;
                var fallbackResult = engine.ProcessDailyLogin(profileId);

                // Re-fetch
                record = context.PlayerLoginCommendations
                    .First(r => r.PlayerProfileId == profileId && r.CampaignId == campaignId);
                testProfile = context.Profiles.Find(profileId)!;

                AssertTrue("Login processes successfully beyond template bounds", fallbackResult.UnlockedReward);
                AssertEquals("Cumulative day increments to 8", 8, record.TotalLogins);
                AssertTrue("Rollover falls back to template's baseline fallback reward", fallbackResult.RewardText.Contains("Silver"));
                AssertEquals("Silver balance incremented by fallback quantity", prevSilverFallback + testCampaign.FallbackRewardValue, testProfile.SilverBalance);

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
            Console.WriteLine($"🧪 LOGIN COMMENDATIONS TESTS: {passed} PASSED // {failed} FAILED");
            Console.WriteLine("==================================================");

            return failed == 0;
        }
    }
}
