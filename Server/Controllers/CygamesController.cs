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

            // Retrieve profile with cards collection, inventory items, and templates mapped in EF
            var profileId = user?.Profile?.Id ?? 1;
            var profile = _dbContext.Profiles
                .Include(p => p.Cards)
                .ThenInclude(c => c.CardTemplate)
                .Include(p => p.InventoryItems)
                .ThenInclude(pi => pi.ItemTemplate)
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

            var itemsHtmlList = "";
            if (profile != null && profile.InventoryItems.Any())
            {
                foreach (var pi in profile.InventoryItems)
                {
                    var temp = pi.ItemTemplate;
                    if (temp == null) continue;

                    var color = temp.Type switch
                    {
                        "EnergyRestorative" => "var(--hud-blue)",
                        "AttackPowerRestorative" => "var(--accent-red)",
                        "DefensePowerRestorative" => "#10b981",
                        "MasteryIso8" => "#a5b4fc",
                        _ => "var(--accent-gold)"
                    };

                    var useButton = temp.Type.EndsWith("Restorative")
                        ? $"<button class='use-btn' onclick='mainframeUseItem({temp.Id}, this)'>USE</button>"
                        : "<span class='badge' style='background: rgba(255,255,255,0.05); color: #64748b;'>STOCK</span>";

                    itemsHtmlList += $@"
                <div class='item-badge' id='mainframe-item-row-{temp.Id}' style='border-left: 4px solid {color}; margin-bottom: 10px; background: rgba(0, 0, 0, 0.4); border-top: 1px solid rgba(255,255,255,0.03); border-right: 1px solid rgba(255,255,255,0.03); border-bottom: 1px solid rgba(255,255,255,0.03); border-radius: 8px; padding: 12px; display: flex; align-items: center; justify-content: space-between;'>
                    <div style='display: flex; flex-direction: column;'>
                        <div style='font-size: 13px; font-weight: 600; color: #ffffff;'>{temp.Name} <span style='color: var(--accent-gold); font-family: ""Space Mono"", monospace; margin-left: 6px;' id='mainframe-qty-{temp.Id}'>x{pi.Quantity}</span></div>
                        <div style='font-size: 10px; color: #64748b; margin-top: 3px;'>{temp.Description}</div>
                    </div>
                    <div>
                        {useButton}
                    </div>
                </div>";
                }
            }
            else
            {
                itemsHtmlList = "<div class='no-cards'>No active item depot inventory found in database profile.</div>";
            }

            var html = $$"""
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>S.H.I.E.L.D. Command Center</title>
    <link href="https://fonts.googleapis.com/css2?family=Outfit:wght@300;400;600;800&family=Space+Mono:wght@400;700&display=swap" rel="stylesheet">
    <style>
        :root {
            --bg-color: #04080f;
            --hud-blue: #00f0ff;
            --hud-glow: rgba(0, 240, 255, 0.2);
            --panel-bg: rgba(6, 12, 24, 0.8);
            --border-color: rgba(0, 240, 255, 0.25);
            --accent-gold: #f59e0b;
            --accent-red: #ef4444;
            --text-color: #e2e8f0;
        }

        * {
            box-sizing: border-box;
            margin: 0;
            padding: 0;
        }

        body {
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
        }

        .hud-header {
            width: 100%;
            max-width: 600px;
            text-align: center;
            border-bottom: 2px solid var(--hud-blue);
            padding-bottom: 15px;
            margin-bottom: 25px;
            position: relative;
            box-shadow: 0 4px 15px var(--hud-glow);
        }

        .hud-header::before, .hud-header::after {
            content: '';
            position: absolute;
            bottom: -5px;
            width: 10px;
            height: 10px;
            background: var(--hud-blue);
        }
        .hud-header::before { left: 0; }
        .hud-header::after { right: 0; }

        .hud-header h1 {
            font-family: 'Space Mono', monospace;
            font-size: 24px;
            font-weight: 700;
            text-transform: uppercase;
            letter-spacing: 3px;
            color: var(--hud-blue);
            text-shadow: 0 0 10px var(--hud-glow);
        }

        .hud-header p {
            font-size: 11px;
            color: #94a3b8;
            letter-spacing: 2px;
            text-transform: uppercase;
            margin-top: 5px;
        }

        .grid {
            width: 100%;
            max-width: 600px;
            display: grid;
            grid-template-columns: 1fr;
            gap: 20px;
        }

        .panel {
            background: var(--panel-bg);
            border: 1px solid var(--border-color);
            border-radius: 12px;
            padding: 20px;
            position: relative;
            backdrop-filter: blur(10px);
            box-shadow: 0 10px 30px rgba(0, 0, 0, 0.5);
            transition: border-color 0.3s ease;
        }

        .panel:hover {
            border-color: var(--hud-blue);
        }

        .panel-title {
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
        }

        .panel-title::before {
            content: '';
            display: inline-block;
            width: 6px;
            height: 6px;
            background: var(--hud-blue);
            margin-right: 8px;
            box-shadow: 0 0 5px var(--hud-blue);
        }

        .agent-stats {
            display: grid;
            grid-template-columns: 1fr 1fr;
            gap: 15px;
        }

        .stat-item {
            background: rgba(0, 0, 0, 0.3);
            border: 1px solid rgba(255, 255, 255, 0.05);
            border-radius: 8px;
            padding: 12px;
            display: flex;
            flex-direction: column;
        }

        .stat-label {
            font-size: 11px;
            text-transform: uppercase;
            color: #64748b;
            letter-spacing: 1px;
            margin-bottom: 4px;
        }

        .stat-value {
            font-size: 18px;
            font-weight: 600;
            color: #ffffff;
        }

        .stat-value.highlight {
            color: var(--hud-blue);
            text-shadow: 0 0 5px var(--hud-glow);
        }

        .stat-value.gold {
            color: var(--accent-gold);
        }

        .status-box {
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
        }

        .status-dot {
            width: 8px;
            height: 8px;
            background: #10b981;
            border-radius: 50%;
            margin-right: 12px;
            box-shadow: 0 0 8px #10b981;
            animation: pulse 1.5s infinite alternate;
        }

        .console {
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
        }

        .console-line {
            margin-bottom: 6px;
        }

        .console-time {
            color: #64748b;
            margin-right: 8px;
        }

        .card-badge {
            background: rgba(0, 0, 0, 0.4);
            border: 1px solid rgba(255, 255, 255, 0.05);
            border-radius: 8px;
            padding: 15px;
            display: flex;
            gap: 15px;
            margin-bottom: 12px;
            transition: all 0.3s ease;
        }

        .card-badge:hover {
            background: rgba(0, 0, 0, 0.6);
            transform: translateX(4px);
            border-color: rgba(255, 255, 255, 0.1);
        }

        .card-info {
            display: flex;
            flex-direction: column;
            width: 100%;
        }

        .card-visual-title {
            font-size: 15px;
            font-weight: 600;
            color: #ffffff;
            margin-bottom: 4px;
        }

        .card-meta {
            display: flex;
            gap: 8px;
            margin-bottom: 10px;
            flex-wrap: wrap;
        }

        .badge {
            font-size: 10px;
            font-weight: 700;
            text-transform: uppercase;
            padding: 3px 8px;
            border-radius: 4px;
            letter-spacing: 0.5px;
        }

        .card-stats-row {
            display: flex;
            gap: 15px;
            background: rgba(255, 255, 255, 0.02);
            padding: 6px 12px;
            border-radius: 6px;
            border: 1px solid rgba(255, 255, 255, 0.02);
            margin-bottom: 8px;
            width: fit-content;
        }

        .card-stat {
            font-size: 12px;
            display: flex;
            align-items: center;
            gap: 5px;
        }

        .card-stat .lbl {
            color: #64748b;
            font-weight: 600;
        }

        .card-stat .val {
            color: #ffffff;
            font-weight: 700;
        }

        .card-quote {
            font-size: 11px;
            color: #94a3b8;
            font-style: italic;
            line-height: 1.4;
            border-left: 2px solid rgba(255, 255, 255, 0.1);
            padding-left: 8px;
            margin-top: 4px;
        }

        .no-cards {
            font-family: 'Space Mono', monospace;
            font-size: 12px;
            color: #64748b;
            text-align: center;
            padding: 20px;
        }

        .use-btn {
            background: linear-gradient(185deg, var(--accent-gold), #b45309);
            border: none;
            color: #ffffff;
            font-weight: 700;
            font-size: 11px;
            padding: 6px 14px;
            border-radius: 6px;
            cursor: pointer;
            box-shadow: 0 2px 6px rgba(245,158,11,0.2);
            transition: all 0.2s ease;
        }

        .use-btn:hover {
            transform: translateY(-1px);
            box-shadow: 0 4px 10px rgba(245,158,11,0.3);
        }

        .use-btn:active {
            transform: scale(0.95);
            background: #b45309;
        }

        .use-btn:disabled {
            background: #475569;
            box-shadow: none;
            cursor: not-allowed;
            transform: none;
        }

        @keyframes pulse {
            from { opacity: 0.4; }
            to { opacity: 1; }
        }
    </style>
</head>
<body>
    <div class="hud-header">
        <h1>S.H.I.E.L.D. Mainframe</h1>
        <p>Tactical Operation Center | Active Session</p>
    </div>

    <div class="grid">
        <div class="panel">
            <div class="panel-title">Agent Credentials</div>
            <div class="status-box">
                <div class="status-dot"></div>
                Node Synced Successfully - Level 1 Clearance
            </div>
            
            <div class="agent-stats">
                <div class="stat-item">
                    <div class="stat-label">Agent ID</div>
                    <div class="stat-value highlight">{{agentName}}</div>
                </div>
                <div class="stat-item">
                    <div class="stat-label">Clearance Level</div>
                    <div class="stat-value">Lvl {{level}}</div>
                </div>
                <div class="stat-item">
                    <div class="stat-label">MobaCoins</div>
                    <div class="stat-value gold">{{coins:N0}}</div>
                </div>
                <div class="stat-item">
                    <div class="stat-label">Silver Balance</div>
                    <div class="stat-value">{{silver:N0}}</div>
                </div>
            </div>
        </div>

        <div class="panel">
            <div class="panel-title">Command Diagnostics</div>
            <div class="console" id="diagnosticConsole">
                <div class="console-line"><span class="console-time">[{{DateTime.UtcNow:HH:mm:ss}}]</span>System initialized.</div>
                <div class="console-line"><span class="console-time">[{{DateTime.UtcNow:HH:mm:ss}}]</span>Session linked to DeNA Social Core.</div>
                <div class="console-line"><span class="console-time">[{{DateTime.UtcNow:HH:mm:ss}}]</span>Verification signature validated.</div>
                <div class="console-line"><span class="console-time">[{{DateTime.UtcNow:HH:mm:ss}}]</span>Secure loopback proxy connected at 10.0.2.2.</div>
            </div>
        </div>

        <div class="panel">
            <div class="panel-title">S.H.I.E.L.D. Inventory Depot</div>
            <div class="items-container">
                {{itemsHtmlList}}
            </div>
        </div>

        <div class="panel">
            <div class="panel-title">Tactical Card Deployments</div>
            <div class="cards-container">
                {{cardsHtmlList}}
            </div>
        </div>
    </div>

    <script>
        function mainframeUseItem(itemId, btn) {
            btn.disabled = true;
            btn.textContent = "...";
            
            fetch('/ultimate/item/use_item', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/x-www-form-urlencoded'
                },
                body: 'item_id=' + itemId
            })
            .then(res => res.json())
            .then(data => {
                const consoleEl = document.getElementById('diagnosticConsole');
                const timeStr = new Date().toUTCString().split(' ')[4];
                
                if (data.success) {
                    const qtyEl = document.getElementById('mainframe-qty-' + itemId);
                    if (qtyEl) {
                        qtyEl.textContent = 'x' + data.remaining_quantity;
                    }
                    if (data.remaining_quantity <= 0) {
                        btn.style.display = 'none';
                    } else {
                        btn.disabled = false;
                        btn.textContent = 'USE';
                    }
                    
                    const newLine = document.createElement('div');
                    newLine.className = 'console-line';
                    newLine.innerHTML = `<span class='console-time'>[${timeStr}]</span>${data.message || 'Item applied successfully!'}`;
                    consoleEl.appendChild(newLine);
                    consoleEl.scrollTop = consoleEl.scrollHeight;
                } else {
                    btn.disabled = false;
                    btn.textContent = 'USE';
                    alert(data.message || 'Consumption failed.');
                }
            })
            .catch(err => {
                btn.disabled = false;
                btn.textContent = 'USE';
                console.error(err);
                alert('Connection failure to secure terminal node.');
            });
        }
    </script>
</body>
</html>
""";

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

        // ==========================================
        // --- ITEM SYSTEM & NAVIGATION WEBVIEWS ---
        // ==========================================

        private UserAccount ResolveCurrentUser()
        {
            var user = (UserAccount?)null;
            
            // 1. Try GAuth Token
            if (HttpContext.Items.TryGetValue("GAuthToken", out var tokenObj) && tokenObj is string gauthToken)
            {
                user = _authService.GetUserByToken(gauthToken);
            }
            
            // 2. Try Session Cookie
            if (user == null)
            {
                var sessionId = Request.Cookies["sid"];
                if (!string.IsNullOrEmpty(sessionId))
                {
                    user = _authService.GetUserBySessionId(sessionId);
                }
            }
            
            // 3. Fallback
            if (user == null)
            {
                user = _authService.ValidateUser("testuser", "password");
            }
            
            return user!;
        }

        [HttpPost("item/get_item_list")]
        public IActionResult GetItemList()
        {
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;
            
            var inventory = _dbContext.PlayerInventoryItems
                .Include(pi => pi.ItemTemplate)
                .Where(pi => pi.PlayerProfileId == profileId)
                .ToList();
                
            var responseList = inventory.Select(pi => new
            {
                item_id = pi.ItemTemplateId,
                name = pi.ItemTemplate?.Name ?? "Unknown Item",
                count = pi.Quantity,
                description = pi.ItemTemplate?.Description ?? "",
                type = pi.ItemTemplate?.Type ?? "EnergyRestorative",
                image_url = $"http://10.0.2.2:5000/assets/items/{pi.ItemTemplate?.ImageFileName}"
            }).ToList();
            
            return Ok(new
            {
                item_list = responseList,
                success = true
            });
        }

        [HttpPost("item/use_item")]
        public async Task<IActionResult> UseItem()
        {
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;
            
            int itemId = 0;
            if (Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync();
                int.TryParse(form["item_id"].ToString(), out itemId);
            }
            
            if (itemId == 0 && Request.Query.TryGetValue("item_id", out var qVal))
            {
                int.TryParse(qVal.ToString(), out itemId);
            }
            
            if (itemId == 0)
            {
                try
                {
                    using var reader = new StreamReader(Request.Body);
                    var body = await reader.ReadToEndAsync();
                    if (!string.IsNullOrEmpty(body))
                    {
                        using var doc = JsonDocument.Parse(body);
                        if (doc.RootElement.TryGetProperty("item_id", out var prop))
                        {
                            itemId = prop.GetInt32();
                        }
                    }
                }
                catch {}
            }
            
            if (itemId == 0)
            {
                return BadRequest(new { success = false, message = "Missing item_id parameter." });
            }

            var invItem = _dbContext.PlayerInventoryItems
                .Include(pi => pi.ItemTemplate)
                .FirstOrDefault(pi => pi.PlayerProfileId == profileId && pi.ItemTemplateId == itemId);
                
            if (invItem == null || invItem.Quantity <= 0)
            {
                return Ok(new { success = false, message = "Insufficient item quantity." });
            }
            
            var profile = _dbContext.Profiles.FirstOrDefault(p => p.Id == profileId);
            if (profile == null)
            {
                return NotFound();
            }

            // Apply item effect
            string message = "";
            if (invItem.ItemTemplate!.Type == "EnergyRestorative")
            {
                int refill = (profile.EnergyMax * invItem.ItemTemplate.EffectValue) / 100;
                profile.EnergyCurrent = Math.Min(profile.EnergyMax, profile.EnergyCurrent + refill);
                message = $"Energy restored by {refill} points!";
            }
            else if (invItem.ItemTemplate.Type == "AttackPowerRestorative")
            {
                message = "Combat attack power fully replenished!";
            }
            else if (invItem.ItemTemplate.Type == "DefensePowerRestorative")
            {
                message = "Combat defense power fully replenished!";
            }
            else if (invItem.ItemTemplate.Type == "MasteryIso8")
            {
                message = "ISO-8 synthesized card mastery incremented successfully!";
            }
            
            invItem.Quantity--;
            _dbContext.SaveChanges();
            
            return Ok(new
            {
                success = true,
                message = message,
                item_id = itemId,
                remaining_quantity = invItem.Quantity,
                player_status = new
                {
                    level = profile.Level,
                    energy_max = profile.EnergyMax,
                    energy_current = profile.EnergyCurrent,
                    silver = profile.SilverBalance,
                    mobacoin = profile.MobaCoinBalance
                }
            });
        }

        [HttpGet("menu")]
        [HttpGet("menu/index")]
        public IActionResult ServeMenuHubPage()
        {
            _logger.LogInformation("[Cygames] ServeMenuHubPage called.");
            var user = ResolveCurrentUser();
            var agentName = user.Profile?.Nickname ?? "Agent";
            var level = user.Profile?.Level ?? 1;

            var html = $$"""
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no">
    <title>S.H.I.E.L.D. Tactical Hub</title>
    <link href="https://fonts.googleapis.com/css2?family=Outfit:wght@300;400;600;800&family=Space+Mono:wght@400;700&display=swap" rel="stylesheet">
    <style>
        :root {
            --bg-color: #04080f;
            --hud-blue: #00f0ff;
            --hud-glow: rgba(0, 240, 255, 0.15);
            --panel-bg: rgba(6, 12, 24, 0.9);
            --border-color: rgba(0, 240, 255, 0.3);
            --text-color: #e2e8f0;
            --accent-red: #ef4444;
            --accent-gold: #f59e0b;
        }

        * {
            box-sizing: border-box;
            margin: 0;
            padding: 0;
        }

        body {
            background-color: var(--bg-color);
            background-image: 
                radial-gradient(at 50% 0%, rgba(0, 240, 255, 0.1) 0px, transparent 60%);
            color: var(--text-color);
            font-family: 'Outfit', sans-serif;
            min-height: 100vh;
            display: flex;
            flex-direction: column;
            overflow-x: hidden;
            padding: 15px;
        }

        .header {
            display: flex;
            justify-content: space-between;
            align-items: center;
            border-bottom: 2px solid var(--border-color);
            padding-bottom: 12px;
            margin-bottom: 20px;
            box-shadow: 0 4px 15px rgba(0, 240, 255, 0.05);
        }

        .header h1 {
            font-size: 18px;
            font-weight: 800;
            text-transform: uppercase;
            letter-spacing: 2px;
            color: #ffffff;
            text-shadow: 0 0 10px var(--hud-glow);
        }

        .agent-badge {
            background: rgba(0, 240, 255, 0.08);
            border: 1px solid var(--border-color);
            border-radius: 6px;
            padding: 4px 10px;
            font-size: 11px;
            font-family: 'Space Mono', monospace;
            color: var(--hud-blue);
        }

        .menu-grid {
            display: grid;
            grid-template-columns: repeat(2, 1fr);
            gap: 15px;
            flex-grow: 1;
        }

        .menu-btn {
            background: var(--panel-bg);
            border: 1px solid var(--border-color);
            border-radius: 12px;
            display: flex;
            flex-direction: column;
            justify-content: center;
            align-items: center;
            padding: 25px 15px;
            text-decoration: none;
            color: var(--text-color);
            transition: all 0.25s ease;
            box-shadow: 0 4px 10px rgba(0, 0, 0, 0.4);
            position: relative;
            overflow: hidden;
        }

        .menu-btn::before {
            content: '';
            position: absolute;
            top: 0; left: 0; width: 100%; height: 100%;
            background: linear-gradient(135deg, rgba(255,255,255,0.03) 0%, rgba(255,255,255,0) 100%);
            z-index: 1;
        }

        .menu-btn:active {
            transform: scale(0.96);
            border-color: var(--hud-blue);
            box-shadow: 0 0 15px var(--hud-glow);
        }

        .menu-icon {
            font-size: 32px;
            margin-bottom: 12px;
            text-shadow: 0 0 10px rgba(255,255,255,0.1);
        }

        .menu-title {
            font-size: 14px;
            font-weight: 700;
            text-transform: uppercase;
            letter-spacing: 1px;
            color: #ffffff;
            margin-bottom: 4px;
        }

        .menu-subtitle {
            font-size: 10px;
            color: #64748b;
            text-align: center;
        }

        .menu-btn.items-btn {
            border-color: rgba(245, 158, 11, 0.4);
        }
        .menu-btn.items-btn:active {
            border-color: var(--accent-gold);
            box-shadow: 0 0 15px rgba(245, 158, 11, 0.2);
        }
        .menu-btn.items-btn .menu-title {
            color: var(--accent-gold);
        }

        .footer {
            text-align: center;
            font-size: 10px;
            color: #475569;
            font-family: 'Space Mono', monospace;
            margin-top: 30px;
            letter-spacing: 1px;
        }
    </style>
</head>
<body>
    <div class="header">
        <h1>S.H.I.E.L.D. HUB</h1>
        <div class="agent-badge">{{agentName}} [LVL {{level}}]</div>
    </div>

    <div class="menu-grid">
        <a href="/ultimate" class="menu-btn">
            <span class="menu-icon">📡</span>
            <span class="menu-title">COMMAND</span>
            <span class="menu-subtitle">Tactical Mainframe</span>
        </a>

        <a href="/ultimate/item/index" class="menu-btn items-btn">
            <span class="menu-icon">🎒</span>
            <span class="menu-title">ITEMS</span>
            <span class="menu-subtitle">Depot & ISO-8 Supply</span>
        </a>

        <a href="#" class="menu-btn" onclick="alert('Secure Link Initialized: Gacha Depot is currently offline.')">
            <span class="menu-icon">🎟️</span>
            <span class="menu-title">SUMMON</span>
            <span class="menu-subtitle">Recruit Hero Assets</span>
        </a>

        <a href="#" class="menu-btn" onclick="alert('Mission Briefing: Quest Terminal offline.')">
            <span class="menu-icon">⚔️</span>
            <span class="menu-title">MISSIONS</span>
            <span class="menu-subtitle">Secure ISO-8 Nodes</span>
        </a>
    </div>

    <div class="footer">
        // SECURE NET PROTOCOL ACTIVE
    </div>
</body>
</html>
""";
            return Content(html, "text/html");
        }

        [HttpGet("item")]
        [HttpGet("item/index")]
        public IActionResult ServeItemsInventoryPage()
        {
            _logger.LogInformation("[Cygames] ServeItemsInventoryPage called.");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            var profile = _dbContext.Profiles
                .Include(p => p.InventoryItems)
                .ThenInclude(pi => pi.ItemTemplate)
                .FirstOrDefault(p => p.Id == profileId);

            var agentName = profile?.Nickname ?? "Agent";
            var energyCur = profile?.EnergyCurrent ?? 0;
            var energyMax = profile?.EnergyMax ?? 100;
            var silver = profile?.SilverBalance ?? 0;
            var mobacoin = profile?.MobaCoinBalance ?? 0;

            var itemsHtml = "";
            if (profile != null && profile.InventoryItems.Any())
            {
                foreach (var pi in profile.InventoryItems)
                {
                    var temp = pi.ItemTemplate;
                    if (temp == null) continue;

                    var icon = temp.Type switch
                    {
                        "EnergyRestorative" => "⚡",
                        "AttackPowerRestorative" => "🔥",
                        "DefensePowerRestorative" => "🛡️",
                        "MasteryIso8" => "🧪",
                        _ => "📦"
                    };

                    var color = temp.Type switch
                    {
                        "EnergyRestorative" => "#00f0ff",
                        "AttackPowerRestorative" => "#ef4444",
                        "DefensePowerRestorative" => "#10b981",
                        "MasteryIso8" => "#a5b4fc",
                        _ => "#f59e0b"
                    };

                    var useButton = temp.Type.EndsWith("Restorative") 
                        ? $"<button class='use-btn' onclick='useItem({temp.Id}, this)'>USE</button>"
                        : "<span class='passive-badge'>STOCK</span>";

                    itemsHtml += $"""
            <div class="item-row" id="item-row-{temp.Id}">
                <div class="item-visual" style="border-color: {color}; color: {color};">
                    {icon}
                </div>
                <div class="item-body">
                    <div class="item-header-row">
                        <span class="item-title">{temp.Name}</span>
                        <span class="item-qty" id="item-qty-{temp.Id}">x{pi.Quantity}</span>
                    </div>
                    <p class="item-desc">{temp.Description}</p>
                </div>
                <div class="item-action">
                    {useButton}
                </div>
            </div>
""";
                }
            }
            else
            {
                itemsHtml = "<div class='no-items'>No item allocations located in tactical mainframe.</div>";
            }

            var html = $$"""
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no">
    <title>S.H.I.E.L.D. Items Depot</title>
    <link href="https://fonts.googleapis.com/css2?family=Outfit:wght@300;400;600;800&family=Space+Mono:wght@400;700&display=swap" rel="stylesheet">
    <style>
        :root {
            --bg-color: #04080f;
            --hud-blue: #00f0ff;
            --hud-glow: rgba(0, 240, 255, 0.15);
            --panel-bg: rgba(6, 12, 24, 0.9);
            --border-color: rgba(0, 240, 255, 0.25);
            --text-color: #e2e8f0;
            --accent-gold: #f59e0b;
        }

        * {
            box-sizing: border-box;
            margin: 0;
            padding: 0;
        }

        body {
            background-color: var(--bg-color);
            background-image: 
                radial-gradient(at 50% 0%, rgba(245, 158, 11, 0.08) 0px, transparent 60%);
            color: var(--text-color);
            font-family: 'Outfit', sans-serif;
            min-height: 100vh;
            display: flex;
            flex-direction: column;
            overflow-x: hidden;
            padding: 12px;
        }

        .header {
            display: flex;
            align-items: center;
            border-bottom: 2px solid var(--border-color);
            padding-bottom: 10px;
            margin-bottom: 15px;
        }

        .back-btn {
            background: none;
            border: 1px solid var(--border-color);
            color: var(--hud-blue);
            border-radius: 6px;
            padding: 4px 10px;
            font-size: 11px;
            font-weight: 700;
            cursor: pointer;
            text-decoration: none;
            font-family: 'Space Mono', monospace;
            margin-right: 15px;
            transition: all 0.2s ease;
        }

        .back-btn:active {
            transform: scale(0.95);
            background: rgba(0, 240, 255, 0.1);
        }

        .header h1 {
            font-size: 16px;
            font-weight: 800;
            text-transform: uppercase;
            letter-spacing: 1px;
            color: #ffffff;
            flex-grow: 1;
        }

        /* Status HUD */
        .status-hud {
            background: var(--panel-bg);
            border: 1px solid var(--border-color);
            border-radius: 10px;
            padding: 10px 12px;
            margin-bottom: 15px;
            box-shadow: 0 4px 10px rgba(0,0,0,0.3);
        }

        .status-row {
            display: flex;
            justify-content: space-between;
            align-items: center;
            margin-bottom: 8px;
        }

        .status-row:last-child {
            margin-bottom: 0;
        }

        .hud-lbl {
            font-size: 10px;
            font-weight: 600;
            text-transform: uppercase;
            letter-spacing: 0.5px;
            color: #94a3b8;
        }

        .hud-val {
            font-size: 12px;
            font-weight: 700;
            font-family: 'Space Mono', monospace;
        }

        .bar-container {
            width: 100%;
            height: 6px;
            background: rgba(255,255,255,0.05);
            border-radius: 3px;
            overflow: hidden;
            border: 1px solid rgba(0,240,255,0.1);
            margin-top: 4px;
        }

        .bar-fill {
            height: 100%;
            background: linear-gradient(90deg, #00b8ff, var(--hud-blue));
            box-shadow: 0 0 8px var(--hud-blue);
            transition: width 0.4s cubic-bezier(0.16, 1, 0.3, 1);
        }

        /* Item Rows */
        .items-list {
            display: flex;
            flex-direction: column;
            gap: 10px;
            flex-grow: 1;
        }

        .item-row {
            background: var(--panel-bg);
            border: 1px solid rgba(255,255,255,0.06);
            border-radius: 10px;
            display: flex;
            align-items: center;
            padding: 10px;
            box-shadow: 0 2px 8px rgba(0,0,0,0.2);
            transition: all 0.2s ease;
        }

        .item-visual {
            width: 42px;
            height: 42px;
            border: 1.5px solid var(--border-color);
            border-radius: 8px;
            display: flex;
            justify-content: center;
            align-items: center;
            font-size: 20px;
            margin-right: 12px;
            flex-shrink: 0;
            box-shadow: inset 0 0 5px rgba(255,255,255,0.03);
        }

        .item-body {
            flex-grow: 1;
            min-width: 0;
        }

        .item-header-row {
            display: flex;
            justify-content: space-between;
            align-items: center;
            margin-bottom: 2px;
        }

        .item-title {
            font-size: 13px;
            font-weight: 700;
            color: #ffffff;
            white-space: nowrap;
            overflow: hidden;
            text-overflow: ellipsis;
        }

        .item-qty {
            font-size: 11px;
            font-weight: 700;
            font-family: 'Space Mono', monospace;
            color: var(--accent-gold);
            margin-left: 8px;
        }

        .item-desc {
            font-size: 10px;
            color: #64748b;
            line-height: 1.3;
            white-space: nowrap;
            overflow: hidden;
            text-overflow: ellipsis;
        }

        .item-action {
            margin-left: 10px;
            flex-shrink: 0;
        }

        .use-btn {
            background: linear-gradient(185deg, var(--accent-gold), #b45309);
            border: none;
            color: #ffffff;
            font-weight: 700;
            font-size: 11px;
            padding: 6px 14px;
            border-radius: 6px;
            cursor: pointer;
            box-shadow: 0 2px 6px rgba(245,158,11,0.2);
            transition: all 0.2s ease;
        }

        .use-btn:active {
            transform: scale(0.93);
            background: #b45309;
        }

        .passive-badge {
            font-size: 9px;
            font-family: 'Space Mono', monospace;
            color: #475569;
            border: 1px solid rgba(255,255,255,0.03);
            padding: 2px 6px;
            border-radius: 4px;
        }

        /* Toast notifications */
        .toast {
            position: fixed;
            bottom: 20px;
            left: 50%;
            transform: translateX(-50%) translateY(100px);
            background: rgba(16, 185, 129, 0.95);
            border: 1px solid #34d399;
            color: #ffffff;
            padding: 8px 16px;
            border-radius: 30px;
            font-size: 12px;
            font-weight: 600;
            z-index: 1000;
            opacity: 0;
            transition: all 0.3s cubic-bezier(0.175, 0.885, 0.32, 1.275);
            box-shadow: 0 10px 25px rgba(16,185,129,0.3);
        }

        .toast.show {
            transform: translateX(-50%) translateY(0);
            opacity: 1;
        }
    </style>
</head>
<body>
    <div class="header">
        <a href="/ultimate/menu" class="back-btn">&lt; BACK</a>
        <h1>ITEMS DEPOT</h1>
    </div>

    <!-- HUD Status -->
    <div class="status-hud">
        <div class="status-row">
            <span class="hud-lbl">ENERGY SYNC</span>
            <span class="hud-val" id="energy-val">{{energyCur}} / {{energyMax}}</span>
        </div>
        <div class="bar-container">
            <div class="bar-fill" id="energy-bar" style="width: {{(energyCur * 100) / energyMax}}%;"></div>
        </div>
    </div>

    <!-- Scrollable inventory items list -->
    <div class="items-list">
        {{itemsHtml}}
    </div>

    <div class="toast" id="refill-toast">ISO-8 CONSUMED: Status secured</div>

    <script>
        function showToast(message) {
            const toast = document.getElementById('refill-toast');
            toast.textContent = message;
            toast.classList.add('show');
            setTimeout(() => {
                toast.classList.remove('show');
            }, 2500);
        }

        function useItem(itemId, btn) {
            btn.disabled = true;
            btn.textContent = "...";

            fetch('/ultimate/item/use_item', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/x-www-form-urlencoded'
                },
                body: 'item_id=' + itemId
            })
            .then(res => res.json())
            .then(data => {
                if (data.success) {
                    // Update item stock count in UI
                    const qtyEl = document.getElementById('item-qty-' + itemId);
                    qtyEl.textContent = 'x' + data.remaining_quantity;
                    
                    if (data.remaining_quantity <= 0) {
                        btn.style.display = 'none';
                        qtyEl.style.color = '#475569';
                    } else {
                        btn.disabled = false;
                        btn.textContent = 'USE';
                    }

                    // Dynamically update energy bar if applicable
                    if (data.player_status) {
                        const status = data.player_status;
                        const energyStr = status.energy_current + ' / ' + status.energy_max;
                        document.getElementById('energy-val').textContent = energyStr;
                        
                        const pct = (status.energy_current * 100) / status.energy_max;
                        document.getElementById('energy-bar').style.width = pct + '%';
                    }

                    showToast(data.message || 'ISO-8 secured and applied successfully!');
                } else {
                    btn.disabled = false;
                    btn.textContent = 'USE';
                    alert(data.message || 'Consumption failed.');
                }
            })
            .catch(err => {
                btn.disabled = false;
                btn.textContent = 'USE';
                console.error(err);
                alert('Connection failure to secure terminal node.');
            });
        }
    </script>
</body>
</html>
""";
            return Content(html, "text/html");
        }
    }
}
