using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using MwohServer.Data;
using MwohServer.Filters;
using MwohServer.Services;
using MwohServer.Models;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Collections.Generic;

namespace MwohServer.Controllers
{
    [ApiController]
    [Route("ultimate")]
    [ServiceFilter(typeof(GAuthValidationFilter))] // Mirror standard Cygames Controller Filters
    public class EventsController : ControllerBase
    {
        private readonly ILogger<EventsController> _logger;
        private readonly MwohDbContext _dbContext;
        private readonly IEventEngine _eventEngine;
        private readonly IAuthService _authService;
        private readonly IWarEventEngine _warEventEngine;

        public EventsController(
            ILogger<EventsController> logger,
            MwohDbContext dbContext,
            IEventEngine eventEngine,
            IAuthService authService,
            IWarEventEngine warEventEngine)
        {
            _logger = logger;
            _dbContext = dbContext;
            _eventEngine = eventEngine;
            _authService = authService;
            _warEventEngine = warEventEngine;
        }

        private UserAccount ResolveCurrentUser()
        {
            var gauthToken = HttpContext.Items.TryGetValue("GAuthToken", out var tokenObj) ? tokenObj as string : null;
            return _authService.ResolveContext(null, Request.Cookies["sid"], gauthToken);
        }

        [HttpGet("event")]
        public IActionResult ServeEventPortal()
        {
            _logger.LogInformation("[EventsController] ServeEventPortal called.");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            var profile = _dbContext.Profiles
                .Include(p => p.EventProgresses)
                .FirstOrDefault(p => p.Id == profileId);

            if (profile == null)
            {
                return RedirectToAction("ServeGameTopPage", "Cygames");
            }

            var activeEvent = _eventEngine.GetActiveEvent();

            // Calculate player active deck cost
            var activeDeck = _dbContext.PlayerCards
                .Include(c => c.CardTemplate)
                .Where(c => c.PlayerProfileId == profile.Id && c.IsInAttackDeck)
                .ToList();

            if (!activeDeck.Any())
            {
                var leader = _dbContext.PlayerCards
                    .Include(c => c.CardTemplate)
                    .FirstOrDefault(c => c.PlayerProfileId == profile.Id && c.IsLeader);
                if (leader != null) activeDeck.Add(leader);
                else
                {
                    var topCard = _dbContext.PlayerCards
                        .Include(c => c.CardTemplate)
                        .OrderByDescending(c => c.CurrentAtk)
                        .FirstOrDefault(c => c.PlayerProfileId == profile.Id);
                    if (topCard != null) activeDeck.Add(topCard);
                }
            }
            int baseApCost = activeDeck.Sum(c => c.CardTemplate?.PowerRequirement ?? 10);
            if (baseApCost <= 0) baseApCost = 10;

            var replacements = new Dictionary<string, string>
            {
                { "agentName", profile.Nickname },
                { "level", profile.Level.ToString() },
                { "energyCur", profile.EnergyCurrent.ToString() },
                { "energyMax", profile.EnergyMax.ToString() },
                { "energyPct", ((double)profile.EnergyCurrent / profile.EnergyMax * 100).ToString("N0") },
                { "baseApCost", baseApCost.ToString() }
            };

            // Default Raid Replacements (prevent template parser errors)
            replacements.Add("easyLevel", "1");
            replacements.Add("easyPartName", "Left Cosmic Wing");
            replacements.Add("easyMainHpCur", "0");
            replacements.Add("easyMainHpMax", "1");
            replacements.Add("easyPartHpCur", "0");
            replacements.Add("easyPartHpMax", "1");
            replacements.Add("easyMainHpPct", "0");
            replacements.Add("easyPartHpPct", "0");
            
            replacements.Add("medLevel", "5");
            replacements.Add("medPartName", "Command Helmet");
            replacements.Add("medMainHpCur", "0");
            replacements.Add("medMainHpMax", "1");
            replacements.Add("medPartHpCur", "0");
            replacements.Add("medPartHpMax", "1");
            replacements.Add("medMainHpPct", "0");
            replacements.Add("medPartHpPct", "0");

            replacements.Add("hardLevel", "15");
            replacements.Add("hardPartName", "Power Reactor Core");
            replacements.Add("hardMainHpCur", "0");
            replacements.Add("hardMainHpMax", "1");
            replacements.Add("hardPartHpCur", "0");
            replacements.Add("hardPartHpMax", "1");
            replacements.Add("hardMainHpPct", "0");
            replacements.Add("hardPartHpPct", "0");

            replacements.Add("hasHelper", "false");
            replacements.Add("helperId", "0");
            replacements.Add("helperName", "");
            replacements.Add("helperLevel", "0");
            replacements.Add("helperCardTitle", "");
            replacements.Add("helperCardImage", "");
            replacements.Add("helperSkillName", "");
            replacements.Add("helperSkillEffect", "");

            if (activeEvent == null)
            {
                // Standby mode
                replacements.Add("isStandby", "true");
                replacements.Add("isUpcoming", "false");
                replacements.Add("isActive", "false");
                replacements.Add("isCalculating", "false");
                replacements.Add("isCompleted", "false");
                replacements.Add("eventId", "");
                replacements.Add("eventTitle", "S.H.I.E.L.D. STANDBY MODE");
                replacements.Add("eventDesc", "The S.H.I.E.L.D. Mainframe scanning matrix is currently monitoring global sectors. No active incursions or priority threat targets detected in this window.");
                replacements.Add("eventType", "Standby");
                replacements.Add("points", "0");
                replacements.Add("rank", "N/A");
                replacements.Add("timerLabel", "SCAN RE-CALIBRATION IN");
                replacements.Add("timerSeconds", "3600");
                replacements.Add("leaderboardHtml", "<div class='no-records-card'>Rankings archive locked during standby periods.</div>");
                replacements.Add("milestonesHtml", "<div class='no-records-card'>Special milestone drop supplies offline.</div>");
            }
            else
            {
                var progress = _eventEngine.GetPlayerProgress(profile.Id, activeEvent.Id);
                var state = _eventEngine.GetEventState(activeEvent);
                var rank = _eventEngine.GetPlayerRank(profile.Id, activeEvent.Id);

                replacements.Add("isStandby", "false");
                replacements.Add("isUpcoming", (state == "Upcoming").ToString().ToLower());
                replacements.Add("isActive", (state == "Active").ToString().ToLower());
                replacements.Add("isCalculating", (state == "Calculating").ToString().ToLower());
                replacements.Add("isCompleted", (state == "Completed").ToString().ToLower());
                replacements.Add("eventId", activeEvent.Id);
                replacements.Add("eventTitle", activeEvent.Title);
                replacements.Add("eventDesc", activeEvent.Description);
                replacements.Add("eventType", activeEvent.EventType);
                replacements.Add("points", progress.Points.ToString("N0"));
                replacements.Add("rank", rank.ToString());
                
                var timerLabel = state switch
                {
                    "Upcoming" => "DEPLOYMENT INITIATED IN",
                    "Active" => "BATTLE OPERATION WINDOW",
                    "Calculating" => "RANK VERIFICATION PERIOD",
                    _ => "CAMPAIGN CONCLUDED"
                };
                replacements.Add("timerLabel", timerLabel);

                var timerSec = GetTimerSeconds(activeEvent, state);
                replacements.Add("timerSeconds", timerSec.ToString());

                // Compile Leaderboard & Milestones HTML
                replacements.Add("leaderboardHtml", RenderLeaderboardHtml(activeEvent.Id, profile.Id));
                replacements.Add("milestonesHtml", RenderMilestonesHtml(activeEvent, progress));

                // Populate Raid Info if event type is Raid
                if (string.Equals(activeEvent.EventType, "Raid", StringComparison.OrdinalIgnoreCase))
                {
                    var raidState = _eventEngine.GetRaidState(profile.Id, activeEvent.Id);
                    
                    // Easy Target
                    replacements["easyLevel"] = raidState.EasyTarget.Level.ToString();
                    replacements["easyPartName"] = raidState.EasyTarget.BodyPartName;
                    replacements["easyMainHpCur"] = raidState.EasyTarget.MainHpCurrent.ToString("N0");
                    replacements["easyMainHpMax"] = raidState.EasyTarget.MainHpMax.ToString("N0");
                    replacements["easyPartHpCur"] = raidState.EasyTarget.BodyPartHpCurrent.ToString("N0");
                    replacements["easyPartHpMax"] = raidState.EasyTarget.BodyPartHpMax.ToString("N0");
                    replacements["easyMainHpPct"] = ((double)raidState.EasyTarget.MainHpCurrent / Math.Max(1, raidState.EasyTarget.MainHpMax) * 100).ToString("N0");
                    replacements["easyPartHpPct"] = ((double)raidState.EasyTarget.BodyPartHpCurrent / Math.Max(1, raidState.EasyTarget.BodyPartHpMax) * 100).ToString("N0");

                    // Medium Target
                    replacements["medLevel"] = raidState.MediumTarget.Level.ToString();
                    replacements["medPartName"] = raidState.MediumTarget.BodyPartName;
                    replacements["medMainHpCur"] = raidState.MediumTarget.MainHpCurrent.ToString("N0");
                    replacements["medMainHpMax"] = raidState.MediumTarget.MainHpMax.ToString("N0");
                    replacements["medPartHpCur"] = raidState.MediumTarget.BodyPartHpCurrent.ToString("N0");
                    replacements["medPartHpMax"] = raidState.MediumTarget.BodyPartHpMax.ToString("N0");
                    replacements["medMainHpPct"] = ((double)raidState.MediumTarget.MainHpCurrent / Math.Max(1, raidState.MediumTarget.MainHpMax) * 100).ToString("N0");
                    replacements["medPartHpPct"] = ((double)raidState.MediumTarget.BodyPartHpCurrent / Math.Max(1, raidState.MediumTarget.BodyPartHpMax) * 100).ToString("N0");

                    // Hard Target
                    replacements["hardLevel"] = raidState.HardTarget.Level.ToString();
                    replacements["hardPartName"] = raidState.HardTarget.BodyPartName;
                    replacements["hardMainHpCur"] = raidState.HardTarget.MainHpCurrent.ToString("N0");
                    replacements["hardMainHpMax"] = raidState.HardTarget.MainHpMax.ToString("N0");
                    replacements["hardPartHpCur"] = raidState.HardTarget.BodyPartHpCurrent.ToString("N0");
                    replacements["hardPartHpMax"] = raidState.HardTarget.BodyPartHpMax.ToString("N0");
                    replacements["hardMainHpPct"] = ((double)raidState.HardTarget.MainHpCurrent / Math.Max(1, raidState.HardTarget.MainHpMax) * 100).ToString("N0");
                    replacements["hardPartHpPct"] = ((double)raidState.HardTarget.BodyPartHpCurrent / Math.Max(1, raidState.HardTarget.BodyPartHpMax) * 100).ToString("N0");

                    // Helper Target
                    if (raidState.HelperProfileId.HasValue)
                    {
                        var helperProfile = _dbContext.Profiles
                            .Include(p => p.Cards)
                                .ThenInclude(c => c.CardTemplate)
                            .FirstOrDefault(p => p.Id == raidState.HelperProfileId.Value);

                        if (helperProfile != null)
                        {
                            var helperCard = helperProfile.Cards.FirstOrDefault(c => c.IsLeader) 
                                ?? helperProfile.Cards.OrderByDescending(c => c.CurrentAtk).FirstOrDefault();

                            if (helperCard != null && helperCard.CardTemplate != null)
                            {
                                replacements["hasHelper"] = "true";
                                replacements["helperId"] = helperProfile.Id.ToString();
                                replacements["helperName"] = helperProfile.Nickname;
                                replacements["helperLevel"] = helperProfile.Level.ToString();
                                replacements["helperCardTitle"] = helperCard.CardTemplate.Title;
                                replacements["helperCardImage"] = "/images/cards/" + helperCard.CardTemplate.ImageFileName;
                                replacements["helperSkillName"] = helperCard.CardTemplate.AbilityName ?? "No Ability";
                                replacements["helperSkillEffect"] = helperCard.CardTemplate.AbilityEffect ?? "No Effect";
                            }
                        }
                    }
                }
            }

            return Content(RenderTemplate("event_hub.html", replacements), "text/html");
        }

        [HttpPost("event/claim_reward")]
        public IActionResult ClaimMilestoneReward([FromBody] ClaimRequest request)
        {
            _logger.LogInformation($"[EventsController] ClaimMilestoneReward called for event: {request.EventId}, tier: {request.TierIndex}");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            var result = _eventEngine.ClaimMilestoneReward(profileId, request.EventId, request.TierIndex);
            if (result.Success)
            {
                return Ok(new { success = true, message = result.Message });
            }
            return Ok(new { success = false, message = result.Message });
        }

        private long GetTimerSeconds(EventTemplate temp, string state)
        {
            var now = DateTime.UtcNow;
            if (state == "Upcoming") return Math.Max(0, (long)(temp.StartDate - now).TotalSeconds);
            if (state == "Active") return Math.Max(0, (long)(temp.EndDate - now).TotalSeconds);
            if (state == "Calculating") return Math.Max(0, (long)(temp.ResultDate - now).TotalSeconds);
            return 0;
        }

        private string RenderLeaderboardHtml(string eventId, int activeProfileId)
        {
            var leaderboard = _eventEngine.GetLeaderboard(eventId, 5);
            var leaderboardHtml = "";

            if (leaderboard.Any())
            {
                for (int i = 0; i < leaderboard.Count; i++)
                {
                    var entry = leaderboard[i];
                    var rank = i + 1;
                    var highlightClass = "";
                    var rankMedal = rank switch
                    {
                        1 => "🥇",
                        2 => "🥈",
                        3 => "🥉",
                        _ => "🎖️"
                    };

                    leaderboardHtml += $"""
                    <div class="leaderboard-row {highlightClass}">
                        <div style="display:flex; align-items:center; gap:10px;">
                            <span class="rank-badge">{rankMedal} #{rank}</span>
                            <div class="dossier-details">
                                <span class="dossier-name">{entry.Nickname}</span>
                                <span class="dossier-level">[Lv. {entry.Level}]</span>
                            </div>
                        </div>
                        <span class="dossier-points">{entry.Points:N0} PTS</span>
                    </div>
                    """;
                }
            }
            else
            {
                leaderboardHtml = "<div class='no-records-card'>No active S.H.I.E.L.D. operatives in ranking databanks.</div>";
            }

            return leaderboardHtml;
        }

        private string RenderMilestonesHtml(EventTemplate activeEvent, PlayerEventProgress progress)
        {
            List<MilestoneConfig> milestones;
            try
            {
                using (var doc = JsonDocument.Parse(activeEvent.CustomConfigJson))
                {
                    var root = doc.RootElement;
                    if (root.TryGetProperty("Milestones", out var milestonesNode))
                    {
                        milestones = JsonSerializer.Deserialize<List<MilestoneConfig>>(milestonesNode.GetRawText()) ?? new();
                    }
                    else
                    {
                        return "<div class='no-records-card'>Milestone rewards blueprints missing.</div>";
                    }
                }
            }
            catch
            {
                return "<div class='no-records-card'>Configuration read error. Core sync mismatch.</div>";
            }

            var milestonesHtml = "";
            for (int i = 0; i < milestones.Count; i++)
            {
                var m = milestones[i];
                var isClaimed = (progress.TierClaimed & (1 << i)) != 0;
                var isCompleted = progress.Points >= m.TargetPoints;

                var cardClass = "milestone-card";
                var btnHtml = "";

                if (isClaimed)
                {
                    cardClass += " claimed";
                    btnHtml = "<button class='milestone-btn claimed' disabled>SECURED</button>";
                }
                else if (isCompleted)
                {
                    cardClass += " completed";
                    btnHtml = $"<button class='milestone-btn ready' onclick='claimMilestone(\"{activeEvent.Id}\", {i}, this)'>SECURE ASSET</button>";
                }
                else
                {
                    cardClass += " active";
                    btnHtml = "<button class='milestone-btn locked' disabled>IN PROGRESS</button>";
                }

                milestonesHtml += $"""
                <div class="{cardClass}">
                    <div style="display:flex; justify-content:space-between; align-items:center; margin-bottom:8px;">
                        <span class="milestone-badge">{m.TargetPoints:N0} PTS REQUIRED</span>
                        <span class="milestone-reward-name">{m.RewardName}</span>
                    </div>
                    <div style="display:flex; justify-content:space-between; align-items:center;">
                        <span class="milestone-status">{ (isClaimed ? "STAMPED ✓" : (isCompleted ? "READY" : "LOCKED")) }</span>
                        {btnHtml}
                    </div>
                </div>
                """;
            }

            return milestonesHtml;
        }

        private string RenderTemplate(string templateName, Dictionary<string, string> replacements)
        {
            var path = Path.Combine(Directory.GetCurrentDirectory(), "Views", templateName);
            var content = System.IO.File.ReadAllText(path);
            foreach (var kvp in replacements)
            {
                content = content.Replace("{{" + kvp.Key + "}}", kvp.Value);
            }
            return content;
        }

        [HttpGet("event/raid/helpers")]
        public IActionResult GetRaidHelpers()
        {
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;
            
            var helpers = _eventEngine.GetAvailableHelpers(profileId, 6);
            return Ok(helpers);
        }

        [HttpPost("event/raid/select_helper")]
        public IActionResult SelectRaidHelper([FromBody] SelectHelperRequest request)
        {
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            _eventEngine.SelectRaidHelper(profileId, request.EventId, request.HelperProfileId);
            return Ok(new { success = true });
        }

        [HttpPost("event/raid/engage")]
        public IActionResult EngageRaid([FromForm] EngageRaidRequest request)
        {
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            var result = _eventEngine.ResolveRaidBattle(profileId, request.EventId, request.Difficulty);
            
            if (!result.Success)
            {
                return Content($"<div class='error-container' style='background:#0f172a;color:#ef4444;font-family:monospace;padding:50px;text-align:center;'><h2>COMBAT DESYNC: {result.Message}</h2><br><a href='/ultimate/event' style='color:#00f0ff;text-decoration:none;'>&lt; RETURN TO PORTAL</a></div>", "text/html");
            }

            var replacements = new Dictionary<string, string>
            {
                { "bossName", result.BossName },
                { "bossLevel", result.BossLevel.ToString() },
                { "bodyPartName", result.BodyPartName },
                { "difficulty", result.Difficulty.ToUpper() },
                { "playerDamage", result.PlayerDamage.ToString("N0") },
                { "bossDefense", result.BossDefense.ToString("N0") },
                { "netDamage", result.NetDamage.ToString("N0") },
                { "mainHpBefore", result.MainHpBefore.ToString("N0") },
                { "mainHpAfter", result.MainHpAfter.ToString("N0") },
                { "partHpBefore", result.PartHpBefore.ToString("N0") },
                { "partHpAfter", result.PartHpAfter.ToString("N0") },
                { "pointsEarned", result.PointsEarned.ToString("N0") },
                { "silverEarned", result.SilverEarned.ToString("N0") }
            };

            string headerTitle = "⚠️ DEPLOYMENT RE-ENGAGEMENT REQUIRED";
            string headerClass = "retreat";
            string headerSubtitle = "Raid target sustained defensive armor. Re-engage strike squads.";

            if (result.VictoryType == "OneShot")
            {
                headerTitle = "🏆 INCURSION CLEARED - CORE OVERLOADED";
                headerClass = "oneshot";
                headerSubtitle = "Legendary Victory! Entire threat health pool obliterated in a single strike!";
            }
            else if (result.VictoryType == "PartDefeated")
            {
                headerTitle = "🏆 TARGET SECURED - SECTOR CLEARED";
                headerClass = "victory";
                headerSubtitle = "Success! Core body part destroyed, forcing priority threat to retreat.";
            }

            replacements.Add("headerTitle", headerTitle);
            replacements.Add("headerClass", headerClass);
            replacements.Add("headerSubtitle", headerSubtitle);

            string logsHtml = "";
            foreach (var log in result.CombatLogs)
            {
                string style = "";
                if (log.StartsWith("[LEGENDARY OUTCOME]") || log.StartsWith("[TACTICAL CLEARANCE]")) style = "color:#10b981;";
                else if (log.StartsWith("[SQUAD INITIALIZATION]") || log.StartsWith("[COOPERATIVE LINK]")) style = "color:#64748b;";
                
                logsHtml += $"<div class='log-row' style='{style}'>{log}</div>";
            }
            replacements.Add("combatLogsHtml", logsHtml);

            return Content(RenderTemplate("raid_battle_result.html", replacements), "text/html");
        }

        [HttpGet("event/war")]
        public IActionResult ServeWarPortal()
        {
            _logger.LogInformation("[EventsController] ServeWarPortal called.");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            var profile = _dbContext.Profiles
                .Include(p => p.EventProgresses)
                .Include(p => p.Alliance)
                .FirstOrDefault(p => p.Id == profileId);

            if (profile == null)
            {
                return RedirectToAction("ServeGameTopPage", "Cygames");
            }

            if (profile.AllianceId == null || profile.Alliance == null)
            {
                return Content("<div class='error-container' style='background:#0f172a;color:#ef4444;font-family:monospace;padding:50px;text-align:center;'><h2>COMMISSION BLOCKED: NO ALLIANCE</h2><br><p>You must be a member of a registered S.H.I.E.L.D. Division to participate in War Events.</p><br><a href='/ultimate/menu' style='color:#00f0ff;text-decoration:none;'>&lt; RETURN TO PORTAL</a></div>", "text/html");
            }

            var activeEvent = _eventEngine.GetActiveEvent();
            if (activeEvent == null)
            {
                // Standby mode
                var standbyReplacements = new Dictionary<string, string>
                {
                    { "level", profile.Level.ToString() },
                    { "agentName", profile.Nickname },
                    { "attackPowerCur", profile.AttackPowerCurrent.ToString() },
                    { "attackPowerMax", profile.AttackPower.ToString() },
                    { "attackPowerPct", ((double)profile.AttackPowerCurrent / profile.AttackPower * 100).ToString("N0") },
                    { "isStandby", "true" },
                    { "isUpcoming", "false" },
                    { "isActive", "false" },
                    { "isCalculating", "false" },
                    { "isCompleted", "false" },
                    { "eventId", "" },
                    { "eventTitle", "S.H.I.E.L.D. STANDBY MODE" },
                    { "eventDesc", "The war mainframe is currently offline. Stand by for priority global alerts." },
                    { "timerSeconds", "3600" },
                    { "timerLabel", "SCAN RE-CALIBRATION IN" },
                    { "points", "0" },
                    { "rank", "N/A" },
                    { "leaderboardHtml", "<div class='no-records-card'>Rankings locked during standby.</div>" },
                    { "milestonesHtml", "<div class='no-records-card'>Milestones supply drops offline.</div>" },
                    { "combatLogsHtml", "" }
                };
                return Content(RenderTemplate("war_portal.html", standbyReplacements), "text/html");
            }

            var progress = _eventEngine.GetPlayerProgress(profile.Id, activeEvent.Id);
            var state = _eventEngine.GetEventState(activeEvent);
            var rank = _eventEngine.GetPlayerRank(profile.Id, activeEvent.Id);

            var timerLabel = state switch
            {
                "Upcoming" => "DEPLOYMENT INITIATED IN",
                "Active" => "WAR DIRECTIVE ACTIVE",
                "Calculating" => "RANK VERIFICATION PERIOD",
                _ => "CAMPAIGN CONCLUDED"
            };

            var replacements = new Dictionary<string, string>
            {
                { "level", profile.Level.ToString() },
                { "agentName", profile.Nickname },
                { "attackPowerCur", profile.AttackPowerCurrent.ToString() },
                { "attackPowerMax", profile.AttackPower.ToString() },
                { "attackPowerPct", ((double)profile.AttackPowerCurrent / profile.AttackPower * 100).ToString("N0") },
                { "isStandby", "false" },
                { "isUpcoming", (state == "Upcoming").ToString().ToLower() },
                { "isActive", (state == "Active").ToString().ToLower() },
                { "isCalculating", (state == "Calculating").ToString().ToLower() },
                { "isCompleted", (state == "Completed").ToString().ToLower() },
                { "eventId", activeEvent.Id },
                { "eventTitle", activeEvent.Title },
                { "eventDesc", activeEvent.Description },
                { "timerSeconds", GetTimerSeconds(activeEvent, state).ToString() },
                { "timerLabel", timerLabel },
                { "points", progress.Points.ToString("N0") },
                { "rank", rank.ToString() },
                { "leaderboardHtml", RenderLeaderboardHtml(activeEvent.Id, profile.Id) },
                { "milestonesHtml", RenderMilestonesHtml(activeEvent, progress) },
                { "combatLogsHtml", "" }
            };

            return Content(RenderTemplate("war_portal.html", replacements), "text/html");
        }

        [HttpGet("event/war/status")]
        public IActionResult GetWarMatchupStatus([FromQuery] string eventId)
        {
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            var profile = _dbContext.Profiles
                .Include(p => p.Alliance)
                .FirstOrDefault(p => p.Id == profileId);

            if (profile == null || profile.AllianceId == null || profile.Alliance == null)
            {
                return BadRequest("Alliance reference missing.");
            }

            var alliance = profile.Alliance;
            
            // Check if queue has timed out for AI matching
            _warEventEngine.CheckOrMatchmakeAlliance(alliance.Id, eventId);

            var activeBattle = _warEventEngine.GetActiveWarBattle(alliance.Id, eventId);
            var isQueued = alliance.IsQueuedForWar;
            var canQueue = string.Equals(profile.AllianceRole, "Leader", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(profile.AllianceRole, "Vice-Leader", StringComparison.OrdinalIgnoreCase);

            if (activeBattle == null)
            {
                return Ok(new
                {
                    isQueued = isQueued,
                    canQueue = canQueue,
                    battle = (object?)null
                });
            }

            // Fetch names and details
            var allianceA = _dbContext.Alliances.Find(activeBattle.AllianceAId);
            var allianceB = _dbContext.Alliances.Find(activeBattle.AllianceBId);

            var leadersA = JsonSerializer.Deserialize<List<WarDefensiveLeaderState>>(activeBattle.AllianceADefensiveLeadersJson) ?? new();
            var leadersB = JsonSerializer.Deserialize<List<WarDefensiveLeaderState>>(activeBattle.AllianceBDefensiveLeadersJson) ?? new();

            return Ok(new
            {
                isQueued = isQueued,
                canQueue = canQueue,
                isAllianceA = activeBattle.AllianceAId == alliance.Id,
                battle = new
                {
                    id = activeBattle.Id,
                    allianceAName = allianceA?.Name ?? "Division Alpha",
                    allianceBName = allianceB?.Name ?? "Division Beta",
                    allianceAHealthCurrent = activeBattle.AllianceAHealthCurrent,
                    allianceAHealthMax = activeBattle.AllianceAHealthMax,
                    allianceBHealthCurrent = activeBattle.AllianceBHealthCurrent,
                    allianceBHealthMax = activeBattle.AllianceBHealthMax,
                    allianceAValorCurrent = activeBattle.AllianceAValorCurrent,
                    allianceBValorCurrent = activeBattle.AllianceBValorCurrent,
                    allianceALeaders = leadersA,
                    allianceBLeaders = leadersB,
                    status = activeBattle.Status,
                    startTime = activeBattle.StartTime,
                    endTime = activeBattle.EndTime,
                    isAiOpponent = activeBattle.IsAiOpponent
                }
            });
        }

        [HttpPost("event/war/queue")]
        public IActionResult JoinWarMatchmakingQueue([FromQuery] string eventId)
        {
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            var profile = _dbContext.Profiles.Find(profileId);
            if (profile == null || profile.AllianceId == null)
            {
                return Ok(new { success = false, message = "Alliance reference not found." });
            }

            if (!string.Equals(profile.AllianceRole, "Leader", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(profile.AllianceRole, "Vice-Leader", StringComparison.OrdinalIgnoreCase))
            {
                return Ok(new { success = false, message = "Command Denied. Leader role required to commission queue." });
            }

            _warEventEngine.EnterMatchmakingQueue(profile.AllianceId.Value);
            
            // Immediate check to see if an opponent is available
            _warEventEngine.CheckOrMatchmakeAlliance(profile.AllianceId.Value, eventId);

            return Ok(new { success = true });
        }

        [HttpPost("event/war/engage")]
        public IActionResult EngageWarOpponent([FromBody] WarEngageRequest request)
        {
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            var result = _warEventEngine.ResolveWarEngagement(profileId, request.EventId, request.TargetProfileId, request.IsCoreAttack);
            return Ok(result);
        }

        public class WarEngageRequest
        {
            public string EventId { get; set; } = string.Empty;
            public int TargetProfileId { get; set; }
            public bool IsCoreAttack { get; set; }
        }

        public class ClaimRequest
        {
            public string EventId { get; set; } = string.Empty;
            public int TierIndex { get; set; }
        }

        public class SelectHelperRequest
        {
            public string EventId { get; set; } = string.Empty;
            public int HelperProfileId { get; set; }
        }

        public class EngageRaidRequest
        {
            public string EventId { get; set; } = string.Empty;
            public string Difficulty { get; set; } = "Easy";
        }

        private class MilestoneConfig
        {
            public int TargetPoints { get; set; }
            public string RewardType { get; set; } = string.Empty;
            public int RewardValue { get; set; }
            public int RewardQuantity { get; set; } = 1;
            public string RewardName { get; set; } = string.Empty;
        }
    }
}
