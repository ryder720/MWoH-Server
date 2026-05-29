using MwohServer.Models;
using System.Collections.Generic;

namespace MwohServer.Services
{
    public interface ITradeEngine
    {
        TradeEligibilityResult CheckTradingEligibility(int senderId, int receiverId);
        TradeActionResult ProposeTrade(
            int senderId, 
            int receiverId, 
            long offeredSilver, 
            long requestedSilver, 
            List<int> offeredCardIds, 
            List<int> requestedCardIds, 
            List<TradeItemOffer> offeredItems, 
            List<TradeItemOffer> requestedItems);
        
        TradeActionResult AcceptTrade(int tradeId, int deciderId);
        TradeActionResult DeclineTrade(int tradeId, int deciderId);
        TradeActionResult CancelTrade(int tradeId, int cancelerId);
    }

    public class TradeItemOffer
    {
        public int ItemTemplateId { get; set; }
        public int Quantity { get; set; }
    }

    public class TradeEligibilityResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int DaysDuration { get; set; }
    }

    public class TradeActionResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
