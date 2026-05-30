using MwohServer.Models;

namespace MwohServer.Services
{
    public interface IProfileManager
    {
        ProfileAllocationResult AllocateStatPoints(int profileId, int energyPoints, int attackPoints, int defensePoints);
        LeaderDesignationResult DesignateLeader(int profileId, int cardId);
    }

    public class ProfileAllocationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int RemainingStatPoints { get; set; }
        public int NewEnergyMax { get; set; }
        public int NewEnergyCurrent { get; set; }
        public int NewAttackCapacity { get; set; }
        public int NewDefenseCapacity { get; set; }
    }

    public class LeaderDesignationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
