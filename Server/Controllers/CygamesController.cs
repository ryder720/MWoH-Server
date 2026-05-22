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
using System.Threading.Tasks;

namespace MwohServer.Controllers
{
    [ApiController]
    [Route("ultimate")]
    [ServiceFilter(typeof(GAuthValidationFilter))] // Validates incoming GAuth signatures (bypassed in Dev mode)
    public class CygamesController : ControllerBase
    {
        private readonly ILogger<CygamesController> _logger;
        private readonly IAuthService _authService;
        private readonly MwohDbContext _dbContext;

        public CygamesController(ILogger<CygamesController> logger, IAuthService authService, MwohDbContext dbContext)
        {
            _logger = logger;
            _authService = authService;
            _dbContext = dbContext;
        }

        // 1. Temporary Credential Request (Cygames OAuth step 1)
        [HttpPost("restful_api_auth/request_temporary_credential")]
        public IActionResult RequestTemporaryCredential()
        {
            _logger.LogInformation("[Cygames] RequestTemporaryCredential called.");
            
            // Return JSON containing temporary oauth token as expected by Smali
            var response = new
            {
                oauth_token = "temp_oauth_token_98765",
                oauth_token_secret = "temp_oauth_secret_abcde",
                oauth_callback_confirmed = true
            };
            return Ok(response);
        }

        // 2. Token Credential Request (Cygames OAuth step 2)
        [HttpPost("restful_api_auth/request_token_credential")]
        public async Task<IActionResult> RequestTokenCredential()
        {
            _logger.LogInformation("[Cygames] RequestTokenCredential called.");
            
            string oauthToken = "";
            string verifier = "";
            
            if (Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync();
                oauthToken = form["oauth_token"].ToString();
                verifier = form["verifier"].ToString();
            }
            
            _logger.LogInformation($"[Cygames] RequestTokenCredential Token: {oauthToken}, Verifier: {verifier}");

            // Find user by token
            var user = _authService.GetUserByToken(oauthToken);
            if (user == null)
            {
                // Fallback to testuser if active session lookup fails in development
                _logger.LogWarning("[Cygames] Active token lookup failed. Falling back to testuser.");
                user = _authService.ValidateUser("testuser", "password");
            }

            string sessionId = "sess_dev_default_session_id";
            if (user != null)
            {
                sessionId = _authService.GenerateSession(user);
            }

            // Set the critical "sid" Cookie in response for game WebView mapping
            Response.Cookies.Append("sid", sessionId, new Microsoft.AspNetCore.Http.CookieOptions
            {
                Path = "/",
                HttpOnly = false, // Must be readable by client web view CookieSync
                Secure = false
            });

            // Return success JSON payload parsed by LoginSessionHandler$3
            var response = new
            {
                success = true,
                menu_version = 1
            };
            return Ok(response);
        }

        // 3. Client Version Check
        [HttpGet("top/client_check")]
        public IActionResult ClientCheck()
        {
            _logger.LogInformation("[Cygames] ClientCheck called.");
            
            var response = new
            {
                status = 0, // Indicates client version is valid/latest
                message = "Node synchronized successfully.",
                latest_version = "2.4"
            };
            
            return Ok(response);
        }

        // 4. Game WebView Top Page (loaded by app topPagePath)
        [HttpGet("")]
        public IActionResult ServeGameTopPage()
        {
            _logger.LogInformation("[Cygames] ServeGameTopPage called.");

            // Read session id from request cookies
            var sessionId = Request.Cookies["sid"];
            UserAccount? user = null;
            if (!string.IsNullOrEmpty(sessionId))
            {
                user = _authService.GetUserBySessionId(sessionId);
            }

            if (user == null)
            {
                // Fallback to pre-seeded user if session is not active for web viewing
                user = _authService.ValidateUser("testuser", "password");
            }

            // Retrieve profile with cards collection and templates mapped in EF
            var profileId = user?.Profile?.Id ?? 1;
            var profile = _dbContext.Profiles
                .Include(p => p.Cards)
                .ThenInclude(c => c.CardTemplate)
                .FirstOrDefault(p => p.Id == profileId);

            var agentName = profile?.Nickname ?? "Unknown Agent";
            var level = profile?.Level ?? 1;
            var coins = profile?.MobaCoinBalance ?? 0;
            var silver = profile?.SilverBalance ?? 0;

            var cardsHtmlList = "";
            if (profile != null && profile.Cards.Any())
            {
                foreach (var playerCard in profile.Cards)
                {
                    var template = playerCard.CardTemplate;
                    if (template == null) continue;

                    var alignmentColor = template.Alignment.ToLower() switch
                    {
                        "speed" => "var(--hud-blue)",
                        "bruiser" => "var(--accent-red)",
                        "tactics" => "#10b981",
                        _ => "#a5b4fc"
                    };

                    cardsHtmlList += $@"
                <div class='card-badge' style='border-left: 4px solid {alignmentColor};'>
                    <div class='card-info'>
                        <div class='card-visual-title'>{template.VisualTitle}</div>
                        <div class='card-meta'>
                            <span class='badge' style='background: rgba(255,255,255,0.05); color: #94a3b8;'>{template.Rarity}</span>
                            <span class='badge' style='background: rgba(0,240,255,0.05); color: {alignmentColor};'>{template.Alignment}</span>
                            <span class='badge' style='background: rgba(245,158,11,0.05); color: var(--accent-gold);'>PWR {template.PowerRequirement}</span>
                        </div>
                        <div class='card-stats-row'>
                            <div class='card-stat'><span class='lbl'>ATK</span> <span class='val'>{playerCard.CurrentAtk:N0}</span></div>
                            <div class='card-stat'><span class='lbl'>DEF</span> <span class='val'>{playerCard.CurrentDef:N0}</span></div>
                            <div class='card-stat'><span class='lbl'>LVL</span> <span class='val'>{playerCard.CurrentLevel}</span></div>
                        </div>
                        {(string.IsNullOrEmpty(template.Quote) ? "" : $"<div class='card-quote'>&ldquo;{template.Quote}&rdquo;</div>")}
                    </div>
                </div>";
                }
            }
            else
            {
                cardsHtmlList = "<div class='no-cards'>No active card tactical deployments found in database profile.</div>";
            }

            var html = $@"
<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>S.H.I.E.L.D. Command Center</title>
    <link href='https://fonts.googleapis.com/css2?family=Outfit:wght@300;400;600;800&family=Space+Mono:wght@400;700&display=swap' rel='stylesheet'>
    <style>
        :root {{
            --bg-color: #04080f;
            --hud-blue: #00f0ff;
            --hud-glow: rgba(0, 240, 255, 0.2);
            --panel-bg: rgba(6, 12, 24, 0.8);
            --border-color: rgba(0, 240, 255, 0.25);
            --accent-gold: #f59e0b;
            --accent-red: #ef4444;
            --text-color: #e2e8f0;
        }}

        * {{
            box-sizing: border-box;
            margin: 0;
            padding: 0;
        }}

        body {{
            background-color: var(--bg-color);
            background-image: 
                radial-gradient(at 50% 0%, rgba(0, 240, 255, 0.15) 0px, transparent 60%),
                linear-gradient(rgba(0, 240, 255, 0.02) 1px, transparent 1px),
                linear-gradient(90deg, rgba(0, 240, 255, 0.02) 1px, transparent 1px);
            background-size: 100% 100%, 20px 20px, 20px 20px;
            color: var(--text-color);
            font-family: 'Outfit', sans-serif;
            min-height: 100vh;
            display: flex;
            flex-direction: column;
            align-items: center;
            overflow-x: hidden;
            padding: 20px;
        }}

        .hud-header {{
            width: 100%;
            max-width: 600px;
            text-align: center;
            border-bottom: 2px solid var(--hud-blue);
            padding-bottom: 15px;
            margin-bottom: 25px;
            position: relative;
            box-shadow: 0 4px 15px var(--hud-glow);
        }}

        .hud-header::before, .hud-header::after {{
            content: '';
            position: absolute;
            bottom: -5px;
            width: 10px;
            height: 10px;
            background: var(--hud-blue);
        }}
        .hud-header::before {{ left: 0; }}
        .hud-header::after {{ right: 0; }}

        .hud-header h1 {{
            font-family: 'Space Mono', monospace;
            font-size: 24px;
            font-weight: 700;
            text-transform: uppercase;
            letter-spacing: 3px;
            color: var(--hud-blue);
            text-shadow: 0 0 10px var(--hud-glow);
        }}

        .hud-header p {{
            font-size: 11px;
            color: #94a3b8;
            letter-spacing: 2px;
            text-transform: uppercase;
            margin-top: 5px;
        }}

        .grid {{
            width: 100%;
            max-width: 600px;
            display: grid;
            grid-template-columns: 1fr;
            gap: 20px;
        }}

        .panel {{
            background: var(--panel-bg);
            border: 1px solid var(--border-color);
            border-radius: 12px;
            padding: 20px;
            position: relative;
            backdrop-filter: blur(10px);
            box-shadow: 0 10px 30px rgba(0, 0, 0, 0.5);
            transition: border-color 0.3s ease;
        }}

        .panel:hover {{
            border-color: var(--hud-blue);
        }}

        .panel-title {{
            font-family: 'Space Mono', monospace;
            font-size: 14px;
            font-weight: 700;
            text-transform: uppercase;
            letter-spacing: 1px;
            color: var(--hud-blue);
            margin-bottom: 15px;
            display: flex;
            align-items: center;
            border-bottom: 1px solid rgba(0, 240, 255, 0.1);
            padding-bottom: 8px;
        }}

        .panel-title::before {{
            content: '';
            display: inline-block;
            width: 6px;
            height: 6px;
            background: var(--hud-blue);
            margin-right: 8px;
            box-shadow: 0 0 5px var(--hud-blue);
        }}

        .agent-stats {{
            display: grid;
            grid-template-columns: 1fr 1fr;
            gap: 15px;
        }}

        .stat-item {{
            background: rgba(0, 0, 0, 0.3);
            border: 1px solid rgba(255, 255, 255, 0.05);
            border-radius: 8px;
            padding: 12px;
            display: flex;
            flex-direction: column;
        }}

        .stat-label {{
            font-size: 11px;
            text-transform: uppercase;
            color: #64748b;
            letter-spacing: 1px;
            margin-bottom: 4px;
        }}

        .stat-value {{
            font-size: 18px;
            font-weight: 600;
            color: #ffffff;
        }}

        .stat-value.highlight {{
            color: var(--hud-blue);
            text-shadow: 0 0 5px var(--hud-glow);
        }}

        .stat-value.gold {{
            color: var(--accent-gold);
        }}

        .status-box {{
            display: flex;
            align-items: center;
            background: rgba(16, 185, 129, 0.05);
            border: 1px solid rgba(16, 185, 129, 0.2);
            color: #34d399;
            border-radius: 8px;
            padding: 12px;
            font-size: 13px;
            font-weight: 600;
            text-transform: uppercase;
            letter-spacing: 1px;
            margin-bottom: 15px;
        }}

        .status-dot {{
            width: 8px;
            height: 8px;
            background: #10b981;
            border-radius: 50%;
            margin-right: 12px;
            box-shadow: 0 0 8px #10b981;
            animation: pulse 1.5s infinite alternate;
        }}

        .console {{
            background: #020408;
            border: 1px solid rgba(255, 255, 255, 0.05);
            border-radius: 8px;
            padding: 15px;
            font-family: 'Space Mono', monospace;
            font-size: 12px;
            color: #34d399;
            height: 120px;
            overflow-y: auto;
            line-height: 1.6;
        }}

        .console-line {{
            margin-bottom: 6px;
        }}

        .console-time {{
            color: #64748b;
            margin-right: 8px;
        }}

        .card-badge {{
            background: rgba(0, 0, 0, 0.4);
            border: 1px solid rgba(255, 255, 255, 0.05);
            border-radius: 8px;
            padding: 15px;
            display: flex;
            gap: 15px;
            margin-bottom: 12px;
            transition: all 0.3s ease;
        }}

        .card-badge:hover {{
            background: rgba(0, 0, 0, 0.6);
            transform: translateX(4px);
            border-color: rgba(255, 255, 255, 0.1);
        }}

        .card-info {{
            display: flex;
            flex-direction: column;
            width: 100%;
        }}

        .card-visual-title {{
            font-size: 15px;
            font-weight: 600;
            color: #ffffff;
            margin-bottom: 4px;
        }}

        .card-meta {{
            display: flex;
            gap: 8px;
            margin-bottom: 10px;
            flex-wrap: wrap;
        }}

        .badge {{
            font-size: 10px;
            font-weight: 700;
            text-transform: uppercase;
            padding: 3px 8px;
            border-radius: 4px;
            letter-spacing: 0.5px;
        }}

        .card-stats-row {{
            display: flex;
            gap: 15px;
            background: rgba(255, 255, 255, 0.02);
            padding: 6px 12px;
            border-radius: 6px;
            border: 1px solid rgba(255, 255, 255, 0.02);
            margin-bottom: 8px;
            width: fit-content;
        }}

        .card-stat {{
            font-size: 12px;
            display: flex;
            align-items: center;
            gap: 5px;
        }}

        .card-stat .lbl {{
            color: #64748b;
            font-weight: 600;
        }}

        .card-stat .val {{
            color: #ffffff;
            font-weight: 700;
        }}

        .card-quote {{
            font-size: 11px;
            color: #94a3b8;
            font-style: italic;
            line-height: 1.4;
            border-left: 2px solid rgba(255, 255, 255, 0.1);
            padding-left: 8px;
            margin-top: 4px;
        }}

        .no-cards {{
            font-family: 'Space Mono', monospace;
            font-size: 12px;
            color: #64748b;
            text-align: center;
            padding: 20px;
        }}

        @keyframes pulse {{
            from {{ opacity: 0.4; }}
            to {{ opacity: 1; }}
        }}
    </style>
</head>
<body>
    <div class='hud-header'>
        <h1>S.H.I.E.L.D. Mainframe</h1>
        <p>Tactical Operation Center | Active Session</p>
    </div>

    <div class='grid'>
        <div class='panel'>
            <div class='panel-title'>Agent Credentials</div>
            <div class='status-box'>
                <div class='status-dot'></div>
                Node Synced Successfully - Level 1 Clearance
            </div>
            
            <div class='agent-stats'>
                <div class='stat-item'>
                    <div class='stat-label'>Agent ID</div>
                    <div class='stat-value highlight'>{agentName}</div>
                </div>
                <div class='stat-item'>
                    <div class='stat-label'>Clearance Level</div>
                    <div class='stat-value'>Lvl {level}</div>
                </div>
                <div class='stat-item'>
                    <div class='stat-label'>MobaCoins</div>
                    <div class='stat-value gold'>{coins:N0}</div>
                </div>
                <div class='stat-item'>
                    <div class='stat-label'>Silver Balance</div>
                    <div class='stat-value'>{silver:N0}</div>
                </div>
            </div>
        </div>

        <div class='panel'>
            <div class='panel-title'>Command Diagnostics</div>
            <div class='console' id='diagnosticConsole'>
                <div class='console-line'><span class='console-time'>[{DateTime.UtcNow:HH:mm:ss}]</span>System initialized.</div>
                <div class='console-line'><span class='console-time'>[{DateTime.UtcNow:HH:mm:ss}]</span>Session linked to DeNA Social Core.</div>
                <div class='console-line'><span class='console-time'>[{DateTime.UtcNow:HH:mm:ss}]</span>Verification signature validated.</div>
                <div class='console-line'><span class='console-time'>[{DateTime.UtcNow:HH:mm:ss}]</span>Secure loopback proxy connected at 10.0.2.2.</div>
            </div>
        </div>

        <div class='panel'>
            <div class='panel-title'>Tactical Card Deployments</div>
            <div class='cards-container'>
                {cardsHtmlList}
            </div>
        </div>
    </div>
</body>
</html>
";

            return Content(html, "text/html");
        }
        // 5. Stub for Local Push Notification settings to avoid client JSON array conversion crashes
        [HttpGet("nexus/register_local_push")]
        public IActionResult RegisterLocalPush()
        {
            _logger.LogInformation("[Cygames] RegisterLocalPush called. Returning empty array.");
            return Content("[]", "application/json");
        }

        // 6. Catch-all fallback for other game APIs (Gacha, Quests, Items, etc.)
        [HttpGet("{*url}")]
        [HttpPost("{*url}")]
        public IActionResult GameFallback(string url)
        {
            _logger.LogInformation($"[Cygames] Game Fallback invoked: {Request.Method} /ultimate/{url}");
            
            if (!string.IsNullOrEmpty(url) && url.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                return Content("", "text/csv");
            }
            
            // Return empty successful JSON stub
            return Ok(new { success = true });
        }
    }
}
