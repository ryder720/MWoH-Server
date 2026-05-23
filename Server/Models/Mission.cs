using System.Collections.Generic;

namespace MwohServer.Models
{
    public class OperationBlueprint
    {
        public int OperationId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string CleanName { get; set; } = string.Empty;
        public int EnergyCost { get; set; }
        public int XpReward { get; set; }
        public int SilverMin { get; set; }
        public int SilverMax { get; set; }
        public string BossName { get; set; } = string.Empty;
        public int BossSilverReward { get; set; }
        public List<MissionBlueprint> Missions { get; set; } = new();
    }

    public class MissionBlueprint
    {
        public string MissionCode { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int EnergyCost { get; set; }
        public int XpReward { get; set; }
        public int SilverMin { get; set; }
        public int SilverMax { get; set; }
        public List<string> PossibleDrops { get; set; } = new();
    }

    public class MissionProgressState
    {
        public int UnlockedOperationId { get; set; } = 1;
        public int UnlockedMissionId { get; set; } = 1; // 1 to 5 within operation
        public Dictionary<string, int> CompletedMissions { get; set; } = new(); // MissionCode -> ClearCount
        public string ActiveMissionId { get; set; } = "1-1";
        public int ActiveMissionProgress { get; set; } = 0; // 0 to 100
    }
}
