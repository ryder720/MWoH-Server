using MwohServer.Models;
using System.Collections.Generic;

namespace MwohServer.Services
{
    public interface IAllianceEngine
    {
        AllianceCreateResult CreateAlliance(int leaderProfileId, string name, string slogan);
        AllianceDonateResult DonateSilver(int profileId, long silverAmount);
        AllianceDonateResult DonateResourceGroup(int profileId, string groupKey);
        AllianceUpgradeResult PurchaseUpgrade(int leaderProfileId, string upgradeType);
        bool CreateJoinRequest(int profileId, int allianceId);
        bool ProcessJoinRequest(int leaderProfileId, int requestId, bool accept);
        bool AssignMemberRole(int leaderProfileId, int memberProfileId, string role);
        bool LeaveAlliance(int profileId);
        bool DisbandAlliance(int leaderProfileId);
        AllianceStatsBoost GetAllianceCombatBoosts(int profileId, string cardAlignment);
    }

    public class AllianceCreateResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public Alliance? Alliance { get; set; }
    }

    public class AllianceDonateResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public long NewPersonalSilver { get; set; }
        public long NewAllianceDonatedSilver { get; set; }
        public int NewAllianceLevel { get; set; }
        public int NewAllianceRating { get; set; }
    }

    public class AllianceUpgradeResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public Alliance? Alliance { get; set; }
    }

    public class AllianceStatsBoost
    {
        public double AtkModifier { get; set; } = 1.0;
        public double DefModifier { get; set; } = 1.0;
        public string Logs { get; set; } = string.Empty;
    }
}
