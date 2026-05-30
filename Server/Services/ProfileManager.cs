using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MwohServer.Data;
using MwohServer.Models;
using System;
using System.Linq;

namespace MwohServer.Services
{
    public class ProfileManager : IProfileManager
    {
        private readonly ILogger<ProfileManager> _logger;
        private readonly MwohDbContext _dbContext;

        public ProfileManager(ILogger<ProfileManager> logger, MwohDbContext dbContext)
        {
            _logger = logger;
            _dbContext = dbContext;
        }

        public ProfileAllocationResult AllocateStatPoints(int profileId, int energyPoints, int attackPoints, int defensePoints)
        {
            _logger.LogInformation($"[ProfileManager] AllocateStatPoints called for profile {profileId}. Energy: {energyPoints}, Attack: {attackPoints}, Defense: {defensePoints}");

            var profile = _dbContext.Profiles.FirstOrDefault(p => p.Id == profileId);
            if (profile == null)
            {
                return new ProfileAllocationResult { Success = false, Message = "Profile not synced." };
            }

            var totalAllocated = energyPoints + attackPoints + defensePoints;
            if (totalAllocated <= 0)
            {
                return new ProfileAllocationResult { Success = false, Message = "Allocated points must be greater than zero." };
            }

            if (totalAllocated > profile.StatPoints)
            {
                return new ProfileAllocationResult { Success = false, Message = "Allocated points exceed available unallocated S.H.I.E.L.D. points." };
            }

            // Deduct and apply stat increments
            profile.StatPoints -= totalAllocated;
            profile.EnergyMax += energyPoints;
            profile.EnergyCurrent += energyPoints;
            profile.AttackPower += attackPoints;
            profile.DefensePower += defensePoints;

            try
            {
                _dbContext.SaveChanges();
                _logger.LogInformation($"[ProfileManager] Successfully allocated {totalAllocated} stat points for profile {profileId}. Remaining: {profile.StatPoints}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[ProfileManager] Failed to save stat point allocations to SQLite for profile {profileId}.");
                return new ProfileAllocationResult { Success = false, Message = "Database write error occurred." };
            }

            return new ProfileAllocationResult
            {
                Success = true,
                Message = "S.H.I.E.L.D. Agent parameters successfully updated and synced!",
                RemainingStatPoints = profile.StatPoints,
                NewEnergyMax = profile.EnergyMax,
                NewEnergyCurrent = profile.EnergyCurrent,
                NewAttackCapacity = profile.AttackPower,
                NewDefenseCapacity = profile.DefensePower
            };
        }

        public LeaderDesignationResult DesignateLeader(int profileId, int cardId)
        {
            _logger.LogInformation($"[ProfileManager] DesignateLeader called for profile {profileId}, cardId: {cardId}");

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

            try
            {
                _dbContext.SaveChanges();
                _logger.LogInformation($"[ProfileManager] Profile {profileId} successfully designated card {cardId} as representative leader.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[ProfileManager] Failed to designate leader card {cardId} for profile {profileId}.");
                return new LeaderDesignationResult { Success = false, Message = "Database write error occurred." };
            }

            return new LeaderDesignationResult
            {
                Success = true,
                Message = "S.H.I.E.L.D. representative leader successfully designated!"
            };
        }
    }
}
