using MwohServer.Models;
using System.Collections.Generic;

namespace MwohServer.Services
{
    public interface IResourceVaultEngine
    {
        RedeemResult Redeem(int profileId, string groupKey);
        DonateResult Donate(int profileId, string groupKey);
    }

    public class RedeemResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public Dictionary<string, int> Redemptions { get; set; } = new Dictionary<string, int>();
        public List<UpdatedResourceDto> UpdatedResources { get; set; } = new List<UpdatedResourceDto>();
    }

    public class DonateResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public long SilverBalance { get; set; }
        public List<UpdatedResourceDto> UpdatedResources { get; set; } = new List<UpdatedResourceDto>();
    }

    public class UpdatedResourceDto
    {
        public int Id { get; set; }
        public int Qty { get; set; }
    }
}
