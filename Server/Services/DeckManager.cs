using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MwohServer.Data;
using MwohServer.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MwohServer.Services
{
    public class DeckManager : IDeckManager
    {
        private readonly ILogger<DeckManager> _logger;
        private readonly MwohDbContext _dbContext;

        public DeckManager(ILogger<DeckManager> logger, MwohDbContext dbContext)
        {
            _logger = logger;
            _dbContext = dbContext;
        }

        public DeckSyncResult SyncDeck(int profileId, string mode, List<int> cardIds)
        {
            var profile = _dbContext.Profiles
                .Include(p => p.Cards)
                    .ThenInclude(c => c.CardTemplate)
                .FirstOrDefault(p => p.Id == profileId);

            if (profile == null)
            {
                return new DeckSyncResult { Success = false, Message = "Profile not found." };
            }

            if (cardIds.Count > 5)
            {
                return new DeckSyncResult { Success = false, Message = "Squad can have at most 5 cards." };
            }

            // Verify they belong to this profile
            var validCards = profile.Cards.Where(c => cardIds.Contains(c.Id)).ToList();
            if (validCards.Count != cardIds.Count)
            {
                return new DeckSyncResult { Success = false, Message = "One or more cards not found or unauthorized." };
            }

            // Verify cost capacity
            var totalCost = validCards.Sum(c => c.CardTemplate?.PowerRequirement ?? 0);
            var limit = mode.ToLower() == "attack" ? profile.AttackPower : profile.DefensePower;
            if (totalCost > limit)
            {
                return new DeckSyncResult { Success = false, Message = "Clearance power requirement exceeds deck capacity!" };
            }

            // Update flags in player's cards
            foreach (var card in profile.Cards)
            {
                if (mode.ToLower() == "attack")
                {
                    card.IsInAttackDeck = cardIds.Contains(card.Id);
                }
                else
                {
                    card.IsInDefenseDeck = cardIds.Contains(card.Id);
                }
            }

            _dbContext.SaveChanges();
            _logger.LogInformation($"[DeckManager] Synchronized {mode.ToUpper()} squad for profile {profileId}. Card IDs: {string.Join(",", cardIds)}");

            return new DeckSyncResult
            {
                Success = true,
                Message = $"{mode.ToUpper()} squad configurations successfully synchronized!"
            };
        }
    }
}
