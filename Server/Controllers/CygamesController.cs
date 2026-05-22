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

            var profileId = user?.Profile?.Id ?? 1;
            var profile = _dbContext.Profiles.FirstOrDefault(p => p.Id == profileId);
            var agentName = profile?.Nickname ?? "TestAgent";
            var clearanceCode = profileId.ToString("D4"); // Pad to look like a S.H.I.E.L.D. badge id

            var html = $$"""
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no">
    <title>Marvel War of Heroes - S.H.I.E.L.D. Operations</title>
    <link href="https://fonts.googleapis.com/css2?family=Outfit:wght@300;400;600;800&family=Space+Mono:wght@400;700&display=swap" rel="stylesheet">
    <style>
        :root {
            --bg-color: #030712;
            --hud-blue: #00f0ff;
            --hud-blue-glow: rgba(0, 240, 255, 0.4);
            --hud-green: #10b981;
            --hud-green-glow: rgba(16, 185, 129, 0.3);
            --accent-gold: #f59e0b;
            --accent-red: #ef4444;
            --text-color: #f3f4f6;
            --text-muted: #9ca3af;
            --panel-bg: rgba(15, 23, 42, 0.6);
            --border-color: rgba(0, 240, 255, 0.15);
        }

        * {
            box-sizing: border-box;
            margin: 0;
            padding: 0;
        }

        body {
            background-color: var(--bg-color);
            background-image: 
                radial-gradient(at 50% 0%, rgba(0, 240, 255, 0.12) 0px, transparent 65%),
                linear-gradient(rgba(0, 240, 255, 0.015) 1px, transparent 1px),
                linear-gradient(90deg, rgba(0, 240, 255, 0.015) 1px, transparent 1px);
            background-size: 100% 100%, 25px 25px, 25px 25px;
            color: var(--text-color);
            font-family: 'Outfit', sans-serif;
            min-height: 100vh;
            display: flex;
            flex-direction: column;
            align-items: center;
            overflow-x: hidden;
            scroll-behavior: smooth;
        }

        /* Scanline Overlay for retro-futurism */
        .scanlines {
            position: fixed;
            top: 0;
            left: 0;
            width: 100%;
            height: 100%;
            background: linear-gradient(
                rgba(18, 16, 16, 0) 50%, 
                rgba(0, 0, 0, 0.15) 50%
            ), linear-gradient(
                90deg, 
                rgba(255, 0, 0, 0.02), 
                rgba(0, 255, 0, 0.01), 
                rgba(0, 0, 255, 0.02)
            );
            background-size: 100% 4px, 6px 100%;
            z-index: 999;
            pointer-events: none;
        }

        /* Splash Screen (100vh) */
        .splash-container {
            display: flex;
            flex-direction: column;
            justify-content: space-between;
            align-items: center;
            width: 100%;
            height: 100vh;
            padding: 30px 20px;
            position: relative;
            z-index: 10;
        }

        .auth-header {
            width: 100%;
            max-width: 480px;
            display: flex;
            justify-content: space-between;
            align-items: center;
            font-family: 'Space Mono', monospace;
            font-size: 11px;
            letter-spacing: 1px;
            text-transform: uppercase;
            padding: 8px 12px;
            background: rgba(0, 0, 0, 0.3);
            border: 1px solid rgba(255, 255, 255, 0.05);
            border-radius: 6px;
            color: var(--text-muted);
        }

        .auth-status {
            display: flex;
            align-items: center;
            color: var(--hud-green);
            text-shadow: 0 0 4px var(--hud-green-glow);
        }

        .auth-dot {
            width: 6px;
            height: 6px;
            background-color: var(--hud-green);
            border-radius: 50%;
            margin-right: 6px;
            box-shadow: 0 0 6px var(--hud-green);
            animation: pulse 1.5s infinite alternate;
        }

        .logo-area {
            flex-grow: 1;
            display: flex;
            justify-content: center;
            align-items: center;
            position: relative;
        }

        /* Concentric HUD Rings */
        .hud-ring-outer {
            width: 180px;
            height: 180px;
            border: 1px dashed var(--hud-blue);
            border-radius: 50%;
            display: flex;
            justify-content: center;
            align-items: center;
            box-shadow: 0 0 15px rgba(0, 240, 255, 0.05);
            animation: rotateCW 20s linear infinite;
        }

        .hud-ring-inner {
            width: 150px;
            height: 150px;
            border: 1px solid rgba(0, 240, 255, 0.1);
            border-top: 2px solid var(--hud-blue);
            border-bottom: 2px solid var(--hud-blue);
            border-radius: 50%;
            display: flex;
            justify-content: center;
            align-items: center;
            animation: rotateCCW 12s linear infinite;
        }

        .crest-center {
            width: 110px;
            height: 110px;
            background: radial-gradient(circle, rgba(6, 12, 24, 0.9) 0%, rgba(3, 7, 18, 0.95) 100%);
            border: 1px solid var(--border-color);
            border-radius: 50%;
            display: flex;
            justify-content: center;
            align-items: center;
            box-shadow: inset 0 0 15px rgba(0, 240, 255, 0.2);
            position: absolute;
            z-index: 5;
        }

        .crest-icon {
            width: 60px;
            height: 60px;
            fill: var(--hud-blue);
            filter: drop-shadow(0 0 8px var(--hud-blue-glow));
            animation: pulseOpacity 3s ease-in-out infinite;
        }

        .splash-bottom {
            width: 100%;
            max-width: 480px;
            text-align: center;
            display: flex;
            flex-direction: column;
            align-items: center;
        }

        .game-title {
            font-size: 2.2rem;
            font-weight: 800;
            letter-spacing: 2px;
            text-transform: uppercase;
            background: linear-gradient(180deg, #ffffff 0%, #cbd5e1 100%);
            -webkit-background-clip: text;
            -webkit-text-fill-color: transparent;
            text-shadow: 0 0 20px rgba(0, 240, 255, 0.3);
            margin-bottom: 8px;
            line-height: 1.1;
        }

        .game-subtitle {
            font-family: 'Space Mono', monospace;
            font-size: 13px;
            font-weight: 400;
            color: var(--hud-blue);
            letter-spacing: 3px;
            text-transform: uppercase;
            text-shadow: 0 0 6px var(--hud-blue-glow);
            margin-bottom: 35px;
        }

        .scroll-indicator {
            display: flex;
            flex-direction: column;
            align-items: center;
            font-family: 'Space Mono', monospace;
            font-size: 9px;
            letter-spacing: 2px;
            color: var(--text-muted);
            text-transform: uppercase;
            cursor: pointer;
            transition: color 0.3s ease;
        }

        .scroll-indicator:hover {
            color: var(--hud-blue);
        }

        .scroll-arrow {
            font-size: 16px;
            margin-top: 5px;
            color: var(--hud-blue);
            animation: bounce 1.6s infinite;
        }

        /* News Section (scrolled state) */
        .news-container {
            width: 100%;
            max-width: 480px;
            padding: 40px 20px 80px 20px;
            z-index: 10;
            display: flex;
            flex-direction: column;
            gap: 25px;
        }

        .news-banner {
            background: linear-gradient(135deg, rgba(0, 240, 255, 0.08) 0%, rgba(10, 15, 29, 0.7) 100%);
            border: 1px solid rgba(0, 240, 255, 0.25);
            border-radius: 12px;
            padding: 22px;
            position: relative;
            overflow: hidden;
            backdrop-filter: blur(10px);
            box-shadow: 0 10px 25px rgba(0, 0, 0, 0.3);
        }

        .news-banner::before {
            content: '';
            position: absolute;
            top: 0;
            left: 0;
            width: 4px;
            height: 100%;
            background: var(--hud-blue);
            box-shadow: 0 0 8px var(--hud-blue);
        }

        .banner-tag {
            font-family: 'Space Mono', monospace;
            font-size: 9px;
            font-weight: 700;
            background: rgba(0, 240, 255, 0.15);
            color: var(--hud-blue);
            border: 1px solid var(--hud-blue);
            padding: 2px 8px;
            border-radius: 4px;
            width: fit-content;
            margin-bottom: 12px;
            letter-spacing: 1px;
        }

        .news-banner h2 {
            font-size: 18px;
            font-weight: 700;
            color: #ffffff;
            margin-bottom: 8px;
            letter-spacing: 0.5px;
        }

        .news-banner p {
            font-size: 12px;
            color: var(--text-muted);
            line-height: 1.5;
        }

        .section-header {
            font-family: 'Space Mono', monospace;
            font-size: 13px;
            font-weight: 700;
            letter-spacing: 2px;
            color: var(--text-muted);
            border-bottom: 1px solid rgba(255, 255, 255, 0.08);
            padding-bottom: 8px;
            display: flex;
            align-items: center;
            justify-content: space-between;
        }

        .news-list {
            display: flex;
            flex-direction: column;
            gap: 15px;
        }

        .news-card {
            background: var(--panel-bg);
            border: 1px solid var(--border-color);
            border-left: 3px solid rgba(255, 255, 255, 0.15);
            border-radius: 8px;
            padding: 16px;
            backdrop-filter: blur(8px);
            transition: all 0.3s ease;
        }

        .news-card:hover {
            transform: translateX(4px);
            border-left-color: var(--hud-blue);
            border-color: rgba(0, 240, 255, 0.2);
            box-shadow: 0 4px 15px rgba(0, 240, 255, 0.05);
        }

        .card-top {
            display: flex;
            justify-content: space-between;
            align-items: center;
            margin-bottom: 8px;
        }

        .card-tag {
            font-family: 'Space Mono', monospace;
            font-size: 9px;
            font-weight: 600;
            color: var(--text-muted);
            letter-spacing: 0.5px;
        }

        .card-date {
            font-family: 'Space Mono', monospace;
            font-size: 9px;
            color: #64748b;
        }

        .news-card h3 {
            font-size: 14px;
            font-weight: 600;
            color: #ffffff;
            margin-bottom: 6px;
        }

        .news-card p {
            font-size: 11px;
            color: var(--text-muted);
            line-height: 1.5;
        }

        .page-footer {
            text-align: center;
            font-family: 'Space Mono', monospace;
            font-size: 9px;
            color: #475569;
            letter-spacing: 1px;
            text-transform: uppercase;
            margin-top: 20px;
        }

        /* Animations */
        @keyframes rotateCW {
            from { transform: rotate(0deg); }
            to { transform: rotate(360deg); }
        }

        @keyframes rotateCCW {
            from { transform: rotate(0deg); }
            to { transform: rotate(-360deg); }
        }

        @keyframes pulse {
            from { opacity: 0.3; }
            to { opacity: 1; }
        }

        @keyframes pulseOpacity {
            0%, 100% { opacity: 0.8; }
            50% { opacity: 0.45; }
        }

        @keyframes bounce {
            0%, 100% { transform: translateY(0); }
            50% { transform: translateY(5px); }
        }
    </style>
</head>
<body>
    <!-- Background grid elements -->
    <div class="scanlines"></div>

    <!-- Splash Screen Container -->
    <div class="splash-container">
        <!-- Authorized Banner -->
        <div class="auth-header">
            <span>DECRYPT NODE // COD-{{clearanceCode}}</span>
            <div class="auth-status">
                <div class="auth-dot"></div>
                <span>AGENT SYNCED</span>
            </div>
        </div>

        <!-- S.H.I.E.L.D. Logo -->
        <div class="logo-area">
            <div class="hud-ring-outer">
                <div class="hud-ring-inner">
                    <!-- Concentric circles logic -->
                </div>
            </div>
            <div class="crest-center">
                <!-- Glowing Shield crest SVG -->
                <svg class="crest-icon" viewBox="0 0 24 24">
                    <path d="M12 2L4 5v6.09c0 5.05 3.41 9.76 8 10.91 4.59-1.15 8-5.86 8-10.91V5l-8-3zm0 10h6c-.47 3.51-2.92 6.55-6 7.42V12H6V7.21l6-2.25V12z"/>
                </svg>
            </div>
        </div>

        <!-- Typography & Deck Callout -->
        <div class="splash-bottom">
            <h1 class="game-title">Marvel War of Heroes</h1>
            <p class="game-subtitle">Form your Deck to Win</p>
            
            <div class="scroll-indicator" onclick="document.getElementById('newsFeed').scrollIntoView();">
                <span>INTEL READOUT</span>
                <span class="scroll-arrow">↓</span>
            </div>
        </div>
    </div>

    <!-- Scrolling News Container -->
    <div class="news-container" id="newsFeed">
        <div class="news-banner">
            <div class="banner-tag">LIVE EVENT</div>
            <h2>AVENGERS ASSEMBLE: MULTIVERSE CLASH</h2>
            <p>A dimensional tear has breached sector-9. Assemble your high-fidelity Bruiser, Speed, and Tactics decks. Engage incursions to extract Iso-8 crystals and protect the timeline!</p>
        </div>

        <div class="section-header">
            <span>// LATEST NEWS & INTEL</span>
            <span>NODE-{{agentName.ToUpper()}}</span>
        </div>

        <div class="news-list">
            <!-- News Item 1 -->
            <div class="news-card">
                <div class="card-top">
                    <span class="card-tag" style="color: var(--hud-blue);">SYSTEM NODE</span>
                    <span class="card-date">2026-05-22</span>
                </div>
                <h3>S.H.I.E.L.D. Items Depot Synchronized</h3>
                <p>Database systems are fully synchronized with DeNA OpenSocial APIs. 42 historical restorations and Iso-8 consumables have been cataloged and seeded.</p>
            </div>

            <!-- News Item 2 -->
            <div class="news-card">
                <div class="card-top">
                    <span class="card-tag" style="color: var(--accent-gold);">EVENT INTEL</span>
                    <span class="card-date">2026-05-23</span>
                </div>
                <h3>Stamina & Power Refill Protocols Online</h3>
                <p>Equip and consume Energy Iso-8 canisters and power kits directly within the mobile Items interface to replenish battle stats in real time.</p>
            </div>

            <!-- News Item 3 -->
            <div class="news-card">
                <div class="card-top">
                    <span class="card-tag" style="color: var(--hud-green);">SECURITY CODE</span>
                    <span class="card-date">2026-05-24</span>
                </div>
                <h3>HMAC Cryptographic Validation Active</h3>
                <p>Game validation handshakes and callback integrations are fully authorized over loopback 10.0.2.2. Cleartext traffic routing is verified secure.</p>
            </div>
        </div>

        <div class="page-footer">
            S.H.I.E.L.D. SECURITY PROTOCOL 24754 // SECURE TRANSMISSION
        </div>
    </div>
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
