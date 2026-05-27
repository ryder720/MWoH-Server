using MwohServer.Models;
using System.Collections.Generic;
using System;

namespace MwohServer.Services
{
    public interface IBattleEngine
    {
        void RestoreBattlePower(PlayerProfile profile);
        BattleResolutionResult ResolveBattle(int attackerProfileId, int defenderProfileId, bool isSparring);
    }

    public class BattleResolutionResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        
        public bool AttackerWon { get; set; }
        public int AttackerFinalPower { get; set; }
        public int DefenderFinalPower { get; set; }
        
        public long SilverExchanged { get; set; }
        public int MasteryEarned { get; set; }
        
        public int AttackerAttackPowerBefore { get; set; }
        public int AttackerAttackPowerAfter { get; set; }
        public int AttackerAttackPowerMax { get; set; }
        
        public int DefenderDefensePowerBefore { get; set; }
        public int DefenderDefensePowerAfter { get; set; }
        public int DefenderDefensePowerMax { get; set; }

        public List<string> LogLines { get; set; } = new();
    }
}
