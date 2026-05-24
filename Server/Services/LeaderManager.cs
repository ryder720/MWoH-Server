using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MwohServer.Data;
using MwohServer.Models;
using System;
using System.Linq;

namespace MwohServer.Services
{
    public class LeaderManager : ILeaderManager
    {
        private readonly ILogger<LeaderManager> _logger;
        private readonly MwohDbContext _dbContext;

        public LeaderManager(ILogger<LeaderManager> logger, MwohDbContext dbContext)
        {
            _logger = logger;
            _dbContext = dbContext;
        }

        public LeaderDesignationResult DesignateLeader(int profileId, int cardId)
        {
            var profile = _dbContext.Profiles
                .Include(p => p.Cards)
                .FirstOrDefault(p => p.Id == profileId);

            if (profile == null)
            {
                return new LeaderDesignationResult { Success = false, Message = "Profile not found." };
            }

            var targetCard = profile.Cards.FirstOrDefault(c => c.Id == cardId);
            if (targetCard == null)
            {
                return new LeaderDesignationResult { Success = false, Message = "Card not found or unauthorized." };
            }

            // Update leader status across all cards
            foreach (var card in profile.Cards)
            {
                card.IsLeader = (card.Id == cardId);
            }

            _dbContext.SaveChanges();
            _logger.LogInformation($"[LeaderManager] Profile {profileId} successfully designated card {cardId} as representative leader.");

            return new LeaderDesignationResult
            {
                Success = true,
                Message = "S.H.I.E.L.D. representative leader successfully designated!"
            };
        }
    }
}
