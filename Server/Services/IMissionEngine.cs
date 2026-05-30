using MwohServer.Models;
using System.Collections.Generic;

namespace MwohServer.Services
{
    public interface IMissionEngine
    {
        List<OperationBlueprint> GetOperations();
        (OperationBlueprint? Operation, MissionBlueprint? Mission) GetMissionBlueprint(string missionCode);
        void RestoreEnergy(PlayerProfile profile);
        MissionAttackResult Attack(int profileId, string missionCode);
        BossBattleResult EngageBoss(int profileId, string missionCode, List<int>? supportProfileIds = null);
    }

    public class MissionAttackResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int EnergyCurrent { get; set; }
        public int EnergyMax { get; set; }
        public double EnergyPct { get; set; }
        public int ProgressPct { get; set; }
        public bool CardDropped { get; set; }
        public string DroppedCardName { get; set; } = string.Empty;
        public bool SectorCleared { get; set; }
        public List<string> LogLines { get; set; } = new();
    }

    public class BossBattleResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string DroppedCardName { get; set; } = string.Empty;
        public int UnlockedOperationId { get; set; }
        public int UnlockedMissionId { get; set; }
        public bool ResourceDropped { get; set; }
        public string DroppedResourceName { get; set; } = string.Empty;
        public string DroppedResourceImage { get; set; } = string.Empty;
        public List<string> LogLines { get; set; } = new();
    }
}
