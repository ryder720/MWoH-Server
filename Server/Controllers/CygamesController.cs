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
        private readonly IGachaSummoner _gachaSummoner;
        private readonly ICardGrowthEngine _cardGrowthEngine;
        private readonly IMissionEngine _missionEngine;
        private readonly IItemLedger _itemLedger;
        private readonly ISessionGateway _sessionGateway;
        private readonly IDeckManager _deckManager;
        private readonly ILeaderManager _leaderManager;

        public CygamesController(
            ILogger<CygamesController> logger, 
            IAuthService authService, 
            MwohDbContext dbContext,
            IGachaSummoner gachaSummoner,
            ICardGrowthEngine cardGrowthEngine,
            IMissionEngine missionEngine,
            IItemLedger itemLedger,
            ISessionGateway sessionGateway,
            IDeckManager deckManager,
            ILeaderManager leaderManager)
        {
            _logger = logger;
            _authService = authService;
            _dbContext = dbContext;
            _gachaSummoner = gachaSummoner;
            _cardGrowthEngine = cardGrowthEngine;
            _missionEngine = missionEngine;
            _itemLedger = itemLedger;
            _sessionGateway = sessionGateway;
            _deckManager = deckManager;
            _leaderManager = leaderManager;
        }

        // 1. Temporary Credential Request (Cygames OAuth step 1)
        [HttpPost("restful_api_auth/request_temporary_credential")]
        public IActionResult RequestTemporaryCredential()
        {
            _logger.LogInformation("[Cygames] RequestTemporaryCredential called.");
            
            string tempToken = "temp_" + Guid.NewGuid().ToString("N");
            _logger.LogInformation($"[Cygames] Generated temporary token: {tempToken}");

            // Return JSON containing temporary oauth token as expected by Smali
            var response = new
            {
                oauth_token = tempToken,
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

            // 1. First attempt to find the user via dynamic temporary token mapping
            var user = _sessionGateway.ExchangeTemporaryToken(oauthToken);
            
            // 2. Fallback to direct active token database search (compatibility fallback)
            if (user == null)
            {
                user = _sessionGateway.ResolveContext(null, null, oauthToken);
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
        // 5b. Redirect Community Button to default system browser
        [HttpGet("community_redirect")]
        public IActionResult RedirectToCommunity()
        {
            _logger.LogInformation($"[Cygames] RedirectToCommunity: Redirecting to {GameplaySettings.CommunityUrl}");
            return Redirect(GameplaySettings.CommunityUrl);
        }

        // 5c. Serve customizable Gacha WebView
        [HttpGet("gacha")]
        public IActionResult ServeGacha()
        {
            _logger.LogInformation("[Cygames] ServeGacha called.");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;
            var profile = _dbContext.Profiles.FirstOrDefault(p => p.Id == profileId);

            var mobaCoins = profile?.MobaCoinBalance ?? 0;
            var silver = profile?.SilverBalance ?? 0;

            var configPath = Path.Combine(Directory.GetCurrentDirectory(), "Config", "gacha_config.json");
            var gachaConfigJson = "[]";
            if (System.IO.File.Exists(configPath))
            {
                gachaConfigJson = System.IO.File.ReadAllText(configPath);
            }

            var inventory = _itemLedger.GetInventory(profileId);
            var ticketsDict = inventory
                .Where(pi => pi.ItemTemplate != null && pi.ItemTemplate.Name.Contains("Ticket", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(pi => pi.ItemTemplateId, pi => pi.Quantity);
            var ticketsJson = JsonSerializer.Serialize(ticketsDict);

            var replacements = new Dictionary<string, string>
            {
                { "mobaCoins", mobaCoins.ToString() },
                { "silver", silver.ToString() },
                { "gachaConfigJson", gachaConfigJson },
                { "ticketsJson", ticketsJson }
            };

            return Content(RenderTemplate("gacha.html", replacements), "text/html");
        }

        // 5d. Handle Gacha pulls via AJAX
        [HttpPost("gacha/pull")]
        public IActionResult PullGacha()
        {
            _logger.LogInformation("[Cygames] PullGacha called.");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            // Parse parameters
            int.TryParse(Request.Form["pack_id"].ToString(), out var packId);
            if (packId == 0 && Request.Query.ContainsKey("pack_id"))
            {
                int.TryParse(Request.Query["pack_id"].ToString(), out packId);
            }

            string currencyType = Request.Form["currency_type"].ToString();
            if (string.IsNullOrEmpty(currencyType) && Request.Query.ContainsKey("currency_type"))
            {
                currencyType = Request.Query["currency_type"].ToString();
            }
            if (string.IsNullOrEmpty(currencyType)) currencyType = "Silver";

            int.TryParse(Request.Form["pull_count"].ToString(), out var pullCount);
            if (pullCount == 0 && Request.Query.ContainsKey("pull_count"))
            {
                int.TryParse(Request.Query["pull_count"].ToString(), out pullCount);
            }
            if (pullCount <= 0) pullCount = 1;

            // Delegate execution to the deep Gacha Summoning Engine
            var result = _gachaSummoner.Pull(profileId, packId, currencyType, pullCount);

            if (!result.Success)
            {
                return Ok(new { success = false, message = result.Message });
            }

            // Map output cards safely
            var responseCards = result.PulledCards.Select(c => new
            {
                id = c.Id,
                templateId = c.CardTemplateId,
                title = c.CardTemplate?.Title ?? "Unknown Hero",
                visualTitle = c.CardTemplate?.VisualTitle ?? "Hero",
                variant = c.CardTemplate?.VariantName ?? "Base",
                alignment = c.CardTemplate?.Alignment ?? "Speed",
                rarity = c.CardTemplate?.Rarity ?? "Normal",
                imageFile = c.CardTemplate?.ImageFileName ?? "",
                baseAtk = c.CardTemplate?.BaseAtk ?? 1000,
                baseDef = c.CardTemplate?.BaseDef ?? 1000,
                maxAtk = c.CardTemplate?.MaxAtk ?? 4000,
                maxDef = c.CardTemplate?.MaxDef ?? 4000,
                powerRequirement = c.CardTemplate?.PowerRequirement ?? 5
            }).ToList();

            var updatedInventory = _itemLedger.GetInventory(profileId);
            var updatedTicketsDict = updatedInventory
                .Where(pi => pi.ItemTemplate != null && pi.ItemTemplate.Name.Contains("Ticket", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(pi => pi.ItemTemplateId, pi => pi.Quantity);

            return Ok(new
            {
                success = true,
                newMobaCoins = result.NewMobaCoins,
                newSilver = result.NewSilver,
                pulledCards = responseCards,
                newTickets = updatedTicketsDict
            });
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
            var gauthToken = HttpContext.Items.TryGetValue("GAuthToken", out var tokenObj) ? tokenObj as string : null;
            return _sessionGateway.ResolveContext(null, Request.Cookies["sid"], gauthToken);
        }

        [HttpPost("item/get_item_list")]
        public IActionResult GetItemList()
        {
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;
            
            var inventory = _itemLedger.GetInventory(profileId);
                
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

            var result = _itemLedger.UseItem(profileId, itemId);
            if (!result.Success)
            {
                return Ok(new { success = false, message = result.Message });
            }
            
            return Ok(new
            {
                success = true,
                message = result.Message,
                item_id = itemId,
                remaining_quantity = result.RemainingQuantity,
                player_status = new
                {
                    level = result.Level,
                    energy_max = result.EnergyMax,
                    energy_current = result.EnergyCurrent,
                    silver = result.Silver,
                    mobacoin = result.MobaCoin
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
                        <div class="card-artwork-placeholder" style="background-image: url('/images/cards/{leader.CardTemplate?.ImageFileName}'); background-size: cover; background-position: center; width: 100%; height: 100%; border-radius: 6px;">
                            <span class="artwork-icon" style="opacity: 0.15;">{icon}</span>
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
            var expNext = GameplaySettings.BaseXpRequirement + (profile.Level - 1) * GameplaySettings.XpIncrementPerLevel;
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
                { "energyRecoveryInterval", GameplaySettings.EnergyRecoveryIntervalSeconds.ToString() },
                { "lastRecoveryTime", DateTime.SpecifyKind(profile.LastEnergyRecoveryTime, DateTimeKind.Utc).ToString("o") },
                { "mobacoin", profile.MobaCoinBalance.ToString() },
                { "silver", profile.SilverBalance.ToString() },
                { "leaderHtml", leaderHtml },
                { "attackDeckCount", attackDeckCount.ToString() },
                { "attackDeckCost", attackDeckCost.ToString() },
                { "attackPower", attackPower.ToString("N0") },
                { "attackCapacity", profile.AttackPower.ToString() },
                { "defenseDeckCount", defenseDeckCount.ToString() },
                { "defenseDeckCost", defenseDeckCost.ToString() },
                { "defensePower", defensePower.ToString("N0") },
                { "defenseCapacity", profile.DefensePower.ToString() },
                { "statPoints", profile.StatPoints.ToString() }
            };

            return Content(RenderTemplate("mypage.html", replacements), "text/html");
        }

        [HttpGet("mypage/deck")]
        [HttpGet("deck")]
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
                in_def = c.IsInDefenseDeck,
                imageFile = c.CardTemplate?.ImageFileName ?? ""
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
        [HttpGet("card_list")]
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
                inDef = c.IsInDefenseDeck,
                imageFile = c.CardTemplate?.ImageFileName ?? "",
                masteryCur = c.CurrentMastery,
                masteryMax = c.CardTemplate?.MaxMastery ?? 100,
                masteryBonusAtk = c.CardTemplate?.MasteryBonusAtk ?? 0,
                masteryBonusDef = c.CardTemplate?.MasteryBonusDef ?? 0,
                fusionBonusAtk = c.FusionBonusAtk,
                fusionBonusDef = c.FusionBonusDef
            }).ToList();

            var replacements = new Dictionary<string, string>
            {
                { "cardsJson", JsonSerializer.Serialize(cardsList) }
            };

            return Content(RenderTemplate("catalog.html", replacements), "text/html");
        }

        [HttpGet("card_str")]
        public IActionResult ServeCardStrRedirect()
        {
            _logger.LogInformation("[Cygames] Native card_str requested. Redirecting to ISO-8 Forge.");
            return RedirectToAction("ServeEnhancementForgePage");
        }

        [HttpGet("shop")]
        [HttpGet("trade_response/trade_list_advance")]
        [HttpGet("wish")]
        [HttpGet("friend")]
        [HttpGet("rareparts")]
        [HttpGet("search_users")]
        [HttpGet("results")]
        [HttpGet("archive")]
        [HttpGet("advise/index/top")]
        [HttpGet("nexus/portal")]
        public IActionResult ServeStubPortal()
        {
            _logger.LogInformation($"[Cygames] ServeStubPortal called for Path: {Request.Path}");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;
            var profile = GetPlayerProfile(profileId);

            var pathName = Request.Path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "system";
            var displayName = pathName.ToLower() switch
            {
                "shop" => "S.H.I.E.L.D. SUPPLY DEPOT & MARKET",
                "trade_list_advance" => "AGENCY TRADING CENTER",
                "wish" => "BLUEPRINT ASSET WISHLIST",
                "friend" => "S.H.I.E.L.D. ALLIES & CO-OP NETWORK",
                "rareparts" => "TECH PARTS SYNTHESIZER",
                "search_users" => "ACTIVE SQUAD ARCHIVES",
                "results" => "BATTLE RECORDS & DECLASSIFIED FILES",
                "archive" => "HERO CODEX & ENCYCLOPEDIA",
                "top" => "AGENT HANDBOOK & FAQ PROTOCOLS",
                "portal" => "GLOBAL NEXUS THREAT CORE",
                _ => "RESTRICTED SUB-LINK SECURE PORTAL"
            };

            var replacements = new Dictionary<string, string>
            {
                { "portalName", displayName },
                { "agentName", profile?.Nickname ?? user.Username },
                { "level", (profile?.Level ?? 1).ToString() }
            };

            return Content(RenderTemplate("stub_portal.html", replacements), "text/html");
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
                cardTemplateId = c.CardTemplateId,
                title = c.CardTemplate?.Title ?? "Unknown Hero",
                variant = c.CardTemplate?.VariantName ?? "Base",
                alignment = c.CardTemplate?.Alignment ?? "Speed",
                rarity = c.CardTemplate?.Rarity ?? "Normal",
                level = c.CurrentLevel,
                maxLevel = CardGrowthEngine.GetMaxLevelByRarity(c.CardTemplate?.Rarity ?? "Normal"),
                baseAtk = c.CardTemplate?.BaseAtk ?? 1000,
                baseDef = c.CardTemplate?.BaseDef ?? 1000,
                maxAtk = c.CardTemplate?.MaxAtk ?? 4000,
                maxDef = c.CardTemplate?.MaxDef ?? 4000,
                atk = c.CurrentAtk,
                def = c.CurrentDef,
                isLeader = c.IsLeader,
                inUse = c.IsInAttackDeck || c.IsInDefenseDeck,
                imageFile = c.CardTemplate?.ImageFileName ?? "",
                abilityLevel = string.IsNullOrEmpty(c.CardTemplate?.AbilityName) ? 0 : c.AbilityLevel,
                abilityName = c.CardTemplate?.AbilityName ?? "",
                abilityEffect = c.CardTemplate?.AbilityEffect ?? "",
                masteryCur = c.CurrentMastery,
                masteryMax = c.CardTemplate?.MaxMastery ?? 100,
                masteryBonusAtk = c.CardTemplate?.MasteryBonusAtk ?? 0,
                masteryBonusDef = c.CardTemplate?.MasteryBonusDef ?? 0,
                fusionBonusAtk = c.FusionBonusAtk,
                fusionBonusDef = c.FusionBonusDef
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

        [HttpGet("card_union")]
        public IActionResult ServeFusionPage()
        {
            _logger.LogInformation("[Cygames] ServeFusionPage called.");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            var profile = GetPlayerProfile(profileId);
            if (profile == null) return RedirectToAction("ServeGameTopPage");

            var ownedCards = profile.Cards.Select(c => new
            {
                id = c.Id,
                cardTemplateId = c.CardTemplateId,
                title = c.CardTemplate?.Title ?? "Unknown Hero",
                variant = c.CardTemplate?.VariantName ?? "Base",
                alignment = c.CardTemplate?.Alignment ?? "Speed",
                rarity = c.CardTemplate?.Rarity ?? "Normal",
                level = c.CurrentLevel,
                maxLevel = CardGrowthEngine.GetMaxLevelByRarity(c.CardTemplate?.Rarity ?? "Normal"),
                baseAtk = c.CardTemplate?.BaseAtk ?? 1000,
                baseDef = c.CardTemplate?.BaseDef ?? 1000,
                maxAtk = c.CardTemplate?.MaxAtk ?? 4000,
                maxDef = c.CardTemplate?.MaxDef ?? 4000,
                atk = c.CurrentAtk,
                def = c.CurrentDef,
                isLeader = c.IsLeader,
                inUse = c.IsInAttackDeck || c.IsInDefenseDeck,
                imageFile = c.CardTemplate?.ImageFileName ?? "",
                abilityLevel = string.IsNullOrEmpty(c.CardTemplate?.AbilityName) ? 0 : c.AbilityLevel,
                abilityName = c.CardTemplate?.AbilityName ?? "",
                abilityEffect = c.CardTemplate?.AbilityEffect ?? "",
                masteryCur = c.CurrentMastery,
                masteryMax = c.CardTemplate?.MaxMastery ?? 100,
                masteryBonusAtk = c.CardTemplate?.MasteryBonusAtk ?? 0,
                masteryBonusDef = c.CardTemplate?.MasteryBonusDef ?? 0,
                fusionBonusAtk = c.FusionBonusAtk,
                fusionBonusDef = c.FusionBonusDef
            }).ToList();

            var replacements = new Dictionary<string, string>
            {
                { "cardsJson", JsonSerializer.Serialize(ownedCards) },
                { "silver", profile.SilverBalance.ToString() },
                { "silverFormatted", profile.SilverBalance.ToString("N0") }
            };

            return Content(RenderTemplate("fuse.html", replacements), "text/html");
        }

        [HttpPost("mypage/fuse_cards")]
        public IActionResult FuseCards()
        {
            _logger.LogInformation("[Cygames] FuseCards called.");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            int.TryParse(Request.Form["base_card_id"].ToString(), out var baseCardId);
            int.TryParse(Request.Form["partner_card_id"].ToString(), out var partnerCardId);

            if (baseCardId <= 0 || partnerCardId <= 0 || baseCardId == partnerCardId)
            {
                return Ok(new { success = false, message = "Invalid card selection for fusion." });
            }

            // Delegate to the deep Card Growth & Fusion Engine
            var result = _cardGrowthEngine.Fuse(profileId, baseCardId, partnerCardId);

            if (!result.Success)
            {
                return Ok(new { success = false, message = result.Message });
            }

            return Ok(new
            {
                success = true,
                message = result.Message,
                remaining_silver = result.RemainingSilver
            });
        }

        [HttpPost("mypage/update_deck")]
        public IActionResult UpdateDeck()
        {
            _logger.LogInformation("[Cygames] UpdateDeck called.");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            string mode = Request.Form["mode"].ToString();
            string cardIdsStr = Request.Form["card_ids"].ToString();

            var cardIds = cardIdsStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                    .Select(id => int.TryParse(id, out var v) ? v : 0)
                                    .Where(id => id > 0)
                                    .ToList();

            var result = _deckManager.SyncDeck(profileId, mode, cardIds);
            if (!result.Success)
            {
                return Ok(new { success = false, message = result.Message });
            }

            return Ok(new { success = true, message = result.Message });
        }

        [HttpPost("mypage/allocate_points")]
        public IActionResult AllocateStatPoints()
        {
            _logger.LogInformation("[Cygames] AllocateStatPoints called.");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            var profile = GetPlayerProfile(profileId);
            if (profile == null)
            {
                return Ok(new { success = false, message = "Profile not synced." });
            }

            int.TryParse(Request.Form["energy"].ToString(), out var energyPoints);
            int.TryParse(Request.Form["attack"].ToString(), out var attackPoints);
            int.TryParse(Request.Form["defense"].ToString(), out var defensePoints);

            var totalAllocated = energyPoints + attackPoints + defensePoints;
            if (totalAllocated <= 0)
            {
                return Ok(new { success = false, message = "Allocated points must be greater than zero." });
            }

            if (totalAllocated > profile.StatPoints)
            {
                return Ok(new { success = false, message = "Allocated points exceed available unallocated S.H.I.E.L.D. points." });
            }

            // Deduct and apply stat increments
            profile.StatPoints -= totalAllocated;
            profile.EnergyMax += energyPoints;
            profile.EnergyCurrent += energyPoints;
            profile.AttackPower += attackPoints;
            profile.DefensePower += defensePoints;

            try
            {
                _dbContext.SaveChanges();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Cygames] Failed to save stat point allocations to SQLite.");
                return Ok(new { success = false, message = "Database write error occurred." });
            }

            return Ok(new
            {
                success = true,
                message = "S.H.I.E.L.D. Agent parameters successfully updated and synced!",
                remainingStatPoints = profile.StatPoints,
                newEnergyMax = profile.EnergyMax,
                newEnergyCurrent = profile.EnergyCurrent,
                newAttackCapacity = profile.AttackPower,
                newDefenseCapacity = profile.DefensePower
            });
        }

        [HttpPost("mypage/set_leader")]
        public IActionResult SetLeader()
        {
            _logger.LogInformation("[Cygames] SetLeader called.");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            int.TryParse(Request.Form["card_id"].ToString(), out var cardId);
            if (cardId <= 0) return Ok(new { success = false, message = "Missing card_id." });

            var result = _leaderManager.DesignateLeader(profileId, cardId);
            if (!result.Success)
            {
                return Ok(new { success = false, message = result.Message });
            }

            return Ok(new { success = true, message = result.Message });
        }

        [HttpPost("mypage/enhance_card")]
        public IActionResult EnhanceCard()
        {
            _logger.LogInformation("[Cygames] EnhanceCard called.");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            int.TryParse(Request.Form["target_card_id"].ToString(), out var targetCardId);
            string materialType = Request.Form["material_type"].ToString();
            string materialIdStr = Request.Form["material_id"].ToString();

            if (targetCardId <= 0 || string.IsNullOrEmpty(materialType) || string.IsNullOrEmpty(materialIdStr))
            {
                return Ok(new { success = false, message = "Missing forge parameters." });
            }

            var materialIds = materialIdStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                           .Select(id => int.TryParse(id, out var v) ? v : 0)
                                           .Where(id => id > 0)
                                           .ToList();

            if (materialIds.Count == 0)
            {
                return Ok(new { success = false, message = "No material selected." });
            }

            // Delegate to the deep Card Growth & Fusion Engine
            var result = _cardGrowthEngine.Enhance(profileId, targetCardId, materialType, materialIds);

            if (!result.Success)
            {
                return Ok(new { success = false, message = result.Message });
            }

            bool abilityLeveledUp = result.Message.Contains("Sync Ability");

            return Ok(new
            {
                success = true,
                message = result.Message,
                remaining_silver = result.RemainingSilver,
                ability_leveled_up = abilityLeveledUp,
                new_ability_level = result.TargetCard?.AbilityLevel ?? 1
            });
        }

        private MissionProgressState GetPlayerMissionProgress(PlayerProfile profile)
        {
            try
            {
                if (!string.IsNullOrEmpty(profile.MissionProgressJson))
                {
                    return JsonSerializer.Deserialize<MissionProgressState>(profile.MissionProgressJson) ?? new MissionProgressState();
                }
            }
            catch { }
            return new MissionProgressState();
        }

        private void SavePlayerMissionProgress(PlayerProfile profile, MissionProgressState state)
        {
            profile.MissionProgressJson = JsonSerializer.Serialize(state);
        }

        [HttpGet("mypage/missions")]
        [HttpGet("quest")]
        public IActionResult ServeMissionsHub()
        {
            _logger.LogInformation("[Cygames] ServeMissionsHub called.");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            var profile = GetPlayerProfile(profileId);
            if (profile == null) return RedirectToAction("ServeGameTopPage");

            var progressState = GetPlayerMissionProgress(profile);
            var operations = _missionEngine.GetOperations();

            var opsHtmlList = new List<string>();
            foreach (var op in operations)
            {
                bool isUnlocked = op.OperationId <= progressState.UnlockedOperationId;
                
                var neonColor = (op.OperationId % 3) switch
                {
                    0 => "#00f0ff", // Speed
                    1 => "#ef4444", // Bruiser
                    2 => "#f59e0b", // Tactics
                    _ => "#00f0ff"
                };
                var alignmentIcon = (op.OperationId % 3) switch
                {
                    0 => "⚡ SPEED",
                    1 => "🔥 BRUISER",
                    2 => "🧠 TACTICS",
                    _ => "⚡ SPEED"
                };

                if (isUnlocked)
                {
                    var missionListHtml = new List<string>();
                    foreach (var mission in op.Missions)
                    {
                        bool missionUnlocked = false;
                        if (op.OperationId < progressState.UnlockedOperationId)
                        {
                            missionUnlocked = true;
                        }
                        else if (op.OperationId == progressState.UnlockedOperationId)
                        {
                            var mSubIndex = 1;
                            var parts = mission.MissionCode.Split('-');
                            if (parts.Length > 1 && int.TryParse(parts[1], out int parsedIndex))
                            {
                                mSubIndex = parsedIndex;
                            }
                            missionUnlocked = mSubIndex <= progressState.UnlockedMissionId;
                        }

                        if (missionUnlocked)
                        {
                            bool isCompleted = progressState.CompletedMissions.ContainsKey(mission.MissionCode);
                            var statusLbl = isCompleted ? "<span class=\"mission-play-btn completed\">CLEARED</span>" : "<span class=\"mission-play-btn\">ENGAGE</span>";

                            // Build dynamic possible drops html roster
                            var dropsHtml = "";
                            if (mission.PossibleDrops != null && mission.PossibleDrops.Count > 0)
                            {
                                var badgeList = mission.PossibleDrops.Select(d => $"""
                                    <span class="drop-badge">{d.ToUpper()}</span>
                                """).ToList();
                                dropsHtml = $"""
                                <div class="drop-info-container">
                                    <div class="drop-title">// ENEMY DATA INTEL DROPS</div>
                                    <div class="drop-badges-list">
                                        {string.Join("", badgeList)}
                                    </div>
                                </div>
                                """;
                            }
                            else
                            {
                                dropsHtml = $"""
                                <div class="drop-info-container">
                                    <div class="drop-title" style="color: var(--text-muted);">// NO INTEL DROPS DETECTED</div>
                                </div>
                                """;
                            }

                            missionListHtml.Add($"""
                            <div class="mission-item">
                                <div class="mission-row" onclick="toggleMissionDropdown(this)">
                                    <span class="mission-code">{mission.MissionCode}</span>
                                    <span class="mission-name">{mission.Name}</span>
                                    <div class="mission-meta-right">
                                        {statusLbl}
                                        <span class="dropdown-chevron">▼</span>
                                    </div>
                                </div>
                                <div class="mission-dropdown-panel" style="display: none;">
                                    <div class="mission-details-grid">
                                        <div class="detail-stat">🔋 ENERGY: <span>{mission.EnergyCost}</span></div>
                                        <div class="detail-stat">⭐ REWARDS: <span>+{mission.XpReward} XP</span></div>
                                        <div class="detail-stat">🪙 SILVER: <span>{mission.SilverMin}-{mission.SilverMax}</span></div>
                                    </div>
                                    {dropsHtml}
                                    <a href="/ultimate/mypage/missions/play/{mission.MissionCode}" class="mission-start-btn">INITIALIZE BATTLE Sector</a>
                                </div>
                            </div>
                            """);
                        }
                        else
                        {
                            missionListHtml.Add($"""
                            <div class="mission-item locked">
                                <div class="mission-row" style="opacity: 0.4; cursor: not-allowed;">
                                    <span class="mission-code" style="color: #6b7280;">{mission.MissionCode}</span>
                                    <span class="mission-name" style="color: #6b7280;">CLASSIFIED SECTOR</span>
                                    <div class="mission-meta-right">
                                        <span class="mission-play-btn completed" style="color: #ef4444; border-color: rgba(239,68,68,0.2);">LOCKED</span>
                                    </div>
                                </div>
                            </div>
                            """);
                        }
                    }

                    var missionsBox = $"""
                    <div class="mission-selector">
                        {string.Join("\n", missionListHtml)}
                    </div>
                    """;

                    opsHtmlList.Add($"""
                    <div class="op-card" style="border-color: {neonColor};">
                        <div class="op-banner" style="background-image: url('/images/operations/operation_{op.OperationId}.jpg');"></div>
                        <div class="op-header">
                            <span class="op-title">{op.CleanName}</span>
                            <span class="op-id" style="color: {neonColor}; border-color: {neonColor};">{alignmentIcon} // CH-{op.OperationId}</span>
                        </div>
                        <div class="op-stats">
                            <div class="op-stat">ENERGY COST: <span style="color: {neonColor};">{op.EnergyCost}</span></div>
                            <div class="op-stat">SECTORS: <span>{op.Missions.Count}</span></div>
                            <div class="op-stat">TARGET VILLAIN: <span style="color: #ef4444;">{op.BossName.ToUpper()}</span></div>
                        </div>
                        {missionsBox}
                    </div>
                    """);
                }
                else
                {
                    opsHtmlList.Add($"""
                    <div class="op-card locked">
                        <div class="locked-overlay">
                            <span class="lock-icon">🔒</span>
                            <span class="lock-lbl">CLASSIFIED // LEVEL INSUFFICIENT</span>
                        </div>
                        <div class="op-banner" style="background-image: url('/images/operations/operation_{op.OperationId}.jpg');"></div>
                        <div class="op-header">
                            <span class="op-title">{op.CleanName}</span>
                            <span class="op-id">CLASSIFIED // CH-{op.OperationId}</span>
                        </div>
                        <div class="op-stats">
                            <div class="op-stat">ENERGY COST: <span>{op.EnergyCost}</span></div>
                            <div class="op-stat">SECTORS: <span>{op.Missions.Count}</span></div>
                            <div class="op-stat">TARGET VILLAIN: <span>CLASSIFIED</span></div>
                        </div>
                    </div>
                    """);
                }
            }

            var replacements = new Dictionary<string, string>
            {
                { "level", profile.Level.ToString() },
                { "agentName", profile.Nickname },
                { "energyCur", profile.EnergyCurrent.ToString() },
                { "energyMax", profile.EnergyMax.ToString() },
                { "energyPct", ((double)profile.EnergyCurrent / profile.EnergyMax * 100).ToString("N0") },
                { "operationsHtml", string.Join("\n", opsHtmlList) }
            };

            return Content(RenderTemplate("missions.html", replacements), "text/html");
        }

        [HttpGet("mypage/missions/play/{id}")]
        public IActionResult ServeMissionPlay(string id)
        {
            _logger.LogInformation($"[Cygames] ServeMissionPlay called for mission {id}.");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            var profile = GetPlayerProfile(profileId);
            if (profile == null) return RedirectToAction("ServeGameTopPage");

            var progressState = GetPlayerMissionProgress(profile);

            var (activeOp, activeMission) = _missionEngine.GetMissionBlueprint(id);

            if (activeOp == null || activeMission == null)
            {
                return RedirectToAction("ServeMissionsHub");
            }

            if (progressState.ActiveMissionId != id)
            {
                progressState.ActiveMissionId = id;
                progressState.ActiveMissionProgress = 0;
                SavePlayerMissionProgress(profile, progressState);
                _dbContext.SaveChanges();
            }

            var leaderCard = profile.Cards.FirstOrDefault(c => c.IsLeader);
            var leaderName = leaderCard?.CardTemplate?.Title ?? "Agent Operative";
            var leaderAtk = leaderCard?.CurrentAtk ?? 1000;

            var replacements = new Dictionary<string, string>
            {
                { "missionId", id },
                { "missionCode", id },
                { "opId", activeOp.OperationId.ToString() },
                { "energyCur", profile.EnergyCurrent.ToString() },
                { "energyMax", profile.EnergyMax.ToString() },
                { "energyPct", ((double)profile.EnergyCurrent / profile.EnergyMax * 100).ToString("N0") },
                { "energyCost", activeMission.EnergyCost.ToString() },
                { "progressPct", progressState.ActiveMissionProgress.ToString() },
                { "leaderName", leaderName },
                { "leaderAtk", leaderAtk.ToString() },
                { "bossName", activeOp.BossName },
                { "bossDisplay", progressState.ActiveMissionProgress >= 100 ? "flex" : "none" }
            };

            return Content(RenderTemplate("mission_play.html", replacements), "text/html");
        }

        [HttpPost("mypage/missions/attack/{id}")]
        public IActionResult ProcessMissionAttack(string id)
        {
            _logger.LogInformation($"[Cygames] ProcessMissionAttack for mission {id}.");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            var result = _missionEngine.Attack(profileId, id);
            if (!result.Success)
            {
                if (result.Message.Contains("DEPLETED"))
                {
                    return Ok(new { success = false, message = result.Message });
                }
                return BadRequest(new { success = false, message = result.Message });
            }

            return Ok(new
            {
                success = true,
                energyCurrent = result.EnergyCurrent,
                energyMax = result.EnergyMax,
                energyPct = result.EnergyPct,
                progressPct = result.ProgressPct,
                cardDropped = result.CardDropped,
                droppedCardName = result.DroppedCardName,
                logLines = result.LogLines
            });
        }

        [HttpPost("mypage/missions/engage-boss/{id}")]
        public IActionResult ProcessBossBattle(string id)
        {
            _logger.LogInformation($"[Cygames] ProcessBossBattle for mission {id}.");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            var result = _missionEngine.EngageBoss(profileId, id);
            if (!result.Success)
            {
                return BadRequest(new { success = false, message = result.Message });
            }

            return Ok(new
            {
                success = true,
                droppedCardName = result.DroppedCardName,
                message = result.Message
            });
        }

        private PlayerProfile? GetPlayerProfile(int profileId, bool includeInventory = false)
        {
            var query = _dbContext.Profiles
                .Include(p => p.Cards)
                    .ThenInclude(c => c.CardTemplate);

            PlayerProfile? profile;
            if (includeInventory)
            {
                profile = query
                    .Include(p => p.InventoryItems)
                        .ThenInclude(pi => pi.ItemTemplate)
                    .FirstOrDefault(p => p.Id == profileId);
            }
            else
            {
                profile = query.FirstOrDefault(p => p.Id == profileId);
            }

            if (profile != null)
            {
                _missionEngine.RestoreEnergy(profile);
            }

            return profile;
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
