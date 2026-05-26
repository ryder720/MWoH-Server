using MwohServer.Models;
using System.Collections.Generic;

namespace MwohServer.Services
{
    public interface IGachaSummoner
    {
        GachaResult Pull(int profileId, int packId, string currencyType, int pullCount);
    }

    public class GachaResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public long NewMobaCoins { get; set; }
        public long NewSilver { get; set; }
        public int NewRallyPoints { get; set; }
        public List<PlayerCard> PulledCards { get; set; } = new();
    }
}
