using MwohServer.Models;
using System.Collections.Generic;

namespace MwohServer.Services
{
    public interface IShieldTeamEngine
    {
        RallyResult Rally(int profileId, int targetId);
        ShieldTeamOperationResult Propose(int profileId, int targetId);
        ShieldTeamOperationResult Accept(int profileId, int proposerId);
        ShieldTeamOperationResult Ignore(int profileId, int proposerId);
        ShieldTeamOperationResult Remove(int profileId, int memberId);
    }

    public class RallyResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int SenderPointsGained { get; set; }
        public int ReceiverPointsGained { get; set; }
        public int NewRallyPoints { get; set; }
    }

    public class ShieldTeamOperationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
