using MwohServer.Models;
using System.Collections.Generic;

namespace MwohServer.Services
{
    public interface IWarEventEngine
    {
        /// <summary>
        /// Retrieves the currently active war battle matchup for the alliance in the specified event, if any.
        /// </summary>
        AllianceWarBattle? GetActiveWarBattle(int allianceId, string eventId);

        /// <summary>
        /// Periodic check to see if we can pair the queued alliance. 
        /// If 10 minutes have elapsed since queueing, automatically pairs against a scaled AI opponent.
        /// </summary>
        AllianceWarBattle? CheckOrMatchmakeAlliance(int allianceId, string eventId);

        /// <summary>
        /// Places the alliance in the matchmaking pool. Asynchronous and non-blocking.
        /// </summary>
        void EnterMatchmakingQueue(int allianceId);

        /// <summary>
        /// Executes a squad strike on either an opposing defensive leader or the alliance core health.
        /// </summary>
        WarBattleResolutionResult ResolveWarEngagement(int profileId, string eventId, int targetProfileId, bool isCoreAttack);
    }

    public class WarBattleResolutionResult
    {
        public bool Success { get; set; } = false;
        public string Message { get; set; } = string.Empty;
        public string OpponentName { get; set; } = string.Empty;
        public long DamageDealt { get; set; }
        public long TargetDefPowerBefore { get; set; }
        public long TargetDefPowerAfter { get; set; }
        public bool TargetFullyDefeated { get; set; }
        public int PointsEarned { get; set; }
        public List<string> CombatLogs { get; set; } = new();
    }
}
