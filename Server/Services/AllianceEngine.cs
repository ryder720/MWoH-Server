using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MwohServer.Data;
using MwohServer.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MwohServer.Services
{
    public class AllianceEngine : IAllianceEngine
    {
        private readonly ILogger<AllianceEngine> _logger;
        private readonly MwohDbContext _dbContext;

        // Curve fitted level-rating requirements (Levels 1 to 120)
        private static readonly Dictionary<int, int> AllianceRatingRequirements = new Dictionary<int, int>
        {
            { 1, 0 },
            { 2, 5 },
            { 3, 15 },
            { 4, 30 },
            { 5, 50 },
            { 6, 80 },
            { 7, 120 },
            { 8, 170 },
            { 9, 230 },
            { 10, 300 },
            { 11, 390 },
            { 12, 500 },
            { 13, 630 },
            { 14, 780 },
            { 15, 950 },
            { 16, 1140 },
            { 17, 1350 },
            { 18, 1580 },
            { 19, 1830 },
            { 20, 2100 },
            { 21, 2430 },
            { 22, 2820 },
            { 23, 3270 },
            { 24, 3780 },
            { 25, 4350 },
            { 26, 4980 },
            { 27, 5670 },
            { 28, 6420 },
            { 29, 7230 },
            { 30, 8100 },
            { 31, 9090 },
            { 32, 10200 },
            { 33, 11430 },
            { 34, 12780 },
            { 35, 14250 },
            { 36, 15840 },
            { 37, 17550 },
            { 38, 19380 },
            { 39, 21330 },
            { 40, 23400 },
            { 41, 25590 },
            { 42, 27900 },
            { 43, 30330 },
            { 44, 32880 },
            { 45, 35550 },
            { 46, 38340 },
            { 47, 41250 },
            { 48, 44280 },
            { 49, 47430 },
            { 50, 52400 },
            { 51, 58080 },
            { 52, 63760 },
            { 53, 69440 },
            { 54, 75120 },
            { 55, 80800 },
            { 56, 87212 },
            { 57, 93625 },
            { 58, 100038 },
            { 59, 106450 },
            { 60, 113400 },
            { 61, 121419 },
            { 62, 129438 },
            { 63, 137457 },
            { 64, 145487 },
            { 65, 155110 },
            { 66, 164732 },
            { 67, 174354 },
            { 68, 183977 },
            { 69, 195243 },
            { 70, 206510 },
            { 71, 217776 },
            { 72, 231285 },
            { 73, 244602 },
            { 74, 259966 },
            { 75, 275599 },
            { 76, 294638 },
            { 77, 313678 },
            { 78, 332717 },
            { 79, 351757 },
            { 80, 370796 },
            { 81, 397170 },
            { 82, 423544 },
            { 83, 449918 },
            { 84, 476292 },
            { 85, 512382 },
            { 86, 548472 },
            { 87, 584562 },
            { 88, 620652 },
            { 89, 656742 },
            { 90, 700421 },
            { 91, 744100 },
            { 92, 787780 },
            { 93, 831459 },
            { 94, 884989 },
            { 95, 943948 },
            { 96, 1002924 },
            { 97, 1061900 },
            { 98, 1134191 },
            { 99, 1206482 },
            { 100, 1278774 },
            { 101, 1351065 },
            { 102, 1423356 },
            { 103, 1509322 },
            { 104, 1600344 },
            { 105, 1695954 },
            { 106, 1797101 },
            { 107, 1890000 },
            { 108, 2013597 },
            { 109, 2129657 },
            { 110, 2250607 },
            { 111, 2376557 },
            { 112, 2507507 },
            { 113, 2643457 },
            { 114, 2784407 },
            { 115, 2930357 },
            { 116, 3081307 },
            { 117, 3237257 },
            { 118, 3398207 },
            { 119, 3564157 },
            { 120, 3735107 }
        };

        public AllianceEngine(ILogger<AllianceEngine> logger, MwohDbContext dbContext)
        {
            _logger = logger;
            _dbContext = dbContext;
        }

        public AllianceCreateResult CreateAlliance(int leaderProfileId, string name, string slogan)
        {
            var profile = _dbContext.Profiles
                .Include(p => p.Alliance)
                .FirstOrDefault(p => p.Id == leaderProfileId);

            if (profile == null)
            {
                return new AllianceCreateResult { Success = false, Message = "Profile mismatch." };
            }

            if (profile.AllianceId != null)
            {
                return new AllianceCreateResult { Success = false, Message = "⚠️ COMMISSION BLOCK // You are already in an active Alliance." };
            }

            if (profile.Level < 20)
            {
                return new AllianceCreateResult { Success = false, Message = "⚠️ LEVEL INSUFFICIENT // Forming an Alliance requires S.H.I.E.L.D. Clearance Level 20+." };
            }

            // Count accepted allies (friends)
            int alliesCount = _dbContext.ShieldTeamMembers
                .Count(m => (m.ProfileId == leaderProfileId || m.MemberProfileId == leaderProfileId) && m.Status == "Accepted");

            if (alliesCount < 10)
            {
                return new AllianceCreateResult { Success = false, Message = $"⚠️ RECRUITS NEEDED // Forming an Alliance requires at least 10 accepted allies (Current: {alliesCount})." };
            }

            if (string.IsNullOrWhiteSpace(name) || name.Length > 23)
            {
                return new AllianceCreateResult { Success = false, Message = "⚠️ NAME MALFORMED // Name must be between 1 and 23 characters." };
            }

            // Verify unique name
            bool nameExists = _dbContext.Alliances.Any(a => a.Name.ToLower() == name.Trim().ToLower());
            if (nameExists)
            {
                return new AllianceCreateResult { Success = false, Message = "⚠️ NAME TAKEN // An Alliance with this designation is already registered in the tactical mainframe." };
            }

            var alliance = new Alliance
            {
                Name = name.Trim(),
                Slogan = string.IsNullOrWhiteSpace(slogan) ? "Assemble!" : slogan.Trim(),
                LeaderProfileId = leaderProfileId,
                Level = 1,
                DonatedSilver = 0,
                Rating = 0,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.Alliances.Add(alliance);
            _dbContext.SaveChanges(); // Persist to get ID

            // Set member properties for the founder
            profile.AllianceId = alliance.Id;
            profile.AllianceRole = "Leader";
            profile.AllianceJoinedAt = DateTime.UtcNow;
            profile.AllianceDonatedSilver = 0;

            _dbContext.SaveChanges();

            _logger.LogInformation($"[AllianceEngine] Alliance '{alliance.Name}' successfully created by profile {leaderProfileId}.");

            return new AllianceCreateResult
            {
                Success = true,
                Message = $"📁 DIVISION COMMISSIONED // Welcome to the S.H.I.E.L.D. Division '{alliance.Name}'!",
                Alliance = alliance
            };
        }

        public AllianceDonateResult DonateSilver(int profileId, long silverAmount)
        {
            if (silverAmount <= 0)
            {
                return new AllianceDonateResult { Success = false, Message = "⚠️ TRANSACTION REJECTED // Silver donation amount must be greater than zero." };
            }

            var profile = _dbContext.Profiles
                .Include(p => p.Alliance)
                .FirstOrDefault(p => p.Id == profileId);

            if (profile == null || profile.AllianceId == null || profile.Alliance == null)
            {
                return new AllianceDonateResult { Success = false, Message = "⚠️ TRANSACTION BLOCKED // You must be a member of an active Alliance." };
            }

            if (profile.SilverBalance < silverAmount)
            {
                return new AllianceDonateResult { Success = false, Message = "⚠️ INSUFFICIENT SILVER // Your personal S.H.I.E.L.D. wallet has insufficient credits." };
            }

            // Deduct from profile and add to Alliance
            profile.SilverBalance -= silverAmount;
            profile.AllianceDonatedSilver += silverAmount;
            profile.Alliance.DonatedSilver += silverAmount;

            // Rating updates: 1 Rating per 1,000 Silver
            profile.Alliance.Rating = (int)(profile.Alliance.DonatedSilver / 1000);

            // Level Up logic
            int oldLevel = profile.Alliance.Level;
            int newLevel = oldLevel;

            while (newLevel < 120 && AllianceRatingRequirements.TryGetValue(newLevel + 1, out var reqRating) && profile.Alliance.Rating >= reqRating)
            {
                newLevel++;
            }

            if (newLevel > oldLevel)
            {
                profile.Alliance.Level = newLevel;
            }

            _dbContext.SaveChanges();

            var msg = $"🧪 CONTRIBUTION RECEIVED // Contributed +{silverAmount.ToString("N0")} Silver to '{profile.Alliance.Name}' mainframe.";
            if (newLevel > oldLevel)
            {
                msg += $" ✨ DIVISION LEVEL UP // '{profile.Alliance.Name}' has reached Clearance Level {newLevel}!";
            }

            return new AllianceDonateResult
            {
                Success = true,
                Message = msg,
                NewPersonalSilver = profile.SilverBalance,
                NewAllianceDonatedSilver = profile.AllianceDonatedSilver,
                NewAllianceLevel = profile.Alliance.Level,
                NewAllianceRating = profile.Alliance.Rating
            };
        }

        public AllianceDonateResult DonateResourceGroup(int profileId, string groupKey)
        {
            var profile = _dbContext.Profiles
                .Include(p => p.Alliance)
                .Include(p => p.InventoryItems)
                    .ThenInclude(pi => pi.ItemTemplate)
                .FirstOrDefault(p => p.Id == profileId);

            if (profile == null || profile.AllianceId == null || profile.Alliance == null)
            {
                return new AllianceDonateResult { Success = false, Message = "⚠️ TRANSACTION BLOCKED // You must be a member of an active Alliance." };
            }

            // Identify resource items matching group key
            var groupResources = profile.InventoryItems
                .Where(pi => pi.ItemTemplate != null && pi.ItemTemplate.Type == "Resource" && (
                    (groupKey == "StormsCape" && pi.ItemTemplate.Name.Contains("Storm's") && pi.ItemTemplate.Name.Contains("Cape")) ||
                    (groupKey == "Suitcase" && pi.ItemTemplate.Name.Contains("Suitcase")) ||
                    (groupKey == "SwordOfProficiency" && pi.ItemTemplate.Name.Contains("Sword")) ||
                    (groupKey == "AssassinsChoker" && pi.ItemTemplate.Name.Contains("Assassin's") && pi.ItemTemplate.Name.Contains("Choker")) ||
                    (groupKey == "ChainBelt" && pi.ItemTemplate.Name.Contains("Chain Belt")) ||
                    (groupKey == "Geirr" && pi.ItemTemplate.Name.Contains("Geirr")) ||
                    (groupKey == "ProjectileArray" && pi.ItemTemplate.Name.Contains("Projectile Array"))
                ) && pi.Quantity > 0).ToList();

            if (groupResources.Count == 0)
            {
                return new AllianceDonateResult { Success = false, Message = "⚠️ VAULT STOCK EMPTY // You have no items in this resource group to donate." };
            }

            long totalSilverValue = 0;
            int totalItemsCount = 0;

            foreach (var pi in groupResources)
            {
                int qty = pi.Quantity;
                int baseVal = pi.ItemTemplate?.EffectValue ?? 2000;
                
                totalSilverValue += (long)qty * baseVal;
                totalItemsCount += qty;
                
                pi.Quantity = 0; // Deplete inventory
            }

            // Contribute to Alliance
            profile.AllianceDonatedSilver += totalSilverValue;
            profile.Alliance.DonatedSilver += totalSilverValue;

            // Recalculate rating and levels
            profile.Alliance.Rating = (int)(profile.Alliance.DonatedSilver / 1000);
            int oldLevel = profile.Alliance.Level;
            int newLevel = oldLevel;

            while (newLevel < 120 && AllianceRatingRequirements.TryGetValue(newLevel + 1, out var reqRating) && profile.Alliance.Rating >= reqRating)
            {
                newLevel++;
            }

            if (newLevel > oldLevel)
            {
                profile.Alliance.Level = newLevel;
            }

            _dbContext.SaveChanges();

            var msg = $"🧪 DEPOSIT SECURED // Deposited {totalItemsCount} rare drops. Credited +{totalSilverValue.ToString("N0")} Silver value directly to S.H.I.E.L.D. Division mainframe!";
            if (newLevel > oldLevel)
            {
                msg += $" ✨ DIVISION LEVEL UP // '{profile.Alliance.Name}' has reached Clearance Level {newLevel}!";
            }

            return new AllianceDonateResult
            {
                Success = true,
                Message = msg,
                NewPersonalSilver = profile.SilverBalance,
                NewAllianceDonatedSilver = profile.AllianceDonatedSilver,
                NewAllianceLevel = profile.Alliance.Level,
                NewAllianceRating = profile.Alliance.Rating
            };
        }

        public AllianceUpgradeResult PurchaseUpgrade(int leaderProfileId, string upgradeType)
        {
            var profile = _dbContext.Profiles
                .Include(p => p.Alliance)
                .FirstOrDefault(p => p.Id == leaderProfileId);

            if (profile == null || profile.AllianceId == null || profile.Alliance == null)
            {
                return new AllianceUpgradeResult { Success = false, Message = "Profile mismatch." };
            }

            var alliance = profile.Alliance;

            // Authorization: must be Leader or Vice-Leader to buy upgrades
            if (profile.AllianceRole != "Leader" && profile.AllianceRole != "Vice-Leader")
            {
                return new AllianceUpgradeResult { Success = false, Message = "⚠️ COMMAND DENIED // Only Division Leaders or Vice-Leaders can initialize upgrade sequences." };
            }

            long cost = 0;
            string upgradeName = "";

            if (upgradeType == "ProtectionWall")
            {
                if (alliance.ProtectionWallCount >= 6)
                {
                    return new AllianceUpgradeResult { Success = false, Message = "⚠️ CAP REACHED // Protection wall grid is at maximum S.H.I.E.L.D. clearance capacity (6 walls)." };
                }
                cost = 50000;
                upgradeName = "Protection Wall Grid Segment";
            }
            else if (upgradeType == "SpeedAdaptor")
            {
                if (alliance.SpeedAdaptorLevel >= 3)
                {
                    return new AllianceUpgradeResult { Success = false, Message = "⚠️ CAP REACHED // Speed alignment adaptor has reached peak operational Level 3." };
                }
                cost = alliance.SpeedAdaptorLevel switch
                {
                    0 => 3000000,
                    1 => 4000000,
                    2 => 6000000,
                    _ => 0
                };
                upgradeName = $"Speed Core Adaptor Level {alliance.SpeedAdaptorLevel + 1}";
            }
            else if (upgradeType == "BruiserAdaptor")
            {
                if (alliance.BruiserAdaptorLevel >= 3)
                {
                    return new AllianceUpgradeResult { Success = false, Message = "⚠️ CAP REACHED // Bruiser alignment adaptor has reached peak operational Level 3." };
                }
                cost = alliance.BruiserAdaptorLevel switch
                {
                    0 => 3000000,
                    1 => 4000000,
                    2 => 6000000,
                    _ => 0
                };
                upgradeName = $"Bruiser Core Adaptor Level {alliance.BruiserAdaptorLevel + 1}";
            }
            else if (upgradeType == "TacticsAdaptor")
            {
                if (alliance.TacticsAdaptorLevel >= 3)
                {
                    return new AllianceUpgradeResult { Success = false, Message = "⚠️ CAP REACHED // Tactics alignment adaptor has reached peak operational Level 3." };
                }
                cost = alliance.TacticsAdaptorLevel switch
                {
                    0 => 3000000,
                    1 => 4000000,
                    2 => 6000000,
                    _ => 0
                };
                upgradeName = $"Tactics Core Adaptor Level {alliance.TacticsAdaptorLevel + 1}";
            }
            else
            {
                return new AllianceUpgradeResult { Success = false, Message = "⚠️ INCORRECT DESIGNATION // Upgrade type unknown." };
            }

            if (alliance.DonatedSilver < cost)
            {
                return new AllianceUpgradeResult { Success = false, Message = $"⚠️ ALLIANCE TREASURY EMPTY // Core upgrade requires {cost.ToString("N0")} Silver. Division Bank has: {alliance.DonatedSilver.ToString("N0")}." };
            }

            // Deduct cost and apply level up
            alliance.DonatedSilver -= cost;

            if (upgradeType == "ProtectionWall")
            {
                alliance.ProtectionWallCount++;
            }
            else if (upgradeType == "SpeedAdaptor")
            {
                alliance.SpeedAdaptorLevel++;
            }
            else if (upgradeType == "BruiserAdaptor")
            {
                alliance.BruiserAdaptorLevel++;
            }
            else if (upgradeType == "TacticsAdaptor")
            {
                alliance.TacticsAdaptorLevel++;
            }

            _dbContext.SaveChanges();

            return new AllianceUpgradeResult
            {
                Success = true,
                Message = $"🔧 CORE UPGRADE ENGAGED // Successfully integrated '{upgradeName}'! Spent {cost.ToString("N0")} Silver from Division treasury.",
                Alliance = alliance
            };
        }

        public bool CreateJoinRequest(int profileId, int allianceId)
        {
            var profile = _dbContext.Profiles.FirstOrDefault(p => p.Id == profileId);
            var alliance = _dbContext.Alliances
                .Include(a => a.Members)
                .FirstOrDefault(a => a.Id == allianceId);

            if (profile == null || alliance == null || profile.AllianceId != null)
            {
                return false;
            }

            // Verify member cap
            int currentMembers = alliance.Members.Count;
            int maxCap = GetMaxMembersLimit(alliance.Level);
            if (currentMembers >= maxCap)
            {
                return false;
            }

            // Verify if a request is already pending
            bool requestExists = _dbContext.AllianceJoinRequests
                .Any(r => r.AllianceId == allianceId && r.PlayerProfileId == profileId && r.Status == "Pending");

            if (requestExists)
            {
                return false;
            }

            var request = new AllianceJoinRequest
            {
                AllianceId = allianceId,
                PlayerProfileId = profileId,
                Status = "Pending",
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.AllianceJoinRequests.Add(request);
            _dbContext.SaveChanges();

            return true;
        }

        public bool ProcessJoinRequest(int leaderProfileId, int requestId, bool accept)
        {
            var leader = _dbContext.Profiles.FirstOrDefault(p => p.Id == leaderProfileId);
            var request = _dbContext.AllianceJoinRequests
                .Include(r => r.PlayerProfile)
                .Include(r => r.Alliance)
                    .ThenInclude(a => a!.Members)
                .FirstOrDefault(r => r.Id == requestId);

            if (leader == null || request == null || request.Status != "Pending" || request.Alliance == null || request.PlayerProfile == null)
            {
                return false;
            }

            // Authorization check
            if (leader.AllianceId != request.AllianceId || (leader.AllianceRole != "Leader" && leader.AllianceRole != "Vice-Leader"))
            {
                return false;
            }

            if (!accept)
            {
                request.Status = "Declined";
                _dbContext.SaveChanges();
                return true;
            }

            // Verify member limit again
            var alliance = request.Alliance;
            int currentMembers = alliance.Members.Count;
            int maxCap = GetMaxMembersLimit(alliance.Level);

            if (currentMembers >= maxCap)
            {
                // Cannot accept since it is full
                return false;
            }

            // Join success
            request.Status = "Accepted";
            
            var profile = request.PlayerProfile;
            profile.AllianceId = alliance.Id;
            profile.AllianceRole = "Member";
            profile.AllianceJoinedAt = DateTime.UtcNow;
            profile.AllianceDonatedSilver = 0;

            // Cancel any other pending requests for this player
            var otherRequests = _dbContext.AllianceJoinRequests
                .Where(r => r.PlayerProfileId == profile.Id && r.Id != requestId && r.Status == "Pending");
            
            foreach (var o in otherRequests)
            {
                o.Status = "Declined";
            }

            _dbContext.SaveChanges();

            return true;
        }

        public bool AssignMemberRole(int leaderProfileId, int memberProfileId, string role)
        {
            var leader = _dbContext.Profiles.FirstOrDefault(p => p.Id == leaderProfileId);
            var member = _dbContext.Profiles.FirstOrDefault(p => p.Id == memberProfileId);

            if (leader == null || member == null || leader.AllianceId == null || leader.AllianceId != member.AllianceId)
            {
                return false;
            }

            var alliance = _dbContext.Alliances
                .Include(a => a.Members)
                .FirstOrDefault(a => a.Id == leader.AllianceId);

            if (alliance == null) return false;

            // Only Leader can assign command roles
            if (leader.AllianceRole != "Leader")
            {
                return false;
            }

            if (role != "Member" && role != "Vice-Leader" && role != "Offense-Leader" && role != "Defense-Leader")
            {
                return false;
            }

            // Verify leadership cap limit based on Level
            if (role != "Member")
            {
                int currentLeadersCount = alliance.Members
                    .Count(m => m.AllianceRole == "Vice-Leader" || m.AllianceRole == "Offense-Leader" || m.AllianceRole == "Defense-Leader");
                
                // If moving a leader to a different leader role, count remains the same.
                bool isAlreadyLeader = member.AllianceRole == "Vice-Leader" || member.AllianceRole == "Offense-Leader" || member.AllianceRole == "Defense-Leader";
                int targetLeadersCount = currentLeadersCount + (isAlreadyLeader ? 0 : 1);

                int maxLeadersCap = GetMaxLeadersLimit(alliance.Level);

                if (targetLeadersCount > maxLeadersCap)
                {
                    return false; // Leadership cap exceeded
                }
            }

            member.AllianceRole = role;
            _dbContext.SaveChanges();

            return true;
        }

        public bool LeaveAlliance(int profileId)
        {
            var profile = _dbContext.Profiles
                .Include(p => p.Alliance)
                .FirstOrDefault(p => p.Id == profileId);

            if (profile == null || profile.AllianceId == null || profile.Alliance == null)
            {
                return false;
            }

            if (profile.SilverBalance < 20000)
            {
                return false; // Insufficient fee
            }

            // Leaders cannot simply leave; they must disband or delegate leadership first!
            if (profile.AllianceRole == "Leader")
            {
                return false;
            }

            profile.SilverBalance -= 20000;
            profile.AllianceId = null;
            profile.AllianceRole = null;
            profile.AllianceDonatedSilver = 0;
            profile.AllianceJoinedAt = null;

            _dbContext.SaveChanges();

            return true;
        }

        public bool DisbandAlliance(int leaderProfileId)
        {
            var profile = _dbContext.Profiles
                .Include(p => p.Alliance)
                    .ThenInclude(a => a!.Members)
                .FirstOrDefault(p => p.Id == leaderProfileId);

            if (profile == null || profile.AllianceId == null || profile.Alliance == null)
            {
                return false;
            }

            if (profile.AllianceRole != "Leader")
            {
                return false;
            }

            var alliance = profile.Alliance;

            // Clear members
            foreach (var m in alliance.Members)
            {
                m.AllianceId = null;
                m.AllianceRole = null;
                m.AllianceJoinedAt = null;
                m.AllianceDonatedSilver = 0;
            }

            // Remove join requests
            var requests = _dbContext.AllianceJoinRequests.Where(r => r.AllianceId == alliance.Id);
            _dbContext.AllianceJoinRequests.RemoveRange(requests);

            _dbContext.Alliances.Remove(alliance);
            _dbContext.SaveChanges();

            return true;
        }

        public AllianceStatsBoost GetAllianceCombatBoosts(int profileId, string cardAlignment)
        {
            var boost = new AllianceStatsBoost();
            var logs = new List<string>();

            var profile = _dbContext.Profiles
                .Include(p => p.Alliance)
                .FirstOrDefault(p => p.Id == profileId);

            if (profile == null || profile.AllianceId == null || profile.Alliance == null)
            {
                return boost;
            }

            var alliance = profile.Alliance;

            // 1. Role-based Boosts
            if (profile.AllianceRole == "Leader")
            {
                boost.AtkModifier += 0.10;
                boost.DefModifier += 0.10;
                logs.Add("[ALLIANCE ROLE] Agent role: Alliance Leader. Decks ATK & DEF increased by 10%!");
            }
            else if (profile.AllianceRole == "Vice-Leader")
            {
                boost.AtkModifier += 0.05;
                boost.DefModifier += 0.05;
                logs.Add("[ALLIANCE ROLE] Agent role: Alliance Vice-Leader. Decks ATK & DEF increased by 5%!");
            }
            else if (profile.AllianceRole == "Offense-Leader")
            {
                boost.AtkModifier += 0.10;
                logs.Add("[ALLIANCE ROLE] Agent role: Offense Leader. Deck ATK increased by 10%!");
            }
            else if (profile.AllianceRole == "Defense-Leader")
            {
                boost.DefModifier += 0.10;
                logs.Add("[ALLIANCE ROLE] Agent role: Defense Leader. Deck DEF increased by 10%!");
            }

            // 2. Protection Wall Boost (1% DEF per wall, up to 5% max active)
            if (alliance.ProtectionWallCount > 0)
            {
                int activeWalls = Math.Min(5, alliance.ProtectionWallCount);
                double wallBonus = activeWalls * 0.01;
                boost.DefModifier += wallBonus;
                logs.Add($"[ALLIANCE RESEARCH] Built walls: {alliance.ProtectionWallCount} ({activeWalls} active). DEF increased by {activeWalls}%!");
            }

            // 3. Alignment Adaptors
            if (cardAlignment == "Speed" && alliance.SpeedAdaptorLevel > 0)
            {
                double adaptorBonus = alliance.SpeedAdaptorLevel switch
                {
                    1 => 0.05,
                    2 => 0.06,
                    3 => 0.07,
                    _ => 0.0
                };
                boost.AtkModifier += adaptorBonus;
                boost.DefModifier += adaptorBonus;
                logs.Add($"[ALLIANCE RESEARCH] Speed Adaptor Lv {alliance.SpeedAdaptorLevel} active. Speed Alignment cards ATK & DEF boosted by {adaptorBonus * 100}%!");
            }
            else if (cardAlignment == "Bruiser" && alliance.BruiserAdaptorLevel > 0)
            {
                double adaptorBonus = alliance.BruiserAdaptorLevel switch
                {
                    1 => 0.05,
                    2 => 0.06,
                    3 => 0.07,
                    _ => 0.0
                };
                boost.AtkModifier += adaptorBonus;
                boost.DefModifier += adaptorBonus;
                logs.Add($"[ALLIANCE RESEARCH] Bruiser Adaptor Lv {alliance.BruiserAdaptorLevel} active. Bruiser Alignment cards ATK & DEF boosted by {adaptorBonus * 100}%!");
            }
            else if (cardAlignment == "Tactics" && alliance.TacticsAdaptorLevel > 0)
            {
                double adaptorBonus = alliance.TacticsAdaptorLevel switch
                {
                    1 => 0.05,
                    2 => 0.06,
                    3 => 0.07,
                    _ => 0.0
                };
                boost.AtkModifier += adaptorBonus;
                boost.DefModifier += adaptorBonus;
                logs.Add($"[ALLIANCE RESEARCH] Tactics Adaptor Lv {alliance.TacticsAdaptorLevel} active. Tactics Alignment cards ATK & DEF boosted by {adaptorBonus * 100}%!");
            }

            boost.Logs = string.Join("\n", logs);
            return boost;
        }

        // Helper calculations matching the plan formulas
        private int GetMaxMembersLimit(int level)
        {
            if (level <= 1) return 10;
            if (level <= 11) return 10 + (level - 1);
            if (level <= 30) return 20 + (int)Math.Floor((level - 11) / 2.0);
            if (level <= 34) return 30;
            if (level <= 54) return 31 + (int)Math.Floor((level - 35) / 5.0);
            if (level <= 109) return 35 + (int)Math.Floor((level - 55) / 10.0);
            return 40;
        }

        private int GetMaxLeadersLimit(int level)
        {
            if (level <= 15) return 3;
            if (level <= 22) return 4;
            if (level <= 30) return 5;
            if (level <= 49) return 6;
            if (level <= 108) return 7;
            return 8;
        }
    }
}
