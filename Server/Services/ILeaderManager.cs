using MwohServer.Models;

namespace MwohServer.Services
{
    public interface ILeaderManager
    {
        LeaderDesignationResult DesignateLeader(int profileId, int cardId);
    }

    public class LeaderDesignationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
