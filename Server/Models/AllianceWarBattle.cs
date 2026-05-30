using System;
using System.ComponentModel.DataAnnotations;

namespace MwohServer.Models
{
    public class AllianceWarBattle
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string EventId { get; set; } = string.Empty;

        public int AllianceAId { get; set; }
        public int AllianceBId { get; set; }

        public long AllianceAHealthCurrent { get; set; }
        public long AllianceAHealthMax { get; set; }
        public long AllianceBHealthCurrent { get; set; }
        public long AllianceBHealthMax { get; set; }

        public int AllianceAValorCurrent { get; set; } = 0;
        public int AllianceBValorCurrent { get; set; } = 0;

        // JSON serialized details of the 3 active defensive leaders for each alliance:
        // [{"ProfileId": 1, "Nickname": "...", "Role": "...", "DefPowerCurrent": 150, "DefPowerMax": 150, "LeaderCardId": 102}]
        public string AllianceADefensiveLeadersJson { get; set; } = "[]";
        public string AllianceBDefensiveLeadersJson { get; set; } = "[]";

        // Status: "Active", "Concluded"
        public string Status { get; set; } = "Active";

        public DateTime StartTime { get; set; } = DateTime.UtcNow;
        public DateTime EndTime { get; set; }

        public int? WinnerAllianceId { get; set; }

        public bool ReadyUpAllianceA { get; set; } = false;
        public bool ReadyUpAllianceB { get; set; } = false;
        
        public bool IsAiOpponent { get; set; } = false;
    }

    public class WarDefensiveLeaderState
    {
        public int ProfileId { get; set; }
        public string Nickname { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public int DefPowerCurrent { get; set; }
        public int DefPowerMax { get; set; }
        public int LeaderCardId { get; set; }
        public string LeaderCardTitle { get; set; } = string.Empty;
        public string LeaderCardImage { get; set; } = string.Empty;
    }
}
