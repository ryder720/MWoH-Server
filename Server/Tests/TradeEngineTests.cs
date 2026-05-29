using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using MwohServer.Data;
using MwohServer.Models;
using MwohServer.Services;

namespace MwohServer.Tests
{
    public static class TradeEngineTests
    {
        public static bool Run(ITradeEngine tradeEngine, MwohDbContext context)
        {
            Console.WriteLine("\n==================================================");
            Console.WriteLine("🧪 INITIALIZING S.H.I.E.L.D. TRADING TEST SUITE");
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

            void CleanupTestData()
            {
                var testUserIds = context.Users
                    .Where(u => u.Username.StartsWith("TradeTestAgent"))
                    .Select(u => u.Id)
                    .ToList();

                var testProfileIds = context.Profiles
                    .Where(p => testUserIds.Contains(p.UserAccountId) || p.Nickname.StartsWith("TradeAgent"))
                    .Select(p => p.Id)
                    .ToList();

                var trades = context.Trades.Where(t => testProfileIds.Contains(t.SenderProfileId) || testProfileIds.Contains(t.ReceiverProfileId)).ToList();
                context.Trades.RemoveRange(trades);

                var cards = context.PlayerCards.Where(pc => testProfileIds.Contains(pc.PlayerProfileId)).ToList();
                context.PlayerCards.RemoveRange(cards);

                var items = context.PlayerInventoryItems.Where(pi => testProfileIds.Contains(pi.PlayerProfileId)).ToList();
                context.PlayerInventoryItems.RemoveRange(items);

                var relations = context.ShieldTeamMembers.Where(m => testProfileIds.Contains(m.ProfileId) || testProfileIds.Contains(m.MemberProfileId)).ToList();
                context.ShieldTeamMembers.RemoveRange(relations);

                var profiles = context.Profiles.Where(p => testProfileIds.Contains(p.Id)).ToList();
                context.Profiles.RemoveRange(profiles);

                var users = context.Users.Where(u => testUserIds.Contains(u.Id)).ToList();
                context.Users.RemoveRange(users);

                context.SaveChanges();
            }

            try
            {
                CleanupTestData();

                // --------------------------------------------------
                // 1. Setup Operatives
                // --------------------------------------------------
                var userA = new UserAccount { Username = "TradeTestAgentA", PasswordHash = "pwd" };
                var userB = new UserAccount { Username = "TradeTestAgentB", PasswordHash = "pwd" };
                context.Users.AddRange(userA, userB);
                context.SaveChanges();

                var sender = new PlayerProfile
                {
                    UserAccountId = userA.Id,
                    Nickname = "TradeAgentA",
                    Level = 15,
                    SilverBalance = 50000,
                    PlayerIdString = "888881",
                    SessionId = "session_tr1"
                };

                var receiver = new PlayerProfile
                {
                    UserAccountId = userB.Id,
                    Nickname = "TradeAgentB",
                    Level = 15,
                    SilverBalance = 50000,
                    PlayerIdString = "888882",
                    SessionId = "session_tr2"
                };

                context.Profiles.AddRange(sender, receiver);
                context.SaveChanges();

                // Seed Card Template & Player Cards
                var template = context.CardTemplates.FirstOrDefault();
                if (template == null)
                {
                    template = new CardTemplate
                    {
                        Title = "Mock Hero",
                        VisualTitle = "Mock_Hero",
                        Alignment = "Speed",
                        Rarity = "Rare",
                        PowerRequirement = 10
                    };
                    context.CardTemplates.Add(template);
                    context.SaveChanges();
                }

                var cardA = new PlayerCard { PlayerProfileId = sender.Id, CardTemplateId = template.Id, CurrentLevel = 1, IsInTrade = false };
                var cardB = new PlayerCard { PlayerProfileId = receiver.Id, CardTemplateId = template.Id, CurrentLevel = 1, IsInTrade = false };
                context.PlayerCards.AddRange(cardA, cardB);
                context.SaveChanges();

                // Seed Item Templates & Inventory Items
                var itemTemplate = context.ItemTemplates.FirstOrDefault();
                if (itemTemplate == null)
                {
                    itemTemplate = new ItemTemplate
                    {
                        Name = "Energy Iso-8",
                        Description = "Restores energy.",
                        Type = "EnergyRestorative"
                    };
                    context.ItemTemplates.Add(itemTemplate);
                    context.SaveChanges();
                }

                var invA = new PlayerInventoryItem { PlayerProfileId = sender.Id, ItemTemplateId = itemTemplate.Id, Quantity = 10 };
                var invB = new PlayerInventoryItem { PlayerProfileId = receiver.Id, ItemTemplateId = itemTemplate.Id, Quantity = 10 };
                context.PlayerInventoryItems.AddRange(invA, invB);
                context.SaveChanges();

                // --------------------------------------------------
                // 2. Clearance Level Guard Test
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 1: Clearance Level Safeguards ---");
                sender.Level = 5; // tradeMinLevel is 10
                context.SaveChanges();

                var el1 = tradeEngine.CheckTradingEligibility(sender.Id, receiver.Id);
                AssertTrue("Trade fails if sender clearance level < TradeMinLevel", !el1.Success);

                sender.Level = 15;
                context.SaveChanges();

                // --------------------------------------------------
                // 3. Co-Op Relationship Network Guards Tests
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 2: Relationship Guards ---");
                var el2 = tradeEngine.CheckTradingEligibility(sender.Id, receiver.Id);
                AssertTrue("Trade fails if operatives are not teammates/same alliance", !el2.Success);

                // Create a fresh friend connection (CreatedAt = Now)
                var friendRelation = new ShieldTeamMember
                {
                    ProfileId = sender.Id,
                    MemberProfileId = receiver.Id,
                    Status = "Accepted",
                    CreatedAt = DateTime.UtcNow
                };
                context.ShieldTeamMembers.Add(friendRelation);
                context.SaveChanges();

                // --------------------------------------------------
                // 4. Anti-Hacking Delay (Cooldown) Tests
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 3: Cooldown Network Constraints ---");
                GameplaySettings.TradeCooldownDays = 14; // restore default
                var el3 = tradeEngine.CheckTradingEligibility(sender.Id, receiver.Id);
                AssertTrue("Trade fails if teammate relationship duration < TradeCooldownDays", !el3.Success);

                // Config Bypass (Cooldown = 0)
                GameplaySettings.TradeCooldownDays = 0;
                var el4 = tradeEngine.CheckTradingEligibility(sender.Id, receiver.Id);
                AssertTrue("Trade succeeds if TradeCooldownDays setting is bypassed (0)", el4.Success);

                // --------------------------------------------------
                // 5. Volume Limits Guard Test
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 4: Consignment Volume Safeguards ---");
                // Try to offer 4 cards (limit is 3)
                var cardListExceeded = new List<int> { cardA.Id, cardA.Id, cardA.Id, cardA.Id };
                var propExceeded = tradeEngine.ProposeTrade(
                    sender.Id, receiver.Id, 1000, 0,
                    cardListExceeded, new List<int>(), new List<TradeItemOffer>(), new List<TradeItemOffer>());
                AssertTrue("Propose fails if card count exceeds TradeMaxCards (default 3)", !propExceeded.Success);

                // --------------------------------------------------
                // 6. Assets Locking and Double Spending Safeguard Tests
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 5: Asset Lockdowns & Proposal State ---");
                var offeredCards = new List<int> { cardA.Id };
                var requestedCards = new List<int> { cardB.Id };
                var offeredItems = new List<TradeItemOffer> { new TradeItemOffer { ItemTemplateId = itemTemplate.Id, Quantity = 3 } };
                var requestedItems = new List<TradeItemOffer> { new TradeItemOffer { ItemTemplateId = itemTemplate.Id, Quantity = 2 } };

                var propRes = tradeEngine.ProposeTrade(
                    sender.Id, receiver.Id, 5000, 2000,
                    offeredCards, requestedCards, offeredItems, requestedItems);

                AssertTrue("Propose trade succeeds with valid assets, deducts instantly", propRes.Success);
                AssertEquals("Offered card IsInTrade locked to true", 1, cardA.IsInTrade ? 1 : 0);
                AssertEquals("Sender Silver balance deducted (-5000)", 45000, sender.SilverBalance);
                AssertEquals("Sender inventory item deducted (-3)", 7, invA.Quantity);

                var trade = context.Trades.FirstOrDefault(t => t.SenderProfileId == sender.Id && t.Status == "Pending");
                AssertTrue("Pending trade proposal successfully logged in database", trade != null);

                // Double proposal guard
                var doubleProp = tradeEngine.ProposeTrade(
                    sender.Id, receiver.Id, 1000, 0,
                    offeredCards, new List<int>(), new List<TradeItemOffer>(), new List<TradeItemOffer>());
                AssertTrue("Cannot offer a card that is already IsInTrade locked", !doubleProp.Success);

                // --------------------------------------------------
                // 7. Atomic Authorization (Accept Trade) Tests
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 6: Exchange Execution (Accept) ---");
                var acceptRes = tradeEngine.AcceptTrade(trade!.Id, receiver.Id);
                AssertTrue("Accepting trade proposal completes transaction atomically", acceptRes.Success);

                // Verify Ownership shifted
                AssertEquals("Offered Card ownership shifted to Receiver B", receiver.Id, cardA.PlayerProfileId);
                AssertEquals("Requested Card ownership shifted to Sender A", sender.Id, cardB.PlayerProfileId);

                // Verify Locks Released
                AssertEquals("Offered card unlocked IsInTrade", 0, cardA.IsInTrade ? 1 : 0);
                AssertEquals("Requested card unlocked IsInTrade", 0, cardB.IsInTrade ? 1 : 0);

                // Verify Silver credit shifts
                // Sender had 45k, receiver had 50k
                // Sender offered 5k (already deducted from sender). Receiver receives +5k offered.
                // Receiver had requested 2k from sender. Sender balance +2k, Receiver balance -2k.
                // Final Expected: Sender A = 45k + 2k = 47k. Receiver B = 50k + 5k - 2k = 53k.
                AssertEquals("Sender final Silver balance matches (47,000)", 47000, sender.SilverBalance);
                AssertEquals("Receiver final Silver balance matches (53,000)", 53000, receiver.SilverBalance);

                // Verify Items shifted
                // Sender offered 3 items (already deducted from A). Receiver receives +3 items: B had 10 -> 13.
                // Receiver requested 2 items from B. B has 10 -> 8. Sender receives +2 items: A had 7 -> 9.
                // Receiver final balance: 10 + 3 - 2 = 11.
                AssertEquals("Sender final inventory item count matches (9)", 9, invA.Quantity);
                AssertEquals("Receiver final inventory item count matches (11)", 11, invB.Quantity);

                AssertEquals("Trade status completed", 1, trade.Status == "Completed" ? 1 : 0);

                // --------------------------------------------------
                // 8. Re-evaluation on Decline
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 7: Re-evaluation on Rejection (Decline) ---");
                // Clean up and create a new proposal to test Decline
                cardA.PlayerProfileId = sender.Id;
                cardB.PlayerProfileId = receiver.Id;
                sender.SilverBalance = 50000;
                receiver.SilverBalance = 50000;
                invA.Quantity = 10;
                invB.Quantity = 10;
                context.SaveChanges();

                var prop2 = tradeEngine.ProposeTrade(
                    sender.Id, receiver.Id, 10000, 0,
                    offeredCards, requestedCards, offeredItems, requestedItems);
                
                var trade2 = context.Trades.FirstOrDefault(t => t.SenderProfileId == sender.Id && t.Status == "Pending");
                AssertTrue("New proposal succeeds", prop2.Success && trade2 != null);

                var declineRes = tradeEngine.DeclineTrade(trade2!.Id, receiver.Id);
                AssertTrue("Declining trade successfully terminates proposal and refunds sender", declineRes.Success);

                AssertEquals("Declined card unlocked IsInTrade", 0, cardA.IsInTrade ? 1 : 0);
                AssertEquals("Sender Silver refunded completely", 50000, sender.SilverBalance);
                AssertEquals("Sender inventory items refunded completely", 10, invA.Quantity);
                AssertEquals("Trade status declined", 1, trade2.Status == "Declined" ? 1 : 0);

                // Cleanup
                CleanupTestData();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"💥 EXCEPTION OCCURRED DURING TRADING TESTS: {ex.Message}\n{ex.StackTrace}");
                Console.ResetColor();
                return false;
            }
            finally
            {
                try
                {
                    CleanupTestData();
                }
                catch (Exception cleanupEx)
                {
                    Console.WriteLine($"⚠️ Cleanup exception in finally: {cleanupEx.Message}");
                }
            }

            Console.WriteLine("==================================================");
            Console.WriteLine($"🧪 TRADING ENGINE UNIT TESTS RESULTS: {passed} PASSED // {failed} FAILED");
            Console.WriteLine("==================================================");

            return failed == 0;
        }
    }
}
