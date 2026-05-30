using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MwohServer.Data;
using MwohServer.Models;
using System;
using System.Linq;

namespace MwohServer.Services
{
    public class ShieldTeamEngine : IShieldTeamEngine
    {
        private readonly MwohDbContext _dbContext;
        private readonly ILogger<ShieldTeamEngine> _logger;
        private readonly IAssignmentEngine _assignmentEngine;

        public ShieldTeamEngine(MwohDbContext dbContext, ILogger<ShieldTeamEngine> logger, IAssignmentEngine assignmentEngine)
        {
            _dbContext = dbContext;
            _logger = logger;
            _assignmentEngine = assignmentEngine;
        }

        public RallyResult Rally(int profileId, int targetId)
        {
            _logger.LogInformation($"[ShieldTeamEngine] Rally called from sender {profileId} to target {targetId}");

            var sender = _dbContext.Profiles.FirstOrDefault(p => p.Id == profileId);
            var receiver = _dbContext.Profiles.FirstOrDefault(p => p.Id == targetId);

            if (sender == null)
            {
                return new RallyResult { Success = false, Message = "Sender profile not found." };
            }
            if (receiver == null)
            {
                return new RallyResult { Success = false, Message = "Target profile not found." };
            }
            if (sender.Id == receiver.Id)
            {
                return new RallyResult { Success = false, Message = "You cannot rally yourself, Agent!" };
            }

            // Check standard 24h cooldown
            var cutoff = DateTime.UtcNow.AddHours(-24);
            var existingRally = _dbContext.RallyLogs
                .FirstOrDefault(rl => rl.SenderProfileId == sender.Id && rl.ReceiverProfileId == receiver.Id && rl.RalliedAt >= cutoff);

            if (existingRally != null)
            {
                var timeRemaining = existingRally.RalliedAt.AddHours(24) - DateTime.UtcNow;
                var hours = (int)timeRemaining.TotalHours;
                var minutes = timeRemaining.Minutes;
                return new RallyResult
                {
                    Success = false,
                    Message = $"Cooldown active. You can rally this agent again in {hours}h {minutes}m."
                };
            }

            // Determine if they are S.H.I.E.L.D. Team members
            var isFriend = _dbContext.ShieldTeamMembers
                .Any(m => m.Status == "Accepted" &&
                         ((m.ProfileId == sender.Id && m.MemberProfileId == receiver.Id) ||
                          (m.ProfileId == receiver.Id && m.MemberProfileId == sender.Id)));

            int senderPoints = isFriend ? 20 : 10;
            int receiverPoints = isFriend ? 10 : 5;

            // Update points
            sender.RallyPoints += senderPoints;
            receiver.RallyPoints += receiverPoints;

            // Log rally activity
            var log = new RallyLog
            {
                SenderProfileId = sender.Id,
                ReceiverProfileId = receiver.Id,
                RalliedAt = DateTime.UtcNow
            };
            _dbContext.RallyLogs.Add(log);

            try
            {
                _dbContext.SaveChanges();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ShieldTeamEngine] Failed to process rally point transaction.");
                return new RallyResult { Success = false, Message = "Database error occurred during rally point credit." };
            }

            return new RallyResult
            {
                Success = true,
                Message = $"Successfully rallied {receiver.Nickname}! You gained +{senderPoints} Rally Points. {receiver.Nickname} gained +{receiverPoints}.",
                SenderPointsGained = senderPoints,
                ReceiverPointsGained = receiverPoints,
                NewRallyPoints = sender.RallyPoints
            };
        }

        public ShieldTeamOperationResult Propose(int profileId, int targetId)
        {
            _logger.LogInformation($"[ShieldTeamEngine] ProposeTeamMember from {profileId} to targetId: {targetId}");

            var profile = _dbContext.Profiles.FirstOrDefault(p => p.Id == profileId);
            if (profile == null)
            {
                return new ShieldTeamOperationResult { Success = false, Message = "Profile not synced." };
            }

            if (profile.Id == targetId)
            {
                return new ShieldTeamOperationResult { Success = false, Message = "You cannot propose S.H.I.E.L.D. Team membership to yourself." };
            }

            var target = _dbContext.Profiles.FirstOrDefault(p => p.Id == targetId);
            if (target == null)
            {
                return new ShieldTeamOperationResult { Success = false, Message = "Proposed Agent could not be located in S.H.I.E.L.D. directory." };
            }

            // Check if relationship already exists
            var existing = _dbContext.ShieldTeamMembers
                .FirstOrDefault(m => (m.ProfileId == profile.Id && m.MemberProfileId == targetId) || (m.ProfileId == targetId && m.MemberProfileId == profile.Id));

            if (existing != null)
            {
                if (existing.Status == "Accepted")
                    return new ShieldTeamOperationResult { Success = false, Message = "This agent is already on your active S.H.I.E.L.D. Team." };
                else
                    return new ShieldTeamOperationResult { Success = false, Message = "A S.H.I.E.L.D. Team proposal is already pending with this agent." };
            }

            // Check capacity for both
            int myMax = profile.Level >= 10 ? Math.Min(50, 6 + (profile.Level - 10) / 2) : 5;
            int myCount = _dbContext.ShieldTeamMembers.Count(m => (m.ProfileId == profile.Id || m.MemberProfileId == profile.Id) && m.Status == "Accepted");
            if (myCount >= myMax)
            {
                return new ShieldTeamOperationResult { Success = false, Message = "⚠️ PROPOSAL DENIED // Your S.H.I.E.L.D. Team capacity has reached maximum limits." };
            }

            int targetMax = target.Level >= 10 ? Math.Min(50, 6 + (target.Level - 10) / 2) : 5;
            int targetCount = _dbContext.ShieldTeamMembers.Count(m => (m.ProfileId == targetId || m.MemberProfileId == targetId) && m.Status == "Accepted");
            if (targetCount >= targetMax)
            {
                return new ShieldTeamOperationResult { Success = false, Message = "⚠️ PROPOSAL DENIED // The target Agent has reached their maximum S.H.I.E.L.D. Team capacity." };
            }

            var proposal = new ShieldTeamMember
            {
                ProfileId = profile.Id,
                MemberProfileId = targetId,
                Status = "Pending",
                CreatedAt = DateTime.UtcNow
            };
            _dbContext.ShieldTeamMembers.Add(proposal);

            try
            {
                _dbContext.SaveChanges();
                // Trigger assignment hook
                _assignmentEngine.RecordEvent(profile.Id, GoalType.ShieldRequest, 1);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ShieldTeamEngine] Failed to save team proposal.");
                return new ShieldTeamOperationResult { Success = false, Message = "Database write error occurred." };
            }

            return new ShieldTeamOperationResult { Success = true, Message = $"S.H.I.E.L.D. Team proposal successfully transmitted to agent {target.Nickname}." };
        }

        public ShieldTeamOperationResult Accept(int profileId, int proposerId)
        {
            _logger.LogInformation($"[ShieldTeamEngine] AcceptTeamProposal from {proposerId} to {profileId}");

            var profile = _dbContext.Profiles.FirstOrDefault(p => p.Id == profileId);
            if (profile == null)
            {
                return new ShieldTeamOperationResult { Success = false, Message = "Profile not synced." };
            }

            var proposal = _dbContext.ShieldTeamMembers
                .FirstOrDefault(m => m.ProfileId == proposerId && m.MemberProfileId == profile.Id && m.Status == "Pending");

            if (proposal == null)
            {
                return new ShieldTeamOperationResult { Success = false, Message = "No pending S.H.I.E.L.D. Team proposal from this agent." };
            }

            var proposer = _dbContext.Profiles.FirstOrDefault(p => p.Id == proposerId);
            if (proposer == null)
            {
                return new ShieldTeamOperationResult { Success = false, Message = "Proposer profile not located." };
            }

            // Check capacity
            int myMax = profile.Level >= 10 ? Math.Min(50, 6 + (profile.Level - 10) / 2) : 5;
            int myCount = _dbContext.ShieldTeamMembers.Count(m => (m.ProfileId == profile.Id || m.MemberProfileId == profile.Id) && m.Status == "Accepted");
            if (myCount >= myMax)
            {
                return new ShieldTeamOperationResult { Success = false, Message = "⚠️ TRANSITION FAILED // Your S.H.I.E.L.D. Team has reached maximum limits. You must dismiss an agent first." };
            }

            int proposerMax = proposer.Level >= 10 ? Math.Min(50, 6 + (proposer.Level - 10) / 2) : 5;
            int proposerCount = _dbContext.ShieldTeamMembers.Count(m => (m.ProfileId == proposerId || m.MemberProfileId == proposerId) && m.Status == "Accepted");
            if (proposerCount >= proposerMax)
            {
                return new ShieldTeamOperationResult { Success = false, Message = "⚠️ TRANSITION FAILED // Proposer's S.H.I.E.L.D. Team capacity is at maximum limits." };
            }

            // Set to accepted and award points
            proposal.Status = "Accepted";
            
            profile.StatPoints += 5;
            proposer.StatPoints += 5;

            try
            {
                _dbContext.SaveChanges();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ShieldTeamEngine] Failed to accept proposal.");
                return new ShieldTeamOperationResult { Success = false, Message = "Database write error occurred." };
            }

            return new ShieldTeamOperationResult
            {
                Success = true,
                Message = $"Proposal accepted! You are now team members with {proposer.Nickname}. Both agents have received 5 S.H.I.E.L.D. Attribute points!"
            };
        }

        public ShieldTeamOperationResult Ignore(int profileId, int proposerId)
        {
            _logger.LogInformation($"[ShieldTeamEngine] IgnoreTeamProposal from proposer {proposerId} to profile {profileId}");

            var proposal = _dbContext.ShieldTeamMembers
                .FirstOrDefault(m => m.ProfileId == proposerId && m.MemberProfileId == profileId && m.Status == "Pending");

            if (proposal != null)
            {
                _dbContext.ShieldTeamMembers.Remove(proposal);
                try
                {
                    _dbContext.SaveChanges();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[ShieldTeamEngine] Failed to ignore proposal.");
                    return new ShieldTeamOperationResult { Success = false, Message = "Database write error occurred." };
                }
            }

            return new ShieldTeamOperationResult { Success = true, Message = "S.H.I.E.L.D. Team proposal dismissed." };
        }

        public ShieldTeamOperationResult Remove(int profileId, int memberId)
        {
            _logger.LogInformation($"[ShieldTeamEngine] RemoveTeamMember memberId: {memberId} by active profile: {profileId}");

            var profile = _dbContext.Profiles.FirstOrDefault(p => p.Id == profileId);
            if (profile == null)
            {
                return new ShieldTeamOperationResult { Success = false, Message = "Profile not synced." };
            }

            var relation = _dbContext.ShieldTeamMembers
                .FirstOrDefault(m => ((m.ProfileId == profile.Id && m.MemberProfileId == memberId) || (m.ProfileId == memberId && m.MemberProfileId == profile.Id)) && m.Status == "Accepted");

            if (relation == null)
            {
                return new ShieldTeamOperationResult { Success = false, Message = "Agent is not currently a member of your S.H.I.E.L.D. Team." };
            }

            var other = _dbContext.Profiles.FirstOrDefault(p => p.Id == memberId);
            if (other == null)
            {
                return new ShieldTeamOperationResult { Success = false, Message = "Target profile not located." };
            }

            // Remove connection
            _dbContext.ShieldTeamMembers.Remove(relation);

            // Apply points deduction to active player
            int pointsToDeduct = 5;
            bool wasSubsequentRemovalPenaltyApplied = false;

            if (GameplaySettings.EnableFriendRemoval24HourPenalty)
            {
                if (profile.LastRemovalTime.HasValue && (DateTime.UtcNow - profile.LastRemovalTime.Value).TotalHours < 24)
                {
                    pointsToDeduct = 6;
                    wasSubsequentRemovalPenaltyApplied = true;
                }
            }

            for (int i = 0; i < pointsToDeduct; i++)
            {
                if (profile.EnergyMax >= profile.AttackPower && profile.EnergyMax >= profile.DefensePower)
                {
                    profile.EnergyMax = Math.Max(10, profile.EnergyMax - 1);
                    if (profile.EnergyCurrent > profile.EnergyMax)
                    {
                        profile.EnergyCurrent = profile.EnergyMax;
                    }
                }
                else if (profile.AttackPower >= profile.EnergyMax && profile.AttackPower >= profile.DefensePower)
                {
                    profile.AttackPower = Math.Max(1, profile.AttackPower - 1);
                }
                else
                {
                    profile.DefensePower = Math.Max(1, profile.DefensePower - 1);
                }
            }

            profile.LastRemovalTime = DateTime.UtcNow;
            profile.RemovalsInLast24Hours++;

            // Apply points deduction (exactly 5) to the dismissed other player
            for (int i = 0; i < 5; i++)
            {
                if (other.EnergyMax >= other.AttackPower && other.EnergyMax >= other.DefensePower)
                {
                    other.EnergyMax = Math.Max(10, other.EnergyMax - 1);
                    if (other.EnergyCurrent > other.EnergyMax)
                    {
                        other.EnergyCurrent = other.EnergyMax;
                    }
                }
                else if (other.AttackPower >= other.EnergyMax && other.AttackPower >= other.DefensePower)
                {
                    other.AttackPower = Math.Max(1, other.AttackPower - 1);
                }
                else
                {
                    other.DefensePower = Math.Max(1, other.DefensePower - 1);
                }
            }

            try
            {
                _dbContext.SaveChanges();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ShieldTeamEngine] Failed to commit team member removal.");
                return new ShieldTeamOperationResult { Success = false, Message = "Database write error occurred." };
            }

            string penaltyAlert = wasSubsequentRemovalPenaltyApplied 
                ? "⚠️ 24-HOUR DOUBLE DISMISSAL PENALTY ENFORCED: 6 S.H.I.E.L.D. points subtracted from parameters!"
                : "5 S.H.I.E.L.D. points subtracted from parameters.";

            return new ShieldTeamOperationResult
            {
                Success = true,
                Message = $"Dismissal completed. Agent {other.Nickname} has been dismissed from your S.H.I.E.L.D. Team. {penaltyAlert}"
            };
        }
    }
}
