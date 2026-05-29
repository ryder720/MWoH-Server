using MwohServer.Models;
using System.Collections.Generic;

namespace MwohServer.Services
{
    public interface ILoginCommendationEngine
    {
        void ReloadTemplates();
        List<LoginCampaignTemplate> GetActiveCampaigns();
        List<PlayerLoginCommendationDto> GetPlayerProgress(int profileId);
        LoginProcessResult ProcessDailyLogin(int profileId);
    }

    public class PlayerLoginCommendationDto
    {
        public LoginCampaignTemplate Campaign { get; set; } = new();
        public int TotalLogins { get; set; }
        public bool AlreadyLoggedToday { get; set; }
        public List<int> ClaimedDays { get; set; } = new();
        public int NextDayToClaim { get; set; }
        public int SecondsUntilReset { get; set; }
    }

    public class LoginProcessResult
    {
        public bool UnlockedReward { get; set; }
        public string CampaignTitle { get; set; } = string.Empty;
        public int DayNumber { get; set; }
        public string RewardText { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public long SilverBalance { get; set; }
        public int RallyPoints { get; set; }
        public long MobaCoinBalance { get; set; }
    }
}
