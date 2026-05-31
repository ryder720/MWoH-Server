using MwohServer.Models;
using System.Collections.Generic;

namespace MwohServer.Services
{
    public interface IEventEngine
    {
        void ReloadTemplates();
        List<EventTemplate> GetTemplates();
        EventTemplate? GetActiveEvent();
        string GetEventState(EventTemplate temp);
        PlayerEventProgress GetPlayerProgress(int profileId, string eventId);
        void RecordEventPoints(int profileId, string eventId, int points);
        int GetPlayerRank(int profileId, string eventId);
        List<(string Nickname, int Level, int Points)> GetLeaderboard(string eventId, int limit = 5);
        ClaimMilestoneResult ClaimMilestoneReward(int profileId, string eventId, int tierIndex);
        EventCalculationResult CalculateAndDispatchRewards(string eventId);
        
        RaidProgressState GetRaidState(int profileId, string eventId);
        List<HelperDto> GetAvailableHelpers(int profileId, int limit = 6);
        void SelectRaidHelper(int profileId, string eventId, int helperProfileId);
        RaidBattleResolutionResult ResolveRaidBattle(int profileId, string eventId, string difficulty);
    }

    public class ClaimMilestoneResult
    {
        public bool Success { get; set; } = false;
        public string Message { get; set; } = string.Empty;
        public int NewTierClaimed { get; set; }
    }

    public class EventCalculationResult
    {
        public bool Success { get; set; } = false;
        public string Message { get; set; } = string.Empty;
        public int AgentsProcessed { get; set; } = 0;
    }

    public class HelperDto
    {
        public int ProfileId { get; set; }
        public string Nickname { get; set; } = string.Empty;
        public int Level { get; set; }
        public int CardId { get; set; }
        public string CardTitle { get; set; } = string.Empty;
        public string CardRarity { get; set; } = string.Empty;
        public string CardImage { get; set; } = string.Empty;
        public string SkillName { get; set; } = string.Empty;
        public string SkillEffect { get; set; } = string.Empty;
    }

    public class RaidBattleResolutionResult
    {
        public bool Success { get; set; } = false;
        public string Message { get; set; } = string.Empty;
        public string BossName { get; set; } = string.Empty;
        public int BossLevel { get; set; }
        public string BodyPartName { get; set; } = string.Empty;
        public string Difficulty { get; set; } = "Easy";
        public long PlayerDamage { get; set; }
        public long BossDefense { get; set; }
        public long NetDamage { get; set; }
        public long MainHpBefore { get; set; }
        public long MainHpAfter { get; set; }
        public long PartHpBefore { get; set; }
        public long PartHpAfter { get; set; }
        public string VictoryType { get; set; } = "Defeat"; // PartDefeated, OneShot, Defeat
        public int PointsEarned { get; set; }
        public long SilverEarned { get; set; }
        public List<string> CombatLogs { get; set; } = new();
    }
}
