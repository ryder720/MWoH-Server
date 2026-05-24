using MwohServer.Models;
using System.Collections.Generic;

namespace MwohServer.Services
{
    public interface ICardGrowthEngine
    {
        EnhanceResult Enhance(int profileId, int targetCardId, string materialType, List<int> materialIds);
        FusionResult Fuse(int profileId, int baseCardId, int partnerCardId);
    }

    public class EnhanceResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public long RemainingSilver { get; set; }
        public PlayerCard? TargetCard { get; set; }
    }

    public class FusionResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public long RemainingSilver { get; set; }
        public PlayerCard? BaseCard { get; set; }
    }
}
