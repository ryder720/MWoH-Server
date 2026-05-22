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
using System.Collections.Generic;


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

            var replacements = new Dictionary<string, string>
            {
                { "clearanceCode", clearanceCode },
                { "agentName", agentName }
            };

            return Content(RenderTemplate("top.html", replacements), "text/html");
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

            var replacements = new Dictionary<string, string>
            {
                { "agentName", agentName },
                { "level", level.ToString() }
            };

            return Content(RenderTemplate("menu.html", replacements), "text/html");
        }

        [HttpGet("item")]
        [HttpGet("item/index")]
        public IActionResult ServeItemsInventoryPage()
        {
            _logger.LogInformation("[Cygames] ServeItemsInventoryPage called.");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            var profile = GetPlayerProfile(profileId, includeInventory: true);

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

            var energyPct = energyMax > 0 ? (energyCur * 100) / energyMax : 0;

            var replacements = new Dictionary<string, string>
            {
                { "energyCur", energyCur.ToString() },
                { "energyMax", energyMax.ToString() },
                { "energyPct", energyPct.ToString() },
                { "itemsHtml", itemsHtml }
            };

            return Content(RenderTemplate("item.html", replacements), "text/html");
        }

        [HttpGet("mypage")]
        public IActionResult ServeMyPage()
        {
            _logger.LogInformation("[Cygames] ServeMyPage called.");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            var profile = GetPlayerProfile(profileId);
            if (profile == null) return RedirectToAction("ServeGameTopPage");

            var cardsList = profile.Cards.ToList();
            var leader = cardsList.FirstOrDefault(c => c.IsLeader);
            
            var leaderHtml = "";
            if (leader != null)
            {
                var alignment = leader.CardTemplate?.Alignment ?? "Speed";
                var neonColor = alignment switch
                {
                    "Speed" => "#00f0ff",
                    "Bruiser" => "#ef4444",
                    "Tactics" => "#f59e0b",
                    _ => "#00f0ff"
                };
                var glowColor = alignment switch
                {
                    "Speed" => "rgba(0, 240, 255, 0.35)",
                    "Bruiser" => "rgba(239, 68, 68, 0.35)",
                    "Tactics" => "rgba(245, 158, 11, 0.35)",
                    _ => "rgba(0, 240, 255, 0.35)"
                };
                var icon = alignment switch
                {
                    "Speed" => "⚡",
                    "Bruiser" => "🔥",
                    "Tactics" => "🧠",
                    _ => "⭐"
                };
                var rarity = leader.CardTemplate?.Rarity ?? "Normal";
                var rarityColor = rarity switch
                {
                    "Normal" => "#e2e8f0",
                    "Rare" => "#38bdf8",
                    "Super Rare" => "#c084fc",
                    "Legendary" => "#f59e0b",
                    _ => "#e2e8f0"
                };

                leaderHtml = $"""
                <div class="leader-card" style="border-color: {neonColor}; box-shadow: 0 0 20px {glowColor};">
                    <div class="card-rarity-badge" style="color: {rarityColor}; border-color: {rarityColor};">{rarity.ToUpper()}</div>
                    <div class="card-title-bar">
                        <span class="card-name">{leader.CardTemplate?.Title ?? "Unknown Hero"}</span>
                        <span class="card-variant">{leader.CardTemplate?.VariantName ?? "Base"}</span>
                    </div>
                    <div class="card-artwork-frame">
                        <div class="card-artwork-placeholder">
                            <span class="artwork-icon">{icon}</span>
                            <span class="artwork-lbl">// Dossier Loaded</span>
                        </div>
                    </div>
                    <div class="card-stats-row">
                        <div class="card-stat">
                            <span class="stat-lbl">LEVEL</span>
                            <span class="stat-val">{leader.CurrentLevel}</span>
                        </div>
                        <div class="card-stat">
                            <span class="stat-lbl">POWER COST</span>
                            <span class="stat-val">{leader.CardTemplate?.PowerRequirement ?? 5}</span>
                        </div>
                        <div class="card-stat">
                            <span class="stat-lbl">ATK</span>
                            <span class="stat-val" style="color: #ef4444;">{leader.CurrentAtk}</span>
                        </div>
                        <div class="card-stat">
                            <span class="stat-lbl">DEF</span>
                            <span class="stat-val" style="color: #10b981;">{leader.CurrentDef}</span>
                        </div>
                    </div>
                </div>
                """;
            }
            else
            {
                leaderHtml = """
                <div class="leader-card empty-card">
                    <div class="card-artwork-placeholder">
                        <span class="artwork-icon">❔</span>
                        <span class="artwork-lbl">// NO LEADER DESIGNATED</span>
                        <a href="/ultimate/mypage/catalog" class="cta-btn-small">DESIGNATE LEADER</a>
                    </div>
                </div>
                """;
            }

            var atkDeck = cardsList.Where(c => c.IsInAttackDeck).ToList();
            var defDeck = cardsList.Where(c => c.IsInDefenseDeck).ToList();

            var attackDeckCount = atkDeck.Count;
            var attackDeckCost = atkDeck.Sum(c => c.CardTemplate?.PowerRequirement ?? 0);
            var attackPower = atkDeck.Sum(c => c.CurrentAtk);

            var defenseDeckCount = defDeck.Count;
            var defenseDeckCost = defDeck.Sum(c => c.CardTemplate?.PowerRequirement ?? 0);
            var defensePower = defDeck.Sum(c => c.CurrentDef);

            var expCurrent = profile.Experience;
            var expNext = profile.Level * 5000;
            var expPct = Math.Min(100, (expCurrent * 100) / expNext);

            var energyCur = profile.EnergyCurrent;
            var energyMax = profile.EnergyMax;
            var energyPct = Math.Min(100, (energyCur * 100) / energyMax);

            var replacements = new Dictionary<string, string>
            {
                { "clearanceCode", profile.Id.ToString("D4") },
                { "level", profile.Level.ToString() },
                { "agentName", profile.Nickname },
                { "expCurrent", expCurrent.ToString() },
                { "expNext", expNext.ToString() },
                { "expPct", expPct.ToString() },
                { "energyCur", energyCur.ToString() },
                { "energyMax", energyMax.ToString() },
                { "energyPct", energyPct.ToString() },
                { "mobacoin", profile.MobaCoinBalance.ToString() },
                { "silver", profile.SilverBalance.ToString() },
                { "leaderHtml", leaderHtml },
                { "attackDeckCount", attackDeckCount.ToString() },
                { "attackDeckCost", attackDeckCost.ToString() },
                { "attackPower", attackPower.ToString("N0") },
                { "defenseDeckCount", defenseDeckCount.ToString() },
                { "defenseDeckCost", defenseDeckCost.ToString() },
                { "defensePower", defensePower.ToString("N0") }
            };

            return Content(RenderTemplate("mypage.html", replacements), "text/html");
        }

        [HttpGet("mypage/deck")]
        public IActionResult ServeDeckManagerPage()
        {
            _logger.LogInformation("[Cygames] ServeDeckManagerPage called.");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            var profile = GetPlayerProfile(profileId);
            if (profile == null) return RedirectToAction("ServeGameTopPage");

            var cardsList = profile.Cards.Select(c => new
            {
                id = c.Id,
                title = c.CardTemplate?.Title ?? "Unknown Hero",
                alignment = c.CardTemplate?.Alignment ?? "Speed",
                rarity = c.CardTemplate?.Rarity ?? "Normal",
                level = c.CurrentLevel,
                atk = c.CurrentAtk,
                def = c.CurrentDef,
                cost = c.CardTemplate?.PowerRequirement ?? 5,
                in_atk = c.IsInAttackDeck,
                in_def = c.IsInDefenseDeck
            }).ToList();

            var replacements = new Dictionary<string, string>
            {
                { "attackCapacity", profile.AttackPower.ToString() },
                { "defenseCapacity", profile.DefensePower.ToString() },
                { "cardsJson", JsonSerializer.Serialize(cardsList) }
            };

            return Content(RenderTemplate("deck.html", replacements), "text/html");
        }

        [HttpGet("mypage/catalog")]
        public IActionResult ServeCardCatalogPage()
        {
            _logger.LogInformation("[Cygames] ServeCardCatalogPage called.");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            var profile = GetPlayerProfile(profileId);
            if (profile == null) return RedirectToAction("ServeGameTopPage");

            var cardsList = profile.Cards.Select(c => new
            {
                id = c.Id,
                title = c.CardTemplate?.Title ?? "Unknown Hero",
                visualTitle = c.CardTemplate?.VisualTitle ?? "Hero",
                variant = c.CardTemplate?.VariantName ?? "Base",
                alignment = c.CardTemplate?.Alignment ?? "Speed",
                rarity = c.CardTemplate?.Rarity ?? "Normal",
                level = c.CurrentLevel,
                atk = c.CurrentAtk,
                def = c.CurrentDef,
                cost = c.CardTemplate?.PowerRequirement ?? 5,
                abilityName = c.CardTemplate?.AbilityName ?? "None",
                abilityEffect = c.CardTemplate?.AbilityEffect ?? "No static effect.",
                quote = c.CardTemplate?.Quote ?? "",
                isLeader = c.IsLeader,
                inAtk = c.IsInAttackDeck,
                inDef = c.IsInDefenseDeck
            }).ToList();

            var replacements = new Dictionary<string, string>
            {
                { "cardsJson", JsonSerializer.Serialize(cardsList) }
            };

            return Content(RenderTemplate("catalog.html", replacements), "text/html");
        }

        [HttpGet("mypage/enhance")]
        public IActionResult ServeEnhancementForgePage()
        {
            _logger.LogInformation("[Cygames] ServeEnhancementForgePage called.");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            var profile = GetPlayerProfile(profileId, includeInventory: true);
            if (profile == null) return RedirectToAction("ServeGameTopPage");

            var serums = profile.InventoryItems
                .Where(pi => pi.ItemTemplate != null && pi.ItemTemplate.Name.Contains("ISO-8 Serum", StringComparison.OrdinalIgnoreCase))
                .Select(pi => new
                {
                    id = pi.ItemTemplateId,
                    name = pi.ItemTemplate?.Name ?? "Serum",
                    count = pi.Quantity,
                    exp_val = pi.ItemTemplateId == 36 ? 5000 : 1000
                }).ToList();

            var ownedCards = profile.Cards.Select(c => new
            {
                id = c.Id,
                title = c.CardTemplate?.Title ?? "Unknown Hero",
                variant = c.CardTemplate?.VariantName ?? "Base",
                alignment = c.CardTemplate?.Alignment ?? "Speed",
                rarity = c.CardTemplate?.Rarity ?? "Normal",
                level = c.CurrentLevel,
                maxLevel = c.CardTemplate?.Rarity switch
                {
                    "Normal" => 40,
                    "Rare" => 60,
                    "Super Rare" => 80,
                    "Legendary" => 99,
                    _ => 50
                },
                baseAtk = c.CardTemplate?.BaseAtk ?? 1000,
                baseDef = c.CardTemplate?.BaseDef ?? 1000,
                maxAtk = c.CardTemplate?.MaxAtk ?? 4000,
                maxDef = c.CardTemplate?.MaxDef ?? 4000,
                atk = c.CurrentAtk,
                def = c.CurrentDef,
                isLeader = c.IsLeader,
                inUse = c.IsInAttackDeck || c.IsInDefenseDeck
            }).ToList();

            var replacements = new Dictionary<string, string>
            {
                { "cardsJson", JsonSerializer.Serialize(ownedCards) },
                { "serumsJson", JsonSerializer.Serialize(serums) },
                { "silver", profile.SilverBalance.ToString() },
                { "silverFormatted", profile.SilverBalance.ToString("N0") }
            };

            return Content(RenderTemplate("enhance.html", replacements), "text/html");
        }

        [HttpPost("mypage/update_deck")]
        public IActionResult UpdateDeck()
        {
            _logger.LogInformation("[Cygames] UpdateDeck called.");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            var profile = GetPlayerProfile(profileId);
            if (profile == null) return BadRequest(new { success = false, message = "Profile not found." });

            string mode = Request.Form["mode"].ToString();
            string cardIdsStr = Request.Form["card_ids"].ToString();

            var cardIds = cardIdsStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                    .Select(id => int.TryParse(id, out var v) ? v : 0)
                                    .Where(id => id > 0)
                                    .ToList();

            if (cardIds.Count > 5)
            {
                return Ok(new { success = false, message = "Squad can have at most 5 cards." });
            }

            // Verify they belong to this profile
            var validCards = profile.Cards.Where(c => cardIds.Contains(c.Id)).ToList();
            if (validCards.Count != cardIds.Count)
            {
                return Ok(new { success = false, message = "One or more cards not found or unauthorized." });
            }

            // Verify cost capacity
            var totalCost = validCards.Sum(c => c.CardTemplate?.PowerRequirement ?? 0);
            var limit = mode == "attack" ? profile.AttackPower : profile.DefensePower;
            if (totalCost > limit)
            {
                return Ok(new { success = false, message = "Clearance power requirement exceeds deck capacity!" });
            }

            // Update
            foreach (var card in profile.Cards)
            {
                if (mode == "attack")
                {
                    card.IsInAttackDeck = cardIds.Contains(card.Id);
                }
                else
                {
                    card.IsInDefenseDeck = cardIds.Contains(card.Id);
                }
            }

            _dbContext.SaveChanges();

            return Ok(new { success = true, message = $"{mode.ToUpper()} squad configurations successfully synchronized!" });
        }

        [HttpPost("mypage/set_leader")]
        public IActionResult SetLeader()
        {
            _logger.LogInformation("[Cygames] SetLeader called.");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            var profile = GetPlayerProfile(profileId);
            if (profile == null) return BadRequest(new { success = false, message = "Profile not found." });

            int.TryParse(Request.Form["card_id"].ToString(), out var cardId);
            if (cardId <= 0) return Ok(new { success = false, message = "Missing card_id." });

            var targetCard = profile.Cards.FirstOrDefault(c => c.Id == cardId);
            if (targetCard == null)
            {
                return Ok(new { success = false, message = "Card not found or unauthorized." });
            }

            // Update leader status
            foreach (var card in profile.Cards)
            {
                card.IsLeader = (card.Id == cardId);
            }

            _dbContext.SaveChanges();

            return Ok(new { success = true, message = "S.H.I.E.L.D. representative leader successfully designated!" });
        }

        [HttpPost("mypage/enhance_card")]
        public IActionResult EnhanceCard()
        {
            _logger.LogInformation("[Cygames] EnhanceCard called.");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            var profile = GetPlayerProfile(profileId, includeInventory: true);
            if (profile == null) return BadRequest(new { success = false, message = "Profile not found." });

            int.TryParse(Request.Form["target_card_id"].ToString(), out var targetCardId);
            string materialType = Request.Form["material_type"].ToString();
            int.TryParse(Request.Form["material_id"].ToString(), out var materialId);

            if (targetCardId <= 0 || string.IsNullOrEmpty(materialType) || materialId <= 0)
            {
                return Ok(new { success = false, message = "Missing forge parameters." });
            }

            var targetCard = profile.Cards.FirstOrDefault(c => c.Id == targetCardId);
            if (targetCard == null)
            {
                return Ok(new { success = false, message = "Target card not found." });
            }

            int expGain = 0;
            PlayerInventoryItem? invItem = null;
            PlayerCard? materialCard = null;

            if (materialType == "serum")
            {
                invItem = profile.InventoryItems.FirstOrDefault(pi => pi.ItemTemplateId == materialId);
                if (invItem == null || invItem.Quantity <= 0)
                {
                    return Ok(new { success = false, message = "Insufficient Serum quantity in depot." });
                }
                expGain = materialId == 36 ? 5000 : 1000;
            }
            else if (materialType == "card")
            {
                materialCard = profile.Cards.FirstOrDefault(c => c.Id == materialId);
                if (materialCard == null)
                {
                    return Ok(new { success = false, message = "Material card not found." });
                }
                if (materialCard.IsLeader || materialCard.IsInAttackDeck || materialCard.IsInDefenseDeck)
                {
                    return Ok(new { success = false, message = "Cannot sacrifice active representative or squad member." });
                }
                expGain = materialCard.CurrentLevel * 200;
            }
            else
            {
                return Ok(new { success = false, message = "Unsupported material type." });
            }

            var levelsGained = Math.Max(1, expGain / 1000);
            var silverCost = levelsGained * 1500;

            if (profile.SilverBalance < silverCost)
            {
                return Ok(new { success = false, message = "Insufficient Silver budget for forge synthesis." });
            }

            // Apply card level changes
            var rarity = targetCard.CardTemplate?.Rarity ?? "Normal";
            var maxLevel = rarity switch
            {
                "Normal" => 40,
                "Rare" => 60,
                "Super Rare" => 80,
                "Legendary" => 99,
                _ => 50
            };

            if (targetCard.CurrentLevel >= maxLevel)
            {
                return Ok(new { success = false, message = "Target Hero is already at maximum clearance capacity!" });
            }

            var newLevel = Math.Min(maxLevel, targetCard.CurrentLevel + levelsGained);
            targetCard.CurrentLevel = newLevel;

            // Interpolate stats
            var baseAtk = targetCard.CardTemplate?.BaseAtk ?? 1000;
            var baseDef = targetCard.CardTemplate?.BaseDef ?? 1000;
            var maxAtk = targetCard.CardTemplate?.MaxAtk ?? 4000;
            var maxDef = targetCard.CardTemplate?.MaxDef ?? 4000;

            var progress = (double)(newLevel - 1) / (maxLevel - 1);
            targetCard.CurrentAtk = (int)Math.Round(baseAtk + (maxAtk - baseAtk) * progress);
            targetCard.CurrentDef = (int)Math.Round(baseDef + (maxDef - baseDef) * progress);

            // Deduct cost and consume material
            profile.SilverBalance -= silverCost;

            if (materialType == "serum" && invItem != null)
            {
                invItem.Quantity--;
            }
            else if (materialType == "card" && materialCard != null)
            {
                _dbContext.PlayerCards.Remove(materialCard);
            }

            _dbContext.SaveChanges();

            return Ok(new
            {
                success = true,
                message = $"Forge committed! {targetCard.CardTemplate?.Title} upgraded to level {newLevel}!",
                remaining_silver = profile.SilverBalance
            });
        }

        private PlayerProfile? GetPlayerProfile(int profileId, bool includeInventory = false)
        {
            var query = _dbContext.Profiles
                .Include(p => p.Cards)
                    .ThenInclude(c => c.CardTemplate);

            if (includeInventory)
            {
                return query
                    .Include(p => p.InventoryItems)
                        .ThenInclude(pi => pi.ItemTemplate)
                    .FirstOrDefault(p => p.Id == profileId);
            }
            return query.FirstOrDefault(p => p.Id == profileId);
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
    }
}
