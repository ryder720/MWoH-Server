using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MwohServer.Data;
using MwohServer.Models;

namespace MwohServer.Services
{
    public class TradeEngine : ITradeEngine
    {
        private readonly MwohDbContext _dbContext;

        public TradeEngine(MwohDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public TradeEligibilityResult CheckTradingEligibility(int senderId, int receiverId)
        {
            if (senderId == receiverId)
            {
                return new TradeEligibilityResult { Success = false, Message = "⚠️ INVALID OPERATION // Agents cannot exchange materials with themselves." };
            }

            var sender = _dbContext.Profiles.FirstOrDefault(p => p.Id == senderId);
            var receiver = _dbContext.Profiles.FirstOrDefault(p => p.Id == receiverId);

            if (sender == null || receiver == null)
            {
                return new TradeEligibilityResult { Success = false, Message = "⚠️ PROFILE SYNCHRONIZATION FAILED // Profile data not found in S.H.I.E.L.D. database." };
            }

            // 1. Min Clearance Level Check
            if (sender.Level < GameplaySettings.TradeMinLevel)
            {
                return new TradeEligibilityResult { Success = false, Message = $"⚠️ ACCESS DENIED // Your Clearance Level ({sender.Level}) is below the required threshold of Level {GameplaySettings.TradeMinLevel}." };
            }

            if (receiver.Level < GameplaySettings.TradeMinLevel)
            {
                return new TradeEligibilityResult { Success = false, Message = $"⚠️ UPLINK REFUSED // Target Operative Clearance Level ({receiver.Level}) is below Level {GameplaySettings.TradeMinLevel}." };
            }

            // 2. Co-Op Alliance / Friendship network check
            var relation = _dbContext.ShieldTeamMembers
                .FirstOrDefault(m => (m.ProfileId == senderId && m.MemberProfileId == receiverId && m.Status == "Accepted") || 
                                     (m.ProfileId == receiverId && m.MemberProfileId == senderId && m.Status == "Accepted"));

            bool sameAlliance = sender.AllianceId != null && receiver.AllianceId != null && sender.AllianceId == receiver.AllianceId;

            if (relation == null && !sameAlliance)
            {
                return new TradeEligibilityResult { Success = false, Message = "⚠️ TRANSACTION LOCKED // Material exchange is restricted. You must be S.H.I.E.L.D. Team Members or in the same Alliance to trade." };
            }

            // 3. Duration restrictions
            int requiredDays = GameplaySettings.TradeCooldownDays;
            if (requiredDays <= 0)
            {
                return new TradeEligibilityResult { Success = true, Message = "🟢 SECURITY PROTOCOLS BYPASSED // Secure local debug trade link active.", DaysDuration = 999 };
            }

            double friendDays = 0;
            double allianceDays = 0;
            bool friendEligible = false;
            bool allianceEligible = false;

            if (relation != null)
            {
                friendDays = (DateTime.UtcNow - relation.CreatedAt).TotalDays;
                if (friendDays >= requiredDays)
                {
                    friendEligible = true;
                }
            }

            if (sameAlliance)
            {
                if (sender.AllianceJoinedAt.HasValue && receiver.AllianceJoinedAt.HasValue)
                {
                    var senderJoinedDays = (DateTime.UtcNow - sender.AllianceJoinedAt.Value).TotalDays;
                    var receiverJoinedDays = (DateTime.UtcNow - receiver.AllianceJoinedAt.Value).TotalDays;
                    allianceDays = Math.Min(senderJoinedDays, receiverJoinedDays);

                    if (allianceDays >= requiredDays)
                    {
                        allianceEligible = true;
                    }
                }
            }

            // Pass if EITHER friendship or alliance relationship is old enough
            if (!friendEligible && !allianceEligible)
            {
                int remainingDays = (int)Math.Ceiling(requiredDays - Math.Max(friendDays, allianceDays));
                return new TradeEligibilityResult 
                { 
                    Success = false, 
                    Message = $"⚠️ SECURITY LOCK ACTIVE // S.H.I.E.L.D. anti-infiltration delay in effect. Uplink must be maintained for {requiredDays} days before trading. Remaining: {remainingDays} days.",
                    DaysDuration = (int)Math.Max(friendDays, allianceDays)
                };
            }

            return new TradeEligibilityResult { Success = true, Message = "🟢 TRANSACTION PROTOCOL ENGAGED // Authorized uplink established.", DaysDuration = (int)Math.Max(friendDays, allianceDays) };
        }

        public TradeActionResult ProposeTrade(
            int senderId, 
            int receiverId, 
            long offeredSilver, 
            long requestedSilver, 
            List<int> offeredCardIds, 
            List<int> requestedCardIds, 
            List<TradeItemOffer> offeredItems, 
            List<TradeItemOffer> requestedItems)
        {
            // Verify Eligibility
            var eligibility = CheckTradingEligibility(senderId, receiverId);
            if (!eligibility.Success)
            {
                return new TradeActionResult { Success = false, Message = eligibility.Message };
            }

            // Check Card Volume limits
            int maxCards = GameplaySettings.TradeMaxCards;
            if (offeredCardIds.Count > maxCards || requestedCardIds.Count > maxCards)
            {
                return new TradeActionResult { Success = false, Message = $"⚠️ PROTOCOL FAULT // Card exchange volume overflows maximum limit of {maxCards} cards per side." };
            }

            using (var transaction = _dbContext.Database.BeginTransaction())
            {
                try
                {
                    var sender = _dbContext.Profiles
                        .Include(p => p.Cards)
                        .Include(p => p.InventoryItems)
                        .FirstOrDefault(p => p.Id == senderId);
                    
                    var receiver = _dbContext.Profiles
                        .Include(p => p.Cards)
                        .FirstOrDefault(p => p.Id == receiverId);

                    if (sender == null || receiver == null)
                    {
                        return new TradeActionResult { Success = false, Message = "⚠️ SYNCHRONIZATION FAILED // Active profile data unreadable." };
                    }

                    // 1. Verify Offered Silver Balance
                    if (offeredSilver < 0 || requestedSilver < 0)
                    {
                        return new TradeActionResult { Success = false, Message = "⚠️ BALANCE EXCEPTION // Credit value cannot be negative." };
                    }

                    if (sender.SilverBalance < offeredSilver)
                    {
                        return new TradeActionResult { Success = false, Message = $"⚠️ TRANSACTION REJECTED // Insufficient Silver balance. Bal: 🪙 {sender.SilverBalance:N0} // Req: 🪙 {offeredSilver:N0}." };
                    }

                    // 2. Verify and Lock Sender's Offered Cards
                    var lockedCards = new List<PlayerCard>();
                    foreach (var cardId in offeredCardIds)
                    {
                        var card = sender.Cards.FirstOrDefault(c => c.Id == cardId);
                        if (card == null)
                        {
                            return new TradeActionResult { Success = false, Message = "⚠️ INTEGRITY BREACH // One or more offered Hero Cards are not owned by you." };
                        }

                        if (card.IsInTrade)
                        {
                            return new TradeActionResult { Success = false, Message = $"⚠️ LOCK EXCEPTION // '{card.GetDisplayName()}' is already locked in another trade." };
                        }

                        if (card.IsLeader || card.IsInAttackDeck || card.IsInDefenseDeck)
                        {
                            return new TradeActionResult { Success = false, Message = $"⚠️ LOCK EXCEPTION // '{card.GetDisplayName()}' is currently deployed in squad configuration or designated as Leader." };
                        }

                        lockedCards.Add(card);
                    }

                    // 3. Verify Receiver's Requested Cards (Readiness check)
                    foreach (var cardId in requestedCardIds)
                    {
                        var card = receiver.Cards.FirstOrDefault(c => c.Id == cardId);
                        if (card == null)
                        {
                            return new TradeActionResult { Success = false, Message = "⚠️ INTEGRITY BREACH // Requested target cards cannot be found in receiver's roster." };
                        }

                        if (card.IsInTrade)
                        {
                            return new TradeActionResult { Success = false, Message = $"⚠️ LOCK EXCEPTION // '{card.GetDisplayName()}' is locked in another trade." };
                        }

                        if (card.IsLeader || card.IsInAttackDeck || card.IsInDefenseDeck)
                        {
                            return new TradeActionResult { Success = false, Message = $"⚠️ LOCK EXCEPTION // '{card.GetDisplayName()}' is currently deployed in receiver's squad configurations." };
                        }
                    }

                    // 4. Verify and Deduct Offered Items
                    foreach (var itemOffer in offeredItems)
                    {
                        if (itemOffer.Quantity <= 0) continue;

                        var inventoryItem = sender.InventoryItems.FirstOrDefault(i => i.ItemTemplateId == itemOffer.ItemTemplateId);
                        if (inventoryItem == null || inventoryItem.Quantity < itemOffer.Quantity)
                        {
                            var template = _dbContext.ItemTemplates.FirstOrDefault(t => t.Id == itemOffer.ItemTemplateId);
                            string itemName = template?.Name ?? $"Item #{itemOffer.ItemTemplateId}";
                            return new TradeActionResult { Success = false, Message = $"⚠️ ITEM DEPLETION // Insufficient quantity of '{itemName}' in your supply depot." };
                        }

                        // Deduct immediately (refunding later if canceled/declined)
                        inventoryItem.Quantity -= itemOffer.Quantity;
                    }

                    // 5. Lock Assets
                    foreach (var card in lockedCards)
                    {
                        card.IsInTrade = true;
                    }
                    sender.SilverBalance -= offeredSilver;

                    // 6. Create Trade Proposal
                    var trade = new Trade
                    {
                        SenderProfileId = senderId,
                        ReceiverProfileId = receiverId,
                        Status = "Pending",
                        CreatedAt = DateTime.UtcNow,
                        OfferedSilver = offeredSilver,
                        RequestedSilver = requestedSilver,
                        OfferedCardIdsJson = JsonSerializer.Serialize(offeredCardIds),
                        RequestedCardIdsJson = JsonSerializer.Serialize(requestedCardIds),
                        OfferedItemsJson = JsonSerializer.Serialize(offeredItems),
                        RequestedItemsJson = JsonSerializer.Serialize(requestedItems)
                    };

                    _dbContext.Trades.Add(trade);
                    _dbContext.SaveChanges();
                    transaction.Commit();

                    return new TradeActionResult { Success = true, Message = "🟢 TRANSMISSION SECURED // Trade proposal has been successfully logged." };
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    return new TradeActionResult { Success = false, Message = $"⚠️ INTERNAL DATABASE FAILURE // {ex.Message}" };
                }
            }
        }

        public TradeActionResult AcceptTrade(int tradeId, int deciderId)
        {
            var trade = _dbContext.Trades.FirstOrDefault(t => t.Id == tradeId);
            if (trade == null)
            {
                return new TradeActionResult { Success = false, Message = "⚠️ CONSOLE ERR // Trade transaction not found." };
            }

            if (trade.Status != "Pending")
            {
                return new TradeActionResult { Success = false, Message = $"⚠️ PROTOCOL LOCKED // Transaction is no longer pending. Status: {trade.Status}." };
            }

            if (trade.ReceiverProfileId != deciderId)
            {
                return new TradeActionResult { Success = false, Message = "⚠️ SECURITY VIOLATION // Only the target recipient can authorize this transaction." };
            }

            // Verify Eligibility Still Stands
            var eligibility = CheckTradingEligibility(trade.SenderProfileId, trade.ReceiverProfileId);
            if (!eligibility.Success)
            {
                return new TradeActionResult { Success = false, Message = eligibility.Message };
            }

            using (var transaction = _dbContext.Database.BeginTransaction())
            {
                try
                {
                    var sender = _dbContext.Profiles
                        .Include(p => p.Cards)
                        .Include(p => p.InventoryItems)
                        .FirstOrDefault(p => p.Id == trade.SenderProfileId);

                    var receiver = _dbContext.Profiles
                        .Include(p => p.Cards)
                        .Include(p => p.InventoryItems)
                        .FirstOrDefault(p => p.Id == trade.ReceiverProfileId);

                    if (sender == null || receiver == null)
                    {
                        return new TradeActionResult { Success = false, Message = "⚠️ SYNCHRONIZATION FAILED // Active profile data unreadable." };
                    }

                    // 1. Re-validate Requested Silver Credit balance
                    if (receiver.SilverBalance < trade.RequestedSilver)
                    {
                        return new TradeActionResult { Success = false, Message = $"⚠️ TRANSACTION LOCKED // Your Silver balance is insufficient. Bal: 🪙 {receiver.SilverBalance:N0} // Req: 🪙 {trade.RequestedSilver:N0}." };
                    }

                    // 2. Re-validate Sender Offered Cards (should still be locked in trade)
                    var offeredCardIds = JsonSerializer.Deserialize<List<int>>(trade.OfferedCardIdsJson) ?? new List<int>();
                    var offeredCards = new List<PlayerCard>();
                    foreach (var cardId in offeredCardIds)
                    {
                        var card = _dbContext.PlayerCards.Include(c => c.CardTemplate).FirstOrDefault(c => c.Id == cardId && c.PlayerProfileId == sender.Id);
                        if (card == null || !card.IsInTrade)
                        {
                            return new TradeActionResult { Success = false, Message = "⚠️ INTEGRITY BREACH // One or more offered Cards are no longer locked or owned by the sender." };
                        }
                        offeredCards.Add(card);
                    }

                    // 3. Validate and Lock Receiver's Requested Cards
                    var requestedCardIds = JsonSerializer.Deserialize<List<int>>(trade.RequestedCardIdsJson) ?? new List<int>();
                    var requestedCards = new List<PlayerCard>();
                    foreach (var cardId in requestedCardIds)
                    {
                        var card = receiver.Cards.FirstOrDefault(c => c.Id == cardId);
                        if (card == null)
                        {
                            return new TradeActionResult { Success = false, Message = "⚠️ INTEGRITY BREACH // One or more requested Cards are no longer in your inventory roster." };
                        }

                        if (card.IsInTrade)
                        {
                            return new TradeActionResult { Success = false, Message = $"⚠️ LOCK EXCEPTION // '{card.GetDisplayName()}' is locked in another pending trade." };
                        }

                        if (card.IsLeader || card.IsInAttackDeck || card.IsInDefenseDeck)
                        {
                            return new TradeActionResult { Success = false, Message = $"⚠️ LOCK EXCEPTION // '{card.GetDisplayName()}' is currently deployed in squad configuration or designated as Leader." };
                        }

                        requestedCards.Add(card);
                    }

                    // 4. Validate and Deduct Receiver's Requested Items
                    var requestedItems = JsonSerializer.Deserialize<List<TradeItemOffer>>(trade.RequestedItemsJson) ?? new List<TradeItemOffer>();
                    foreach (var itemOffer in requestedItems)
                    {
                        if (itemOffer.Quantity <= 0) continue;

                        var inventoryItem = receiver.InventoryItems.FirstOrDefault(i => i.ItemTemplateId == itemOffer.ItemTemplateId);
                        if (inventoryItem == null || inventoryItem.Quantity < itemOffer.Quantity)
                        {
                            var template = _dbContext.ItemTemplates.FirstOrDefault(t => t.Id == itemOffer.ItemTemplateId);
                            string itemName = template?.Name ?? $"Item #{itemOffer.ItemTemplateId}";
                            return new TradeActionResult { Success = false, Message = $"⚠️ ITEM DEPLETION // Insufficient quantity of '{itemName}' in your supply depot." };
                        }

                        // Deduct immediately
                        inventoryItem.Quantity -= itemOffer.Quantity;
                    }

                    // 5. Transfer Financials
                    receiver.SilverBalance += trade.OfferedSilver;
                    sender.SilverBalance += trade.RequestedSilver;
                    receiver.SilverBalance -= trade.RequestedSilver;

                    // 6. Transfer Offered Cards (Sender -> Receiver)
                    foreach (var card in offeredCards)
                    {
                        card.PlayerProfileId = receiver.Id;
                        card.IsInTrade = false;
                        card.IsLeader = false;
                        card.IsInAttackDeck = false;
                        card.IsInDefenseDeck = false;
                    }

                    // 7. Transfer Requested Cards (Receiver -> Sender)
                    foreach (var card in requestedCards)
                    {
                        card.PlayerProfileId = sender.Id;
                        card.IsInTrade = false;
                        card.IsLeader = false;
                        card.IsInAttackDeck = false;
                        card.IsInDefenseDeck = false;
                    }

                    // 8. Transfer Offered Items (Sender already deducted, add to Receiver)
                    var offeredItems = JsonSerializer.Deserialize<List<TradeItemOffer>>(trade.OfferedItemsJson) ?? new List<TradeItemOffer>();
                    foreach (var itemOffer in offeredItems)
                    {
                        if (itemOffer.Quantity <= 0) continue;

                        var receiverItem = receiver.InventoryItems.FirstOrDefault(i => i.ItemTemplateId == itemOffer.ItemTemplateId);
                        if (receiverItem == null)
                        {
                            receiverItem = new PlayerInventoryItem
                            {
                                PlayerProfileId = receiver.Id,
                                ItemTemplateId = itemOffer.ItemTemplateId,
                                Quantity = 0
                            };
                            _dbContext.PlayerInventoryItems.Add(receiverItem);
                        }
                        receiverItem.Quantity += itemOffer.Quantity;
                    }

                    // 9. Transfer Requested Items (Receiver already deducted, add to Sender)
                    foreach (var itemOffer in requestedItems)
                    {
                        if (itemOffer.Quantity <= 0) continue;

                        var senderItem = sender.InventoryItems.FirstOrDefault(i => i.ItemTemplateId == itemOffer.ItemTemplateId);
                        if (senderItem == null)
                        {
                            senderItem = new PlayerInventoryItem
                            {
                                PlayerProfileId = sender.Id,
                                ItemTemplateId = itemOffer.ItemTemplateId,
                                Quantity = 0
                            };
                            _dbContext.PlayerInventoryItems.Add(senderItem);
                        }
                        senderItem.Quantity += itemOffer.Quantity;
                    }

                    // 10. Finalize Trade
                    trade.Status = "Completed";
                    trade.CompletedAt = DateTime.UtcNow;

                    _dbContext.SaveChanges();
                    transaction.Commit();

                    return new TradeActionResult { Success = true, Message = "🟢 TRANSMISSION COMPLETE // Material assets successfully swapped in mainframe databases." };
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    return new TradeActionResult { Success = false, Message = $"⚠️ INTERNAL DATABASE FAILURE // {ex.Message}" };
                }
            }
        }

        public TradeActionResult DeclineTrade(int tradeId, int deciderId)
        {
            var trade = _dbContext.Trades.FirstOrDefault(t => t.Id == tradeId);
            if (trade == null)
            {
                return new TradeActionResult { Success = false, Message = "⚠️ CONSOLE ERR // Trade transaction not found." };
            }

            if (trade.Status != "Pending")
            {
                return new TradeActionResult { Success = false, Message = "⚠️ PROTOCOL LOCKED // Transaction is no longer pending." };
            }

            if (trade.ReceiverProfileId != deciderId)
            {
                return new TradeActionResult { Success = false, Message = "⚠️ SECURITY VIOLATION // Only the target recipient can decline this proposal." };
            }

            using (var transaction = _dbContext.Database.BeginTransaction())
            {
                try
                {
                    var sender = _dbContext.Profiles
                        .Include(p => p.Cards)
                        .Include(p => p.InventoryItems)
                        .FirstOrDefault(p => p.Id == trade.SenderProfileId);

                    if (sender == null)
                    {
                        return new TradeActionResult { Success = false, Message = "⚠️ SYNCHRONIZATION FAILED // Active profile data unreadable." };
                    }

                    // 1. Unlock Sender's Cards
                    var offeredCardIds = JsonSerializer.Deserialize<List<int>>(trade.OfferedCardIdsJson) ?? new List<int>();
                    foreach (var cardId in offeredCardIds)
                    {
                        var card = _dbContext.PlayerCards.FirstOrDefault(c => c.Id == cardId && c.PlayerProfileId == sender.Id);
                        if (card != null)
                        {
                            card.IsInTrade = false;
                        }
                    }

                    // 2. Refund Sender's Silver Credits
                    sender.SilverBalance += trade.OfferedSilver;

                    // 3. Refund Sender's Offered Items
                    var offeredItems = JsonSerializer.Deserialize<List<TradeItemOffer>>(trade.OfferedItemsJson) ?? new List<TradeItemOffer>();
                    foreach (var itemOffer in offeredItems)
                    {
                        if (itemOffer.Quantity <= 0) continue;

                        var senderItem = sender.InventoryItems.FirstOrDefault(i => i.ItemTemplateId == itemOffer.ItemTemplateId);
                        if (senderItem == null)
                        {
                            senderItem = new PlayerInventoryItem
                            {
                                PlayerProfileId = sender.Id,
                                ItemTemplateId = itemOffer.ItemTemplateId,
                                Quantity = 0
                            };
                            _dbContext.PlayerInventoryItems.Add(senderItem);
                        }
                        senderItem.Quantity += itemOffer.Quantity;
                    }

                    // 4. Update Status
                    trade.Status = "Declined";
                    trade.CompletedAt = DateTime.UtcNow;

                    _dbContext.SaveChanges();
                    transaction.Commit();

                    return new TradeActionResult { Success = true, Message = "🔴 ABORT TRANSACTION // Trade proposal has been declined and assets refunded." };
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    return new TradeActionResult { Success = false, Message = $"⚠️ INTERNAL DATABASE FAILURE // {ex.Message}" };
                }
            }
        }

        public TradeActionResult CancelTrade(int tradeId, int cancelerId)
        {
            var trade = _dbContext.Trades.FirstOrDefault(t => t.Id == tradeId);
            if (trade == null)
            {
                return new TradeActionResult { Success = false, Message = "⚠️ CONSOLE ERR // Trade transaction not found." };
            }

            if (trade.Status != "Pending")
            {
                return new TradeActionResult { Success = false, Message = "⚠️ PROTOCOL LOCKED // Transaction is no longer pending." };
            }

            if (trade.SenderProfileId != cancelerId)
            {
                return new TradeActionResult { Success = false, Message = "⚠️ SECURITY VIOLATION // Only the sender who proposed the trade can abort it." };
            }

            using (var transaction = _dbContext.Database.BeginTransaction())
            {
                try
                {
                    var sender = _dbContext.Profiles
                        .Include(p => p.Cards)
                        .Include(p => p.InventoryItems)
                        .FirstOrDefault(p => p.Id == trade.SenderProfileId);

                    if (sender == null)
                    {
                        return new TradeActionResult { Success = false, Message = "⚠️ SYNCHRONIZATION FAILED // Active profile data unreadable." };
                    }

                    // 1. Unlock Sender's Cards
                    var offeredCardIds = JsonSerializer.Deserialize<List<int>>(trade.OfferedCardIdsJson) ?? new List<int>();
                    foreach (var cardId in offeredCardIds)
                    {
                        var card = _dbContext.PlayerCards.FirstOrDefault(c => c.Id == cardId && c.PlayerProfileId == sender.Id);
                        if (card != null)
                        {
                            card.IsInTrade = false;
                        }
                    }

                    // 2. Refund Sender's Silver Credits
                    sender.SilverBalance += trade.OfferedSilver;

                    // 3. Refund Sender's Offered Items
                    var offeredItems = JsonSerializer.Deserialize<List<TradeItemOffer>>(trade.OfferedItemsJson) ?? new List<TradeItemOffer>();
                    foreach (var itemOffer in offeredItems)
                    {
                        if (itemOffer.Quantity <= 0) continue;

                        var senderItem = sender.InventoryItems.FirstOrDefault(i => i.ItemTemplateId == itemOffer.ItemTemplateId);
                        if (senderItem == null)
                        {
                            senderItem = new PlayerInventoryItem
                            {
                                PlayerProfileId = sender.Id,
                                ItemTemplateId = itemOffer.ItemTemplateId,
                                Quantity = 0
                            };
                            _dbContext.PlayerInventoryItems.Add(senderItem);
                        }
                        senderItem.Quantity += itemOffer.Quantity;
                    }

                    // 4. Update Status
                    trade.Status = "Canceled";
                    trade.CompletedAt = DateTime.UtcNow;

                    _dbContext.SaveChanges();
                    transaction.Commit();

                    return new TradeActionResult { Success = true, Message = "🔴 CONSIGNMENT ABORTED // Trade has been canceled and assets returned." };
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    return new TradeActionResult { Success = false, Message = $"⚠️ INTERNAL DATABASE FAILURE // {ex.Message}" };
                }
            }
        }
    }
}
