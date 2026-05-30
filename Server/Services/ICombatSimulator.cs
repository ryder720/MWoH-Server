using MwohServer.Models;
using System.Collections.Generic;

namespace MwohServer.Services
{
    public interface ICombatSimulator
    {
        CombatSimulationResult Simulate(
            List<PlayerCard> attackerCards, 
            List<PlayerCard> defenderCards,
            string attackerNickname,
            int attackerLevel,
            int attackerPower,
            int attackerCost,
            string defenderNickname,
            int defenderLevel,
            int defenderPower,
            int defenderCost,
            double defenderScale,
            List<AllianceStatsBoost> attackerAllianceBoosts,
            List<AllianceStatsBoost> defenderAllianceBoosts,
            bool isSparring);
    }

    public class CombatSimulationResult
    {
        public bool AttackerWon { get; set; }
        public int AttackerFinalPower { get; set; }
        public int DefenderFinalPower { get; set; }
        public int AttackerTriggerCount { get; set; }
        public int DefenderTriggerCount { get; set; }
        public List<string> LogLines { get; set; } = new();
    }
}
