using MwohServer.Models;
using System.Collections.Generic;

namespace MwohServer.Services
{
    public interface IAssignmentEngine
    {
        void ReloadTemplates();
        List<AssignmentTemplate> GetActiveTemplates();
        List<PlayerAssignmentProgressDto> GetPlayerProgress(int profileId);
        void RecordEvent(int profileId, GoalType goalType, int value);
        ClaimResult ClaimReward(int profileId, string assignmentId);
    }

    public class PlayerAssignmentProgressDto
    {
        public AssignmentTemplate Template { get; set; } = new();
        public int CurrentProgress { get; set; }
        public bool IsCompleted { get; set; }
        public bool IsClaimed { get; set; }
        public bool IsActive { get; set; }
        public int SecondsRemaining { get; set; } // Bounded time remaining, or -1 if infinite
    }

    public class ClaimResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public long SilverBalance { get; set; }
        public int RallyPoints { get; set; }
        public long MobaCoinBalance { get; set; }
        public string RewardText { get; set; } = string.Empty;
    }
}
