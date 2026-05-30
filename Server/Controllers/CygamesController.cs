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
        private readonly IProfileManager _profileManager;
        private readonly IBattleEngine _battleEngine;
        private readonly IAllianceEngine _allianceEngine;
        private readonly ITradeEngine _tradeEngine;
        private readonly IAssignmentEngine _assignmentEngine;
        private readonly ILoginCommendationEngine _loginCommendationEngine;
        private readonly IShieldTeamEngine _shieldTeamEngine;
        private readonly IResourceVaultEngine _resourceVaultEngine;

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
            IProfileManager profileManager,
            IBattleEngine battleEngine,
            IAllianceEngine allianceEngine,
            ITradeEngine tradeEngine,
            IAssignmentEngine assignmentEngine,
            ILoginCommendationEngine loginCommendationEngine,
            IShieldTeamEngine shieldTeamEngine,
            IResourceVaultEngine resourceVaultEngine)
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
            _profileManager = profileManager;
            _battleEngine = battleEngine;
            _allianceEngine = allianceEngine;
            _tradeEngine = tradeEngine;
            _assignmentEngine = assignmentEngine;
            _loginCommendationEngine = loginCommendationEngine;
            _shieldTeamEngine = shieldTeamEngine;
            _resourceVaultEngine = resourceVaultEngine;
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
            var rallyPoints = profile?.RallyPoints ?? 0;

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
                { "rallyPoints", rallyPoints.ToString() },
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
                newRallyPoints = result.NewRallyPoints,
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
                image_url = $"http://10.0.2.2:5000/images/items/{pi.ItemTemplate?.ImageFileName}"
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
            int targetCardId = 0;
            if (Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync();
                int.TryParse(form["item_id"].ToString(), out itemId);
                int.TryParse(form["target_card_id"].ToString(), out targetCardId);
            }
            
            if (itemId == 0 && Request.Query.TryGetValue("item_id", out var qVal))
            {
                int.TryParse(qVal.ToString(), out itemId);
            }
            if (targetCardId == 0 && Request.Query.TryGetValue("target_card_id", out var qCardVal))
            {
                int.TryParse(qCardVal.ToString(), out targetCardId);
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
                        if (doc.RootElement.TryGetProperty("target_card_id", out var cardProp))
                        {
                            targetCardId = cardProp.GetInt32();
                        }
                    }
                }
                catch {}
            }
            
            if (itemId == 0)
            {
                return BadRequest(new { success = false, message = "Missing item_id parameter." });
            }

            var result = _itemLedger.UseItem(profileId, itemId, targetCardId);
            if (!result.Success)
            {
                return Ok(new { success = false, message = result.Message });
            }
            
            var updatedProfile = GetPlayerProfile(profileId);
            int updatedCardsCount = updatedProfile?.Cards.Count ?? 0;
            int updatedMaxCapacity = updatedProfile?.MaxCardCapacity ?? 250;

            return Ok(new
            {
                success = true,
                message = result.Message,
                item_id = itemId,
                remaining_quantity = result.RemainingQuantity,
                updatedCard = result.UpdatedCard,
                player_status = new
                {
                    level = result.Level,
                    energy_max = result.EnergyMax,
                    energy_current = result.EnergyCurrent,
                    attack_power_current = result.AttackPowerCurrent,
                    attack_power_max = result.AttackPowerMax,
                    defense_power_current = result.DefensePowerCurrent,
                    defense_power_max = result.DefensePowerMax,
                    silver = result.Silver,
                    mobacoin = result.MobaCoin,
                    max_card_capacity = updatedMaxCapacity,
                    cards_count = updatedCardsCount
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
                        "LevelUpSerum" => "🧪",
                        "MasteryIso8" => "🧪",
                        "GachaTicket" => "🎟️",
                        _ => "📦"
                    };

                    var color = temp.Type switch
                    {
                        "EnergyRestorative" => "#00f0ff",
                        "AttackPowerRestorative" => "#ef4444",
                        "DefensePowerRestorative" => "#10b981",
                        "LevelUpSerum" => "#a855f7",
                        "MasteryIso8" => "#a5b4fc",
                        "GachaTicket" => "#f43f5e",
                        _ => "#f59e0b"
                    };

                    var useButton = (temp.Type.EndsWith("Restorative") || temp.Type == "LevelUpSerum" || temp.Type == "MasteryIso8" || temp.Type == "InventoryExpansion" || temp.Type == "GachaTicket") 
                        ? $"<button class='use-btn' data-type='{temp.Type}' onclick='useItem({temp.Id}, this)'>USE</button>"
                        : "<span class='passive-badge'>STOCK</span>";

                    itemsHtml += $"""
            <div class="item-row" id="item-row-{temp.Id}">
                <div class="item-visual" style="border-color: {color};">
                    <img src="/images/items/{temp.ImageFileName}" class="item-img" onerror="this.style.display='none'; document.getElementById('ph_{temp.Id}').style.display='flex';">
                    <div class="item-ph" id="ph_{temp.Id}" style="display:none; color: {color};">{icon}</div>
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

            var cardsList = profile?.Cards.Select(c => new
            {
                id = c.Id,
                title = c.CardTemplate?.Title ?? "Unknown Hero",
                visualTitle = c.CardTemplate?.VisualTitle ?? "Hero",
                variant = c.CardTemplate?.VariantName ?? "Base",
                alignment = c.CardTemplate?.Alignment ?? "Speed",
                rarity = c.CardTemplate?.Rarity ?? "Normal",
                level = c.CurrentLevel,
                levelMax = c.GetMaxLevel(),
                atk = c.CurrentAtk,
                def = c.CurrentDef,
                masteryCur = c.CurrentMastery,
                masteryMax = c.CardTemplate?.MaxMastery ?? 100,
                imageFile = c.CardTemplate?.ImageFileName ?? ""
            }).ToList() ?? new();
            var cardsJson = JsonSerializer.Serialize(cardsList);

            var energyPct = energyMax > 0 ? (energyCur * 100) / energyMax : 0;
            
            var cardsCount = cardsList.Count;
            var cardsMax = profile?.MaxCardCapacity ?? 250;
            var capacityPct = cardsMax > 0 ? (cardsCount * 100) / cardsMax : 0;

            var attackPowerCur = profile?.AttackPowerCurrent ?? 0;
            var attackPowerMax = profile?.AttackPower ?? 100;
            var attackPowerPct = attackPowerMax > 0 ? (attackPowerCur * 100) / attackPowerMax : 0;

            var defensePowerCur = profile?.DefensePowerCurrent ?? 0;
            var defensePowerMax = profile?.DefensePower ?? 100;
            var defensePowerPct = defensePowerMax > 0 ? (defensePowerCur * 100) / defensePowerMax : 0;

            var replacements = new Dictionary<string, string>
            {
                { "energyCur", energyCur.ToString() },
                { "energyMax", energyMax.ToString() },
                { "energyPct", energyPct.ToString() },
                { "attackPowerCur", attackPowerCur.ToString() },
                { "attackPowerMax", attackPowerMax.ToString() },
                { "attackPowerPct", attackPowerPct.ToString() },
                { "defensePowerCur", defensePowerCur.ToString() },
                { "defensePowerMax", defensePowerMax.ToString() },
                { "defensePowerPct", defensePowerPct.ToString() },
                { "itemsHtml", itemsHtml },
                { "cardsJson", cardsJson },
                { "cardsCount", cardsCount.ToString() },
                { "cardsMax", cardsMax.ToString() },
                { "capacityPct", capacityPct.ToString() }
            };

            return Content(RenderTemplate("item.html", replacements), "text/html");
        }

        [HttpGet("resource")]
        [HttpGet("resource/index")]
        [HttpGet("rareparts")]
        public IActionResult ServeResourceVaultPage()
        {
            _logger.LogInformation("[Cygames] ServeResourceVaultPage called.");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            var profile = GetPlayerProfile(profileId, includeInventory: true);
            if (profile == null) return RedirectToAction("ServeGameTopPage");

            var silver = profile.SilverBalance;
            var agentName = profile.Nickname;
            var level = profile.Level;

            var resourceList = profile.InventoryItems
                .Where(pi => pi.ItemTemplate != null && pi.ItemTemplate.Type == "Resource")
                .Select(pi => {
                    var name = pi.ItemTemplate?.Name ?? string.Empty;
                    string groupKey = "Unknown";
                    if (name.Contains("Cape")) groupKey = "StormsCape";
                    else if (name.Contains("Suitcase")) groupKey = "Suitcase";
                    else if (name.Contains("Sword")) groupKey = "SwordOfProficiency";
                    else if (name.Contains("Choker")) groupKey = "AssassinsChoker";
                    else if (name.Contains("Belt")) groupKey = "ChainBelt";
                    else if (name.Contains("Geirr")) groupKey = "Geirr";
                    else if (name.Contains("Array")) groupKey = "ProjectileArray";

                    string color = "Red";
                    string[] colors = { "Red", "Blue", "Green", "Yellow", "Purple", "Emerald", "Cyan", "Crimson", "Cobalt", "Amber", "Violet", "Aqua" };
                    foreach (var c in colors)
                    {
                        if (name.Contains(c))
                        {
                            color = c;
                            break;
                        }
                    }

                    return new {
                        id = pi.ItemTemplate?.Id ?? 0,
                        name = name,
                        qty = pi.Quantity,
                        donation = pi.ItemTemplate?.EffectValue ?? 2000,
                        imageFile = pi.ItemTemplate?.ImageFileName ?? "",
                        groupKey = groupKey,
                        color = color
                    };
                }).ToList();

            var resourcesJson = JsonSerializer.Serialize(resourceList);

            var redemptionsDict = new Dictionary<string, int>();
            if (!string.IsNullOrEmpty(profile.ResourceRedemptionsJson))
            {
                try { redemptionsDict = JsonSerializer.Deserialize<Dictionary<string, int>>(profile.ResourceRedemptionsJson) ?? new(); } catch {}
            }
            var redemptionsJson = JsonSerializer.Serialize(redemptionsDict);

            var replacements = new Dictionary<string, string>
            {
                { "silver", silver.ToString("N0") },
                { "agentName", agentName },
                { "level", level.ToString() },
                { "resourcesJson", resourcesJson },
                { "redemptionsJson", redemptionsJson }
            };

            return Content(RenderTemplate("resource.html", replacements), "text/html");
        }

        [HttpPost("resource/redeem")]
        public IActionResult RedeemResourceSet()
        {
            _logger.LogInformation("[Cygames] RedeemResourceSet called.");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            var groupKey = Request.Form["group_key"].ToString();
            if (string.IsNullOrEmpty(groupKey))
            {
                return BadRequest(new { success = false, message = "Group key missing." });
            }

            var result = _resourceVaultEngine.Redeem(profileId, groupKey);
            if (!result.Success)
            {
                if (result.Message.Contains("missing") || result.Message.Contains("Invalid"))
                {
                    return BadRequest(new { success = false, message = result.Message });
                }
                return Ok(new { success = false, message = result.Message });
            }

            var mappedResources = result.UpdatedResources.Select(ur => new
            {
                id = ur.Id,
                qty = ur.Qty
            }).ToList();

            return Ok(new
            {
                success = true,
                message = result.Message,
                redemptions = result.Redemptions,
                resources = mappedResources
            });
        }

        [HttpPost("resource/donate")]
        public IActionResult DonateResources()
        {
            _logger.LogInformation("[Cygames] DonateResources called.");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            var groupKey = Request.Form["group_key"].ToString();
            if (string.IsNullOrEmpty(groupKey))
            {
                return BadRequest(new { success = false, message = "Group key missing." });
            }

            var result = _resourceVaultEngine.Donate(profileId, groupKey);
            if (!result.Success)
            {
                if (result.Message.Contains("missing") || result.Message.Contains("mismatch"))
                {
                    return BadRequest(new { success = false, message = result.Message });
                }
                return Ok(new { success = false, message = result.Message });
            }

            var mappedResources = result.UpdatedResources.Select(ur => new
            {
                id = ur.Id,
                qty = ur.Qty
            }).ToList();

            return Ok(new
            {
                success = true,
                message = result.Message,
                silverBalance = result.SilverBalance,
                resources = mappedResources
            });
        }

        [HttpGet("mypage")]
        public IActionResult ServeMyPage()
        {
            _logger.LogInformation("[Cygames] ServeMyPage called.");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            var profile = GetPlayerProfile(profileId);
            if (profile == null) return RedirectToAction("ServeGameTopPage");

            var loginPopupScript = "";
            try
            {
                var loginResult = _loginCommendationEngine.ProcessDailyLogin(profileId);
                if (loginResult.UnlockedReward)
                {
                    profile.SilverBalance = loginResult.SilverBalance;
                    profile.RallyPoints = loginResult.RallyPoints;
                    profile.MobaCoinBalance = loginResult.MobaCoinBalance;

                    var formattedMessage = loginResult.Message.Replace("\n", "<br>");
                    loginPopupScript = $$"""
                    <!-- Tech Login Commendation Modal -->
                    <div id="login-modal-overlay" style="position: fixed; top: 0; left: 0; width: 100%; height: 100%; background: rgba(3, 7, 18, 0.85); backdrop-filter: blur(5px); display: flex; align-items: center; justify-content: center; z-index: 999999; padding: 20px; box-sizing: border-box; font-family: 'Outfit', sans-serif;">
                        <div style="background: rgba(13, 20, 35, 0.95); border: 1px solid #00f0ff; border-radius: 8px; width: 100%; max-width: 400px; box-shadow: 0 10px 25px rgba(0, 240, 255, 0.35); overflow: hidden; animation: zoomIn 0.3s cubic-bezier(0.16, 1, 0.3, 1) forwards;">
                            <div style="background: rgba(0, 240, 255, 0.05); border-bottom: 1px solid rgba(0, 240, 255, 0.15); padding: 12px; display: flex; align-items: center; gap: 10px;">
                                <span style="font-size: 16px; color: #00f0ff; filter: drop-shadow(0 0 6px rgba(0, 240, 255, 0.4)); animation: pulse 1.5s infinite;">📡</span>
                                <span style="font-family: 'Space Mono', monospace; font-size: 10px; font-weight: 700; color: #00f0ff; letter-spacing: 1px;">// S.H.I.E.L.D. SECURE LOGIN STAMP</span>
                            </div>
                            <div style="padding: 20px; font-size: 13px; color: #e2e8f0; line-height: 1.5; text-align: center;">
                                <div style="font-size: 28px; margin-bottom: 12px; filter: drop-shadow(0 0 8px rgba(0, 240, 255, 0.3));">📅</div>
                                <p style="margin: 0; font-family: 'Space Mono', monospace; font-size: 11px; color: #94a3b8; text-transform: uppercase;">-- Daily Commendations Dossier --</p>
                                <p style="margin: 10px 0 0 0; text-align: left; background: rgba(0, 240, 255, 0.02); border: 1px dashed rgba(0, 240, 255, 0.12); border-radius: 4px; padding: 10px;">{{formattedMessage}}</p>
                            </div>
                            <div style="padding: 12px; border-top: 1px solid rgba(255, 255, 255, 0.04); display: flex; justify-content: center;">
                                <button onclick="document.getElementById('login-modal-overlay').remove()" style="background: rgba(0, 240, 255, 0.1); border: 1px solid #00f0ff; color: #00f0ff; font-family: 'Space Mono', monospace; font-size: 10px; font-weight: 700; padding: 8px 16px; border-radius: 4px; cursor: pointer; letter-spacing: 1px; transition: all 0.2s ease;">DISMISS DOSSIER</button>
                            </div>
                        </div>
                    </div>
                    <style>
                        @keyframes zoomIn {
                            from { opacity: 0; transform: scale(0.9); }
                            to { opacity: 1; transform: scale(1); }
                        }
                        @keyframes pulse {
                            0% { opacity: 0.7; }
                            50% { opacity: 1; }
                            100% { opacity: 0.7; }
                        }
                    </style>
                    """ ;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ServeMyPage] Daily login commendation failed: {ex.Message}");
            }

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

            var attackPowerCur = profile.AttackPowerCurrent;
            var attackPowerMax = profile.AttackPower;
            var attackPowerPct = attackPowerMax > 0 ? Math.Min(100, (attackPowerCur * 100) / attackPowerMax) : 0;

            var defensePowerCur = profile.DefensePowerCurrent;
            var defensePowerMax = profile.DefensePower;
            var defensePowerPct = defensePowerMax > 0 ? Math.Min(100, (defensePowerCur * 100) / defensePowerMax) : 0;

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
                { "attackPowerCur", attackPowerCur.ToString() },
                { "attackPowerMax", attackPowerMax.ToString() },
                { "attackPowerPct", attackPowerPct.ToString() },
                { "defensePowerCur", defensePowerCur.ToString() },
                { "defensePowerMax", defensePowerMax.ToString() },
                { "defensePowerPct", defensePowerPct.ToString() },
                { "attackRecoveryInterval", GameplaySettings.AttackRecoveryIntervalSeconds.ToString() },
                { "defenseRecoveryInterval", GameplaySettings.DefenseRecoveryIntervalSeconds.ToString() },
                { "lastBattleRecoveryTime", DateTime.SpecifyKind(profile.LastBattlePowerRecoveryTime, DateTimeKind.Utc).ToString("o") },
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
                { "statPoints", profile.StatPoints.ToString() },
                { "loginPopupScript", loginPopupScript }
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

        [HttpGet("mypage/card/{id}")]
        public IActionResult ServeCardDetailsPage(int id)
        {
            _logger.LogInformation($"[Cygames] ServeCardDetailsPage called for Card ID: {id}");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            var card = _dbContext.PlayerCards
                .Include(c => c.CardTemplate)
                .FirstOrDefault(c => c.Id == id && c.PlayerProfileId == profileId);

            if (card == null)
            {
                return RedirectToAction("ServeCardCatalogPage");
            }

            var masteryCur = card.CurrentMastery;
            var masteryMax = card.CardTemplate?.MaxMastery ?? 100;
            var masteryBonusAtk = card.CardTemplate?.MasteryBonusAtk ?? 0;
            var masteryBonusDef = card.CardTemplate?.MasteryBonusDef ?? 0;

            var activeMasteryAtk = masteryMax > 0 ? (int)Math.Round((double)(masteryBonusAtk * masteryCur) / masteryMax) : 0;
            var activeMasteryDef = masteryMax > 0 ? (int)Math.Round((double)(masteryBonusDef * masteryCur) / masteryMax) : 0;

            var baseLevelAtk = card.CurrentAtk - activeMasteryAtk;
            var baseLevelDef = card.CurrentDef - activeMasteryDef;

            var masteryPct = masteryMax > 0 ? (int)Math.Round((double)(masteryCur * 100) / masteryMax) : 0;

            var replacements = new Dictionary<string, string>
            {
                { "id", card.Id.ToString() },
                { "title", card.CardTemplate?.Title ?? "Unknown Hero" },
                { "alignment", card.CardTemplate?.Alignment ?? "Speed" },
                { "alignmentUpper", (card.CardTemplate?.Alignment ?? "Speed").ToUpper() },
                { "rarity", card.CardTemplate?.Rarity ?? "Normal" },
                { "rarityUpper", (card.CardTemplate?.Rarity ?? "Normal").ToUpper() },
                { "level", card.CurrentLevel.ToString() },
                { "cost", (card.CardTemplate?.PowerRequirement ?? 5).ToString() },
                { "atk", card.CurrentAtk.ToString("N0") },
                { "atkBase", baseLevelAtk.ToString("N0") },
                { "atkMastery", activeMasteryAtk.ToString("N0") },
                { "def", card.CurrentDef.ToString("N0") },
                { "defBase", baseLevelDef.ToString("N0") },
                { "defMastery", activeMasteryDef.ToString("N0") },
                { "masteryCur", masteryCur.ToString() },
                { "masteryMax", masteryMax.ToString() },
                { "masteryPct", masteryPct.ToString() },
                { "abilityName", (card.CardTemplate?.AbilityName ?? "None").ToUpper() },
                { "abilityEffect", card.CardTemplate?.AbilityEffect ?? "No active sync ability." },
                { "quote", card.CardTemplate?.Quote ?? "With great power comes great responsibility!" },
                { "imageFile", card.CardTemplate?.ImageFileName ?? "" },
                { "isLeader", card.IsLeader ? "true" : "false" },
                { "leaderStatusText", card.IsLeader ? "ACTIVE REPRESENTATIVE" : "SET AS ACTIVE LEADER" },
                { "leaderDisabledAttr", card.IsLeader ? "disabled" : "" }
            };

            return Content(RenderTemplate("card_details.html", replacements), "text/html");
        }

        [HttpGet("card_str")]
        public IActionResult ServeCardStrRedirect()
        {
            _logger.LogInformation("[Cygames] Native card_str requested. Redirecting to ISO-8 Forge.");
            return RedirectToAction("ServeEnhancementForgePage");
        }

        [HttpGet("shop")]
        [HttpGet("wish")]
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

        [HttpGet("friend")]
        [HttpGet("friend/index")]
        public IActionResult ServeSHIELDTeamPage()
        {
            _logger.LogInformation("[Cygames] ServeSHIELDTeamPage called.");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            var profile = GetPlayerProfile(profileId);
            if (profile == null) return RedirectToAction("ServeGameTopPage");

            int maxCapacity = 5;
            if (profile.Level >= 10)
            {
                maxCapacity = 6 + (profile.Level - 10) / 2;
                if (maxCapacity > 50) maxCapacity = 50;
            }

            // 1. Fetch Accepted Team Members
            var teamRelations = _dbContext.ShieldTeamMembers
                .Where(m => (m.ProfileId == profile.Id || m.MemberProfileId == profile.Id) && m.Status == "Accepted")
                .ToList();

            var teamProfiles = new List<object>();
            foreach (var rel in teamRelations)
            {
                var otherId = rel.ProfileId == profile.Id ? rel.MemberProfileId : rel.ProfileId;
                var otherProfile = _dbContext.Profiles
                    .Include(p => p.Cards)
                        .ThenInclude(c => c.CardTemplate)
                    .FirstOrDefault(p => p.Id == otherId);

                if (otherProfile != null)
                {
                    var leaderCard = otherProfile.Cards.FirstOrDefault(c => c.IsLeader) ?? otherProfile.Cards.FirstOrDefault();
                    teamProfiles.Add(new
                    {
                        id = otherProfile.Id,
                        nickname = otherProfile.Nickname,
                        level = otherProfile.Level,
                        playerId = otherProfile.PlayerIdString,
                        leaderName = leaderCard?.CardTemplate?.Title ?? "Agent Recruit",
                        leaderImage = leaderCard?.CardTemplate?.VisualTitle ?? "Standard_Shield",
                        leaderAtk = leaderCard?.CurrentAtk ?? 0,
                        leaderDef = leaderCard?.CurrentDef ?? 0
                    });
                }
            }

            // 2. Fetch Incoming Pending Invites
            var receivedInvites = _dbContext.ShieldTeamMembers
                .Where(m => m.MemberProfileId == profile.Id && m.Status == "Pending")
                .ToList();

            var incomingProfiles = new List<object>();
            foreach (var rel in receivedInvites)
            {
                var senderProfile = _dbContext.Profiles
                    .Include(p => p.Cards)
                        .ThenInclude(c => c.CardTemplate)
                    .FirstOrDefault(p => p.Id == rel.ProfileId);

                if (senderProfile != null)
                {
                    var leaderCard = senderProfile.Cards.FirstOrDefault(c => c.IsLeader) ?? senderProfile.Cards.FirstOrDefault();
                    incomingProfiles.Add(new
                    {
                        id = senderProfile.Id,
                        nickname = senderProfile.Nickname,
                        level = senderProfile.Level,
                        playerId = senderProfile.PlayerIdString,
                        leaderName = leaderCard?.CardTemplate?.Title ?? "Agent Recruit",
                        leaderImage = leaderCard?.CardTemplate?.VisualTitle ?? "Standard_Shield"
                    });
                }
            }

            // 3. Fetch Outgoing Sent Invites
            var sentInvites = _dbContext.ShieldTeamMembers
                .Where(m => m.ProfileId == profile.Id && m.Status == "Pending")
                .ToList();

            var outgoingProfiles = new List<object>();
            foreach (var rel in sentInvites)
            {
                var receiverProfile = _dbContext.Profiles
                    .Include(p => p.Cards)
                        .ThenInclude(c => c.CardTemplate)
                    .FirstOrDefault(p => p.Id == rel.MemberProfileId);

                if (receiverProfile != null)
                {
                    var leaderCard = receiverProfile.Cards.FirstOrDefault(c => c.IsLeader) ?? receiverProfile.Cards.FirstOrDefault();
                    outgoingProfiles.Add(new
                    {
                        id = receiverProfile.Id,
                        nickname = receiverProfile.Nickname,
                        level = receiverProfile.Level,
                        playerId = receiverProfile.PlayerIdString,
                        leaderName = leaderCard?.CardTemplate?.Title ?? "Agent Recruit",
                        leaderImage = leaderCard?.CardTemplate?.VisualTitle ?? "Standard_Shield"
                    });
                }
            }

            var cutoff = DateTime.UtcNow.AddHours(-24);
            var recentlyRalliedIds = _dbContext.RallyLogs
                .Where(r => r.SenderProfileId == profile.Id && r.RalliedAt >= cutoff)
                .Select(r => r.ReceiverProfileId)
                .ToList();

            var replacements = new Dictionary<string, string>
            {
                { "agentName", profile.Nickname },
                { "level", profile.Level.ToString() },
                { "playerIdString", profile.PlayerIdString },
                { "teamCount", teamProfiles.Count.ToString() },
                { "maxCapacity", maxCapacity.ToString() },
                { "activeTeamJson", System.Text.Json.JsonSerializer.Serialize(teamProfiles) },
                { "receivedInvitesJson", System.Text.Json.JsonSerializer.Serialize(incomingProfiles) },
                { "sentInvitesJson", System.Text.Json.JsonSerializer.Serialize(outgoingProfiles) },
                { "ralliedIdsJson", System.Text.Json.JsonSerializer.Serialize(recentlyRalliedIds) },
                { "rallyPoints", profile.RallyPoints.ToString() }
            };

            return Content(RenderTemplate("friend.html", replacements), "text/html");
        }

        [HttpGet("friend/search")]
        public IActionResult SearchAgents([FromQuery] string query)
        {
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;
            var profile = GetPlayerProfile(profileId);
            if (profile == null) return BadRequest("Profile not found.");

            if (string.IsNullOrEmpty(query))
            {
                return Ok(new List<object>());
            }

            var searchResults = _dbContext.Profiles
                .Include(p => p.Cards)
                    .ThenInclude(c => c.CardTemplate)
                .Where(p => p.Id != profile.Id && (p.Nickname.Contains(query) || p.PlayerIdString == query))
                .Take(25)
                .ToList();

            var responseList = new List<object>();
            var cutoff = DateTime.UtcNow.AddHours(-24);

            foreach (var r in searchResults)
            {
                var leaderCard = r.Cards.FirstOrDefault(c => c.IsLeader) ?? r.Cards.FirstOrDefault();
                
                // Determine friendship status
                var relation = _dbContext.ShieldTeamMembers
                    .FirstOrDefault(m => (m.ProfileId == profile.Id && m.MemberProfileId == r.Id) || (m.ProfileId == r.Id && m.MemberProfileId == profile.Id));

                string status = "None";
                if (relation != null)
                {
                    if (relation.Status == "Accepted")
                    {
                        status = "Accepted";
                    }
                    else if (relation.Status == "Pending")
                    {
                        status = relation.ProfileId == profile.Id ? "PendingSent" : "PendingReceived";
                    }
                }

                var isRallied = _dbContext.RallyLogs
                    .Any(rl => rl.SenderProfileId == profile.Id && rl.ReceiverProfileId == r.Id && rl.RalliedAt >= cutoff);

                responseList.Add(new
                {
                    id = r.Id,
                    nickname = r.Nickname,
                    level = r.Level,
                    playerId = r.PlayerIdString,
                    leaderName = leaderCard?.CardTemplate?.Title ?? "Agent Recruit",
                    leaderImage = leaderCard?.CardTemplate?.VisualTitle ?? "Standard_Shield",
                    friendshipStatus = status,
                    isRallied = isRallied
                });
            }

            return Ok(responseList);
        }

        [HttpPost("friend/rally")]
        public IActionResult RallyAgent([FromForm] int targetId)
        {
            _logger.LogInformation($"[Cygames] RallyAgent called for targetId: {targetId}");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            var result = _shieldTeamEngine.Rally(profileId, targetId);
            if (!result.Success)
            {
                if (result.Message.Contains("not found"))
                {
                    return BadRequest(result.Message);
                }
                return Ok(new { success = false, message = result.Message });
            }

            return Ok(new
            {
                success = true,
                message = result.Message,
                senderPoints = result.SenderPointsGained,
                receiverPoints = result.ReceiverPointsGained,
                newRallyPoints = result.NewRallyPoints
            });
        }

        [HttpPost("friend/propose")]
        public IActionResult ProposeTeamMember([FromForm] int targetId)
        {
            _logger.LogInformation($"[Cygames] ProposeTeamMember targetId: {targetId}");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            var result = _shieldTeamEngine.Propose(profileId, targetId);
            return Ok(new { success = result.Success, message = result.Message });
        }

        [HttpPost("friend/accept")]
        public IActionResult AcceptTeamProposal([FromForm] int proposerId)
        {
            _logger.LogInformation($"[Cygames] AcceptTeamProposal proposerId: {proposerId}");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            var result = _shieldTeamEngine.Accept(profileId, proposerId);
            return Ok(new { success = result.Success, message = result.Message });
        }

        [HttpPost("friend/ignore")]
        public IActionResult IgnoreTeamProposal([FromForm] int proposerId)
        {
            _logger.LogInformation($"[Cygames] IgnoreTeamProposal proposerId: {proposerId}");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            var result = _shieldTeamEngine.Ignore(profileId, proposerId);
            return Ok(new { success = result.Success, message = result.Message });
        }

        [HttpPost("friend/remove")]
        public IActionResult RemoveTeamMember([FromForm] int memberId)
        {
            _logger.LogInformation($"[Cygames] RemoveTeamMember memberId: {memberId}");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            var result = _shieldTeamEngine.Remove(profileId, memberId);
            return Ok(new { success = result.Success, message = result.Message });
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

            int.TryParse(Request.Form["energy"].ToString(), out var energyPoints);
            int.TryParse(Request.Form["attack"].ToString(), out var attackPoints);
            int.TryParse(Request.Form["defense"].ToString(), out var defensePoints);

            var result = _profileManager.AllocateStatPoints(profileId, energyPoints, attackPoints, defensePoints);
            if (!result.Success)
            {
                return Ok(new { success = false, message = result.Message });
            }

            return Ok(new
            {
                success = true,
                message = result.Message,
                remainingStatPoints = result.RemainingStatPoints,
                newEnergyMax = result.NewEnergyMax,
                newEnergyCurrent = result.NewEnergyCurrent,
                newAttackCapacity = result.NewAttackCapacity,
                newDefenseCapacity = result.NewDefenseCapacity
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

            var result = _profileManager.DesignateLeader(profileId, cardId);
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

            // Fetch S.H.I.E.L.D. Team accepted members
            var teamRelations = _dbContext.ShieldTeamMembers
                .Where(m => (m.ProfileId == profile.Id || m.MemberProfileId == profile.Id) && m.Status == "Accepted")
                .ToList();

            var teamProfiles = new List<object>();
            foreach (var rel in teamRelations)
            {
                var otherId = rel.ProfileId == profile.Id ? rel.MemberProfileId : rel.ProfileId;
                var otherProfile = _dbContext.Profiles
                    .FirstOrDefault(p => p.Id == otherId);

                if (otherProfile != null)
                {
                    teamProfiles.Add(new
                    {
                        id = otherProfile.Id,
                        nickname = otherProfile.Nickname,
                        level = otherProfile.Level
                    });
                }
            }

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
                { "bossDisplay", progressState.ActiveMissionProgress >= 100 ? "flex" : "none" },
                { "activeTeamJson", System.Text.Json.JsonSerializer.Serialize(teamProfiles) }
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

        public class EngageBossRequest
        {
            public List<int>? SupportIds { get; set; }
        }

        [HttpPost("mypage/missions/engage-boss/{id}")]
        public IActionResult ProcessBossBattle(string id, [FromBody] EngageBossRequest? req)
        {
            _logger.LogInformation($"[Cygames] ProcessBossBattle for mission {id} with supports.");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            var supportIds = req?.SupportIds ?? new List<int>();

            var result = _missionEngine.EngageBoss(profileId, id, supportIds);
            if (!result.Success)
            {
                return BadRequest(new { success = false, message = result.Message });
            }

            return Ok(new
            {
                success = true,
                droppedCardName = result.DroppedCardName,
                resourceDropped = result.ResourceDropped,
                droppedResourceName = result.DroppedResourceName,
                droppedResourceImage = result.DroppedResourceImage,
                message = result.Message,
                logLines = result.LogLines
            });
        }

        [HttpGet("mypage/agent/{id}")]
        public IActionResult ServeAgentDossier(int id)
        {
            _logger.LogInformation($"[Cygames] ServeAgentDossier called for agent id: {id}");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            var currentPlayer = _dbContext.Profiles.FirstOrDefault(p => p.Id == profileId);
            if (currentPlayer == null) return RedirectToAction("ServeGameTopPage");

            var targetPlayer = _dbContext.Profiles
                .Include(p => p.Cards)
                    .ThenInclude(c => c.CardTemplate)
                .FirstOrDefault(p => p.Id == id);

            if (targetPlayer == null) return NotFound("Agent dossier not found in S.H.I.E.L.D. database.");

            // Self redirection if clicking own agent dossier
            if (targetPlayer.Id == currentPlayer.Id)
            {
                return Redirect("/ultimate/friend");
            }

            var leaderCard = targetPlayer.Cards.FirstOrDefault(c => c.IsLeader) ?? targetPlayer.Cards.FirstOrDefault();

            // Determine relationship status
            var relation = _dbContext.ShieldTeamMembers
                .FirstOrDefault(m => (m.ProfileId == currentPlayer.Id && m.MemberProfileId == targetPlayer.Id) || 
                                     (m.ProfileId == targetPlayer.Id && m.MemberProfileId == currentPlayer.Id));

            string status = "None";
            if (relation != null)
            {
                if (relation.Status == "Accepted")
                {
                    status = "Accepted";
                }
                else if (relation.Status == "Pending")
                {
                    status = relation.ProfileId == currentPlayer.Id ? "PendingSent" : "PendingReceived";
                }
            }

            // Check rally cooldown
            var cutoff = DateTime.UtcNow.AddHours(-24);
            var lastRally = _dbContext.RallyLogs
                .FirstOrDefault(rl => rl.SenderProfileId == currentPlayer.Id && rl.ReceiverProfileId == targetPlayer.Id && rl.RalliedAt >= cutoff);

            bool cooldownActive = lastRally != null;
            string cooldownText = "0";
            if (lastRally != null)
            {
                var remaining = lastRally.RalliedAt.AddHours(24) - DateTime.UtcNow;
                cooldownText = $"{(int)remaining.TotalHours}h {remaining.Minutes}m";
            }

            var replacements = new Dictionary<string, string>
            {
                { "agentName", targetPlayer.Nickname },
                { "level", targetPlayer.Level.ToString() },
                { "playerIdString", targetPlayer.PlayerIdString },
                { "leaderName", leaderCard?.CardTemplate?.Title ?? "Agent Recruit" },
                { "leaderImage", leaderCard?.CardTemplate?.VisualTitle ?? "Standard_Shield" },
                { "leaderRarity", leaderCard?.CardTemplate?.Rarity ?? "Normal" },
                { "leaderAlignment", leaderCard?.CardTemplate?.Alignment ?? "Speed" },
                { "leaderAtk", (leaderCard?.CurrentAtk ?? 0).ToString() },
                { "leaderDef", (leaderCard?.CurrentDef ?? 0).ToString() },
                { "relationshipStatus", status },
                { "rallyCooldownActive", cooldownActive ? "true" : "false" },
                { "cooldownText", cooldownText },
                { "agentId", targetPlayer.Id.ToString() },
                { "rallyPoints", currentPlayer.RallyPoints.ToString() },
                { "currentPlayerName", currentPlayer.Nickname }
            };

            return Content(RenderTemplate("agent_dossier.html", replacements), "text/html");
        }

        [HttpGet("search_users")]
        public IActionResult ServeSearchUsersPage()
        {
            _logger.LogInformation("[Cygames] ServeSearchUsersPage called.");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            var currentPlayer = GetPlayerProfile(profileId);
            if (currentPlayer == null) return RedirectToAction("ServeGameTopPage");

            var rivals = _dbContext.Profiles
                .Include(p => p.Cards)
                    .ThenInclude(c => c.CardTemplate)
                .Where(p => p.Id != currentPlayer.Id)
                .ToList();

            var rivalsJson = System.Text.Json.JsonSerializer.Serialize(rivals.Select(r => new
            {
                id = r.Id,
                nickname = r.Nickname,
                level = r.Level,
                silver = r.SilverBalance,
                leaderCard = r.Cards.FirstOrDefault(c => c.IsLeader)?.CardTemplate?.VisualTitle ?? "Standard Recruit",
                leaderImage = r.Cards.FirstOrDefault(c => c.IsLeader)?.CardTemplate?.ImageFileName ?? "default_leader.jpg",
                alignment = r.Cards.FirstOrDefault(c => c.IsLeader)?.CardTemplate?.Alignment ?? "Bruiser"
            }));

            var attackPct = Math.Min(100, (currentPlayer.AttackPowerCurrent * 100) / currentPlayer.AttackPower);
            var defensePct = Math.Min(100, (currentPlayer.DefensePowerCurrent * 100) / currentPlayer.DefensePower);

            var replacements = new Dictionary<string, string>
            {
                { "currentPlayerName", currentPlayer.Nickname },
                { "currentPlayerLevel", currentPlayer.Level.ToString() },
                { "currentPlayerSilver", currentPlayer.SilverBalance.ToString("N0") },
                { "currentPlayerAttackPower", $"{currentPlayer.AttackPowerCurrent}/{currentPlayer.AttackPower}" },
                { "currentPlayerAttackPowerCur", currentPlayer.AttackPowerCurrent.ToString() },
                { "currentPlayerAttackPowerMax", currentPlayer.AttackPower.ToString() },
                { "currentPlayerAttackPowerPct", attackPct.ToString() },
                { "currentPlayerDefensePower", $"{currentPlayer.DefensePowerCurrent}/{currentPlayer.DefensePower}" },
                { "currentPlayerDefensePowerCur", currentPlayer.DefensePowerCurrent.ToString() },
                { "currentPlayerDefensePowerMax", currentPlayer.DefensePower.ToString() },
                { "currentPlayerDefensePowerPct", defensePct.ToString() },
                { "rivalsJson", rivalsJson }
            };

            return Content(RenderTemplate("search_users.html", replacements), "text/html");
        }

        [HttpGet("results")]
        public IActionResult ServeResultsPage()
        {
            _logger.LogInformation("[Cygames] ServeResultsPage called.");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            var currentPlayer = GetPlayerProfile(profileId);
            if (currentPlayer == null) return RedirectToAction("ServeGameTopPage");

            var records = _dbContext.BattleRecords
                .Include(r => r.Attacker)
                .Include(r => r.Defender)
                .Where(r => r.AttackerProfileId == currentPlayer.Id || r.DefenderProfileId == currentPlayer.Id)
                .OrderByDescending(r => r.BattleTime)
                .Take(50)
                .ToList();

            var recordsJson = System.Text.Json.JsonSerializer.Serialize(records.Select(r => new
            {
                id = r.Id,
                attackerId = r.AttackerProfileId,
                attackerName = r.Attacker?.Nickname ?? "Unknown Agent",
                defenderId = r.DefenderProfileId,
                defenderName = r.Defender?.Nickname ?? "Unknown Agent",
                winnerId = r.WinnerProfileId,
                attackerPower = r.AttackerFinalPower,
                defenderPower = r.DefenderFinalPower,
                silver = r.SilverExchanged,
                mastery = r.MasteryEarned,
                time = r.BattleTime.ToString("yyyy-MM-dd HH:mm:ss"),
                isSparring = r.IsSparring,
                details = r.DetailsJson
            }));

            var replacements = new Dictionary<string, string>
            {
                { "currentPlayerId", currentPlayer.Id.ToString() },
                { "currentPlayerName", currentPlayer.Nickname },
                { "recordsJson", recordsJson }
            };

            return Content(RenderTemplate("results.html", replacements), "text/html");
        }

        [HttpGet("battle/fight/{defenderId}")]
        public IActionResult ServeFightPage(int defenderId)
        {
            _logger.LogInformation($"[Cygames] ServeFightPage called for defender: {defenderId}");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            var currentPlayer = GetPlayerProfile(profileId);
            if (currentPlayer == null) return RedirectToAction("ServeGameTopPage");

            var defender = _dbContext.Profiles
                .Include(p => p.Cards)
                    .ThenInclude(c => c.CardTemplate)
                .FirstOrDefault(p => p.Id == defenderId);

            if (defender == null) return NotFound("Rival S.H.I.E.L.D. Agent not found.");
            if (defender.Id == currentPlayer.Id) return Redirect("/ultimate/search_users");

            var attackerLeader = currentPlayer.Cards.FirstOrDefault(c => c.IsLeader) ?? currentPlayer.Cards.FirstOrDefault();
            var defenderLeader = defender.Cards.FirstOrDefault(c => c.IsLeader) ?? defender.Cards.FirstOrDefault();

            var attackPct = Math.Min(100, (currentPlayer.AttackPowerCurrent * 100) / currentPlayer.AttackPower);
            var defensePct = Math.Min(100, (currentPlayer.DefensePowerCurrent * 100) / currentPlayer.DefensePower);

            var replacements = new Dictionary<string, string>
            {
                { "currentPlayerId", currentPlayer.Id.ToString() },
                { "currentPlayerName", currentPlayer.Nickname },
                { "currentPlayerLevel", currentPlayer.Level.ToString() },
                { "currentPlayerAttackPower", currentPlayer.AttackPowerCurrent.ToString() },
                { "currentPlayerAttackPowerMax", currentPlayer.AttackPower.ToString() },
                { "currentPlayerAttackPowerPct", attackPct.ToString() },
                { "currentPlayerDefensePower", currentPlayer.DefensePowerCurrent.ToString() },
                { "currentPlayerDefensePowerMax", currentPlayer.DefensePower.ToString() },
                { "currentPlayerDefensePowerPct", defensePct.ToString() },
                { "defenderId", defender.Id.ToString() },
                { "defenderName", defender.Nickname },
                { "defenderLevel", defender.Level.ToString() },
                { "attackerLeaderName", attackerLeader?.CardTemplate?.VisualTitle ?? "Standard Recruit" },
                { "attackerLeaderImage", attackerLeader?.CardTemplate?.ImageFileName ?? "default_leader.jpg" },
                { "defenderLeaderName", defenderLeader?.CardTemplate?.VisualTitle ?? "Standard Recruit" },
                { "defenderLeaderImage", defenderLeader?.CardTemplate?.ImageFileName ?? "default_leader.jpg" }
            };

            return Content(RenderTemplate("battle.html", replacements), "text/html");
        }

        [HttpPost("battle/engage")]
        public IActionResult EngageBattle([FromBody] EngageBattleRequest req)
        {
            _logger.LogInformation($"[Cygames] EngageBattle API called: defenderId={req.DefenderId}, isSparring={req.IsSparring}");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            var result = _battleEngine.ResolveBattle(profileId, req.DefenderId, req.IsSparring);
            if (!result.Success)
            {
                return Ok(new { success = false, message = result.Message });
            }

            return Ok(new
            {
                success = true,
                attackerWon = result.AttackerWon,
                attackerFinalPower = result.AttackerFinalPower,
                defenderFinalPower = result.DefenderFinalPower,
                silverExchanged = result.SilverExchanged,
                masteryEarned = result.MasteryEarned,
                attackerAttackPowerBefore = result.AttackerAttackPowerBefore,
                attackerAttackPowerAfter = result.AttackerAttackPowerAfter,
                attackerAttackPowerMax = result.AttackerAttackPowerMax,
                defenderDefensePowerBefore = result.DefenderDefensePowerBefore,
                defenderDefensePowerAfter = result.DefenderDefensePowerAfter,
                defenderDefensePowerMax = result.DefenderDefensePowerMax,
                logLines = result.LogLines
            });
        }

        public class EngageBattleRequest
        {
            public int DefenderId { get; set; }
            public bool IsSparring { get; set; }
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
                _battleEngine.RestoreBattlePower(profile);
            }

            return profile;
        }

        // ==========================================
        // ALLIANCE CONTROLLER ENDPOINTS
        // ==========================================

        [HttpGet("alliance")]
        public IActionResult ServeAllianceHubPage()
        {
            _logger.LogInformation("[Cygames] ServeAllianceHubPage called.");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            // Load profile with inventory for resources list
            var profile = GetPlayerProfile(profileId, includeInventory: true);
            if (profile == null) return RedirectToAction("ServeGameTopPage");

            var agentName = profile.Nickname;
            var level = profile.Level;
            var silver = profile.SilverBalance;

            var replacements = new Dictionary<string, string>();

            if (profile.AllianceId == null)
            {
                // Count accepted allies
                int alliesCount = _dbContext.ShieldTeamMembers
                    .Count(m => (m.ProfileId == profileId || m.MemberProfileId == profileId) && m.Status == "Accepted");

                bool canForm = level >= 20 && alliesCount >= 10;
                string formStatus = canForm ? "SATISFIED" : "RESTRICTED";

                // Generate active alliances list
                var recommendationsHtml = "";
                var existingAlliances = _dbContext.Alliances.Include(a => a.Members).Take(5).ToList();
                if (existingAlliances.Any())
                {
                    foreach (var a in existingAlliances)
                    {
                        int maxCap = a.Level <= 1 ? 10 : a.Level <= 11 ? 10 + (a.Level - 1) : 20 + (int)Math.Floor((a.Level - 11) / 2.0);
                        if (a.Level >= 31 && a.Level <= 34) maxCap = 30;
                        else if (a.Level >= 35 && a.Level <= 54) maxCap = 31 + (int)Math.Floor((a.Level - 35) / 5.0);
                        else if (a.Level >= 55 && a.Level <= 109) maxCap = 35 + (int)Math.Floor((a.Level - 55) / 10.0);
                        else if (a.Level >= 110) maxCap = 40;

                        recommendationsHtml += $"""
                        <div class="alliance-recommendation-row">
                            <div class="rec-info">
                                <span class="rec-name">{a.Name}</span>
                                <span class="rec-desc">Lv. {a.Level} // Rating: {a.Rating} // "{a.Slogan}"</span>
                            </div>
                            <div style="text-align: right; display: flex; flex-direction: column; gap: 6px; align-items: flex-end;">
                                <span class="rec-cap">{a.Members.Count} / {maxCap} Members</span>
                                <button class="alliance-action-btn" onclick="sendJoinRequest({a.Id}, this)">Ask to Join</button>
                            </div>
                        </div>
                        """;
                    }
                }
                else
                {
                    recommendationsHtml = "<div class='no-records-card'>No active S.H.I.E.L.D. Divisions registered in tactical database. Form the first division!</div>";
                }

                replacements = new Dictionary<string, string>
                {
                    { "agentName", agentName },
                    { "level", level.ToString() },
                    { "silver", silver.ToString("N0") },
                    { "alliesCount", alliesCount.ToString() },
                    { "formRequirementStatus", formStatus },
                    { "allianceRecommendationsHtml", recommendationsHtml },
                    { "inAllianceDisplay", "none" },
                    { "noAllianceDisplay", "block" },
                    // Placeholders for template fields so rendering won't fail
                    { "allianceName", "" },
                    { "allianceSlogan", "" },
                    { "allianceLevel", "1" },
                    { "allianceRating", "0" },
                    { "donatedSilver", "0" },
                    { "protectionWalls", "0" },
                    { "speedAdaptor", "0" },
                    { "bruiserAdaptor", "0" },
                    { "tacticsAdaptor", "0" },
                    { "agentRole", "" },
                    { "personalDonated", "0" },
                    { "memberCount", "0/10" },
                    { "membersHtml", "" },
                    { "joinRequestsHtml", "" },
                    { "requestsDisplay", "none" },
                    { "roleActionsDisplay", "none" },
                    { "resourcesJson", "[]" }
                };
            }
            else
            {
                // In alliance: Load full Alliance Hub
                var alliance = _dbContext.Alliances
                    .Include(a => a.Members)
                    .FirstOrDefault(a => a.Id == profile.AllianceId);

                if (alliance == null) return RedirectToAction("ServeGameTopPage");

                int maxMembers = alliance.Level <= 1 ? 10 : alliance.Level <= 11 ? 10 + (alliance.Level - 1) : 20 + (int)Math.Floor((alliance.Level - 11) / 2.0);
                if (alliance.Level >= 31 && alliance.Level <= 34) maxMembers = 30;
                else if (alliance.Level >= 35 && alliance.Level <= 54) maxMembers = 31 + (int)Math.Floor((alliance.Level - 35) / 5.0);
                else if (alliance.Level >= 55 && alliance.Level <= 109) maxMembers = 35 + (int)Math.Floor((alliance.Level - 55) / 10.0);
                else if (alliance.Level >= 110) maxMembers = 40;

                bool isLeader = profile.AllianceRole == "Leader";
                bool isOfficer = isLeader || profile.AllianceRole == "Vice-Leader";

                // Generate members list
                var membersHtml = "";
                foreach (var m in alliance.Members.OrderByDescending(x => x.AllianceRole == "Leader")
                                                  .ThenByDescending(x => x.AllianceRole == "Vice-Leader")
                                                  .ThenByDescending(x => x.AllianceDonatedSilver))
                {
                    var joinedStr = m.AllianceJoinedAt?.ToString("yyyy-MM-dd HH:mm") ?? "N/A";
                    var actionSelectorHtml = "";

                    if (isLeader && m.Id != profileId)
                    {
                        actionSelectorHtml = $"""
                        <div class="member-actions" style="margin-top: 4px;">
                            <select onchange="changeMemberRole({m.Id}, this)" class="role-selector" style="font-family:'Space Mono',monospace; font-size:8px; background: rgba(0,0,0,0.5); color:#00f0ff; border:1px solid rgba(0,240,255,0.2); border-radius:4px; padding:2px 4px;">
                                <option value="Member" {(m.AllianceRole == "Member" ? "selected" : "")}>AGENT</option>
                                <option value="Vice-Leader" {(m.AllianceRole == "Vice-Leader" ? "selected" : "")}>VICE-LEADER</option>
                                <option value="Offense-Leader" {(m.AllianceRole == "Offense-Leader" ? "selected" : "")}>OFFENSE LEADER</option>
                                <option value="Defense-Leader" {(m.AllianceRole == "Defense-Leader" ? "selected" : "")}>DEFENSE LEADER</option>
                            </select>
                        </div>
                        """;
                    }

                    membersHtml += $"""
                    <div class="member-row">
                        <div class="member-info">
                            <span class="member-name">{m.Nickname} <span class="member-role-badge {m.AllianceRole?.ToLower()}">{m.AllianceRole?.ToUpper() ?? "AGENT"}</span></span>
                            <span class="member-desc">Clearance: Lv. {m.Level} // Joined: {joinedStr}</span>
                        </div>
                        <div style="text-align: right; display: flex; flex-direction: column; gap: 4px; align-items: flex-end;">
                            <span class="member-donation">🪙 {m.AllianceDonatedSilver.ToString("N0")} Contributed</span>
                            {actionSelectorHtml}
                        </div>
                    </div>
                    """;
                }

                // Generate join requests list for Leaders/Vice-Leaders
                var joinRequestsHtml = "";
                var requests = _dbContext.AllianceJoinRequests
                    .Include(r => r.PlayerProfile)
                    .Where(r => r.AllianceId == alliance.Id && r.Status == "Pending")
                    .ToList();

                if (requests.Any())
                {
                    foreach (var r in requests)
                    {
                        joinRequestsHtml += $"""
                        <div class="request-row" id="req-row-{r.Id}">
                            <div class="member-info">
                                <span class="member-name">{r.PlayerProfile?.Nickname ?? "Unknown Agent"}</span>
                                <span class="member-desc">Clearance Level: Lv. {r.PlayerProfile?.Level ?? 1}</span>
                            </div>
                            <div style="display: flex; gap: 8px;">
                                <button class="alliance-action-btn accept" onclick="respondRequest({r.Id}, true, this)">Accept</button>
                                <button class="alliance-action-btn decline" onclick="respondRequest({r.Id}, false, this)">Decline</button>
                            </div>
                        </div>
                        """;
                    }
                }
                else
                {
                    joinRequestsHtml = "<div class='no-requests-msg'>No pending access requests in mainframe.</div>";
                }

                // Filter out player's stock of Rare drops (Type == "Resource")
                var resourceList = profile.InventoryItems
                    .Where(pi => pi.ItemTemplate != null && pi.ItemTemplate.Type == "Resource")
                    .Select(pi => new {
                        id = pi.ItemTemplateId,
                        groupKey = pi.ItemTemplate!.Name.Contains("Storm's") && pi.ItemTemplate.Name.Contains("Cape") ? "StormsCape" :
                                   pi.ItemTemplate.Name.Contains("Suitcase") ? "Suitcase" :
                                   pi.ItemTemplate.Name.Contains("Sword") ? "SwordOfProficiency" :
                                   pi.ItemTemplate.Name.Contains("Assassin's") && pi.ItemTemplate.Name.Contains("Choker") ? "AssassinsChoker" :
                                   pi.ItemTemplate.Name.Contains("Chain Belt") ? "ChainBelt" :
                                   pi.ItemTemplate.Name.Contains("Geirr") ? "Geirr" : "ProjectileArray",
                        color = pi.ItemTemplate.Name.Split(' ').Last(), // e.g. "Red", "Blue", etc.
                        imageFile = pi.ItemTemplate.ImageFileName,
                        qty = pi.Quantity
                    }).ToList();

                var resourcesJson = JsonSerializer.Serialize(resourceList);

                replacements = new Dictionary<string, string>
                {
                    { "agentName", agentName },
                    { "level", level.ToString() },
                    { "silver", silver.ToString("N0") },
                    { "allianceName", alliance.Name },
                    { "allianceSlogan", alliance.Slogan },
                    { "allianceLevel", alliance.Level.ToString() },
                    { "allianceRating", alliance.Rating.ToString("N0") },
                    { "donatedSilver", alliance.DonatedSilver.ToString("N0") },
                    { "protectionWalls", alliance.ProtectionWallCount.ToString() },
                    { "speedAdaptor", alliance.SpeedAdaptorLevel.ToString() },
                    { "bruiserAdaptor", alliance.BruiserAdaptorLevel.ToString() },
                    { "tacticsAdaptor", alliance.TacticsAdaptorLevel.ToString() },
                    { "agentRole", profile.AllianceRole?.ToUpper() ?? "AGENT" },
                    { "personalDonated", profile.AllianceDonatedSilver.ToString("N0") },
                    { "memberCount", $"{alliance.Members.Count}/{maxMembers}" },
                    { "membersHtml", membersHtml },
                    { "joinRequestsHtml", joinRequestsHtml },
                    { "requestsDisplay", isOfficer ? "block" : "none" },
                    { "roleActionsDisplay", isLeader ? "block" : "none" },
                    { "resourcesJson", resourcesJson },
                    { "inAllianceDisplay", "block" },
                    { "noAllianceDisplay", "none" },
                    { "alliesCount", "0" },
                    { "formRequirementStatus", "RESTRICTED" },
                    { "allianceRecommendationsHtml", "" }
                };
            }

            return Content(RenderTemplate("alliance.html", replacements), "text/html");
        }

        [HttpPost("alliance/create")]
        public IActionResult CreateAllianceForm()
        {
            _logger.LogInformation("[Cygames] CreateAllianceForm called.");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            var name = Request.Form["alliance_name"].ToString();
            var slogan = Request.Form["alliance_slogan"].ToString();

            var res = _allianceEngine.CreateAlliance(profileId, name, slogan);
            if (res.Success)
            {
                return RedirectToAction("ServeAllianceHubPage");
            }

            return BadRequest(new { success = false, message = res.Message });
        }

        [HttpPost("alliance/donate-silver")]
        public IActionResult DonateSilverApi()
        {
            _logger.LogInformation("[Cygames] DonateSilverApi called.");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            if (!long.TryParse(Request.Form["amount"], out long amount))
            {
                return Ok(new { success = false, message = "⚠️ AMOUNT UNREADABLE // Provide a valid Silver credit contribution amount." });
            }

            var res = _allianceEngine.DonateSilver(profileId, amount);
            if (res.Success)
            {
                return Ok(new
                {
                    success = true,
                    message = res.Message,
                    silverBalance = res.NewPersonalSilver,
                    donatedSilver = res.NewAllianceDonatedSilver,
                    allianceLevel = res.NewAllianceLevel,
                    allianceRating = res.NewAllianceRating
                });
            }

            return Ok(new { success = false, message = res.Message });
        }

        [HttpPost("alliance/donate-resources")]
        public IActionResult DonateResourcesApi()
        {
            _logger.LogInformation("[Cygames] DonateResourcesApi called.");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            var groupKey = Request.Form["group_key"].ToString();
            if (string.IsNullOrEmpty(groupKey))
            {
                return Ok(new { success = false, message = "⚠️ VAULT BLOCK // Resource group key missing." });
            }

            var res = _allianceEngine.DonateResourceGroup(profileId, groupKey);
            if (res.Success)
            {
                // Also get the updated inventory stock of Resources to return to UI
                var profile = GetPlayerProfile(profileId, includeInventory: true);
                var updatedResources = profile!.InventoryItems
                    .Where(pi => pi.ItemTemplate != null && pi.ItemTemplate.Type == "Resource")
                    .Select(pi => new {
                        id = pi.ItemTemplateId,
                        qty = pi.Quantity
                    }).ToList();

                return Ok(new
                {
                    success = true,
                    message = res.Message,
                    donatedSilver = res.NewAllianceDonatedSilver,
                    allianceLevel = res.NewAllianceLevel,
                    allianceRating = res.NewAllianceRating,
                    resources = updatedResources
                });
            }

            return Ok(new { success = false, message = res.Message });
        }

        [HttpPost("alliance/purchase-upgrade")]
        public IActionResult PurchaseUpgradeApi()
        {
            _logger.LogInformation("[Cygames] PurchaseUpgradeApi called.");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            var upgradeType = Request.Form["upgrade_type"].ToString();
            if (string.IsNullOrEmpty(upgradeType))
            {
                return Ok(new { success = false, message = "⚠️ UPGRADE CODE MISSING." });
            }

            var res = _allianceEngine.PurchaseUpgrade(profileId, upgradeType);
            if (res.Success && res.Alliance != null)
            {
                return Ok(new
                {
                    success = true,
                    message = res.Message,
                    donatedSilver = res.Alliance.DonatedSilver,
                    protectionWalls = res.Alliance.ProtectionWallCount,
                    speedAdaptor = res.Alliance.SpeedAdaptorLevel,
                    bruiserAdaptor = res.Alliance.BruiserAdaptorLevel,
                    tacticsAdaptor = res.Alliance.TacticsAdaptorLevel
                });
            }

            return Ok(new { success = false, message = res.Message });
        }

        [HttpPost("alliance/send-request")]
        public IActionResult SendRequestApi()
        {
            _logger.LogInformation("[Cygames] SendRequestApi called.");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            if (!int.TryParse(Request.Form["alliance_id"], out int allianceId))
            {
                return Ok(new { success = false, message = "⚠️ DIVISION DESIGNATION MISSING." });
            }

            bool success = _allianceEngine.CreateJoinRequest(profileId, allianceId);
            if (success)
            {
                return Ok(new { success = true, message = "📡 REQUEST TRANSMITTED // S.H.I.E.L.D. Division command notified." });
            }

            return Ok(new { success = false, message = "⚠️ TRANSMISSION BLOCKED // Request is already active, or members capacity is saturated." });
        }

        [HttpPost("alliance/respond-request")]
        public IActionResult RespondRequestApi()
        {
            _logger.LogInformation("[Cygames] RespondRequestApi called.");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            if (!int.TryParse(Request.Form["request_id"], out int requestId))
            {
                return Ok(new { success = false, message = "⚠️ REQUEST ID INVALID." });
            }

            bool accept = Request.Form["accept"].ToString().ToLower() == "true";

            bool success = _allianceEngine.ProcessJoinRequest(profileId, requestId, accept);
            if (success)
            {
                return Ok(new { success = true, message = accept ? "✔️ AGENT CLEARED // Recruit successfully integrated into division." : "❌ REQUEST PURGED // Access request declined." });
            }

            return Ok(new { success = false, message = "⚠️ ACTION BLOCKED // Request is inactive, or max membership clearance limit exceeded." });
        }

        [HttpPost("alliance/set-role")]
        public IActionResult SetRoleApi()
        {
            _logger.LogInformation("[Cygames] SetRoleApi called.");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            if (!int.TryParse(Request.Form["member_id"], out int memberId))
            {
                return Ok(new { success = false, message = "⚠️ MEMBER ID INVALID." });
            }

            var role = Request.Form["role"].ToString();

            bool success = _allianceEngine.AssignMemberRole(profileId, memberId, role);
            if (success)
            {
                return Ok(new { success = true, message = $"✔️ COMMAND AUTHORISED // Role reassigned to: {role.ToUpper()}." });
            }

            return Ok(new { success = false, message = "⚠️ ACTION REFUSED // You lack Leader credentials, or officers clearance capacity is saturated." });
        }

        [HttpPost("alliance/leave")]
        public IActionResult LeaveAllianceApi()
        {
            _logger.LogInformation("[Cygames] LeaveAllianceApi called.");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            bool success = _allianceEngine.LeaveAlliance(profileId);
            if (success)
            {
                return Ok(new { success = true, message = "🚪 DIVISION VACATED // Successfully resigned from S.H.I.E.L.D. Division. Resignation fee deducted (-20,000 Silver)." });
            }

            return Ok(new { success = false, message = "⚠️ LEAVE DENIED // Leaders cannot leave without disbanding or delegating command. Personal balance must exceed 20,000 Silver." });
        }

        [HttpPost("alliance/disband")]
        public IActionResult DisbandAllianceApi()
        {
            _logger.LogInformation("[Cygames] DisbandAllianceApi called.");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            bool success = _allianceEngine.DisbandAlliance(profileId);
            if (success)
            {
                return Ok(new { success = true, message = "💥 DIVISION DECOMMISSIONED // Alliance successfully disbanded. Mainframe resources cleared." });
            }

            return Ok(new { success = false, message = "⚠️ DISBAND BLOCKED // Only the Alliance Leader can decommission this division." });
        }

        [HttpGet("trade_response/trade_list_advance")]
        public IActionResult ServeTradeCenter()
        {
            _logger.LogInformation("[Cygames] ServeTradeCenter called.");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;
            var profile = GetPlayerProfile(profileId, includeInventory: true);
            if (profile == null) return RedirectToAction("ServeGameTopPage");

            if (profile.Level < GameplaySettings.TradeMinLevel)
            {
                return Content($"<html><body style='background:#030712; color:#ef4444; font-family:sans-serif; text-align:center; padding-top:50px;'><h2>⚠️ SECURITY OVERRIDE ACTIVE</h2><p>Agent Clearance Level insufficient. Access to the Material Exchange Mainframe requires Clearance Level {GameplaySettings.TradeMinLevel}.</p><a href='/ultimate' style='color:#00f0ff;'>Return to Mainframe</a></body></html>", "text/html");
            }

            // Load incoming trades
            var incomingTrades = _dbContext.Trades
                .Include(t => t.SenderProfile)
                .Where(t => t.ReceiverProfileId == profile.Id && t.Status == "Pending")
                .ToList();

            // Load outgoing trades
            var outgoingTrades = _dbContext.Trades
                .Include(t => t.ReceiverProfile)
                .Where(t => t.SenderProfileId == profile.Id && t.Status == "Pending")
                .ToList();

            // Load historical trades (last 20)
            var tradeHistory = _dbContext.Trades
                .Include(t => t.SenderProfile)
                .Include(t => t.ReceiverProfile)
                .Where(t => (t.SenderProfileId == profile.Id || t.ReceiverProfileId == profile.Id) && t.Status != "Pending")
                .OrderByDescending(t => t.CompletedAt ?? t.CreatedAt)
                .Take(20)
                .ToList();

            // Helper lists to build HTML
            var incomingHtml = "";
            var outgoingHtml = "";
            var historyHtml = "";

            // Helper to get card template details
            var templates = _dbContext.CardTemplates.ToDictionary(t => t.Id, t => t);
            var itemTemplates = _dbContext.ItemTemplates.ToDictionary(t => t.Id, t => t);

            string FormatTradeDetails(Trade t, bool incoming)
            {
                var offeredCardIds = JsonSerializer.Deserialize<List<int>>(t.OfferedCardIdsJson) ?? new List<int>();
                var requestedCardIds = JsonSerializer.Deserialize<List<int>>(t.RequestedCardIdsJson) ?? new List<int>();
                var offeredItems = JsonSerializer.Deserialize<List<TradeItemOffer>>(t.OfferedItemsJson) ?? new List<TradeItemOffer>();
                var requestedItems = JsonSerializer.Deserialize<List<TradeItemOffer>>(t.RequestedItemsJson) ?? new List<TradeItemOffer>();

                var offerTextList = new List<string>();
                var reqTextList = new List<string>();

                if (t.OfferedSilver > 0) offerTextList.Add($"🪙 {t.OfferedSilver:N0} Silver");
                if (t.RequestedSilver > 0) reqTextList.Add($"🪙 {t.RequestedSilver:N0} Silver");

                foreach (var cid in offeredCardIds)
                {
                    var card = _dbContext.PlayerCards.Include(pc => pc.CardTemplate).FirstOrDefault(pc => pc.Id == cid);
                    if (card != null && card.CardTemplate != null)
                        offerTextList.Add($"🃏 {card.CardTemplate.Title} (Lv. {card.CurrentLevel})");
                }

                foreach (var cid in requestedCardIds)
                {
                    var card = _dbContext.PlayerCards.Include(pc => pc.CardTemplate).FirstOrDefault(pc => pc.Id == cid);
                    if (card != null && card.CardTemplate != null)
                        reqTextList.Add($"🃏 {card.CardTemplate.Title} (Lv. {card.CurrentLevel})");
                }

                foreach (var item in offeredItems)
                {
                    if (item.Quantity <= 0) continue;
                    if (itemTemplates.TryGetValue(item.ItemTemplateId, out var template))
                        offerTextList.Add($"🎒 {template.Name} x{item.Quantity}");
                }

                foreach (var item in requestedItems)
                {
                    if (item.Quantity <= 0) continue;
                    if (itemTemplates.TryGetValue(item.ItemTemplateId, out var template))
                        reqTextList.Add($"🎒 {template.Name} x{item.Quantity}");
                }

                string offerBox = offerTextList.Any() ? string.Join("<br>", offerTextList) : "<span style='color:var(--text-muted);'>No assets</span>";
                string reqBox = reqTextList.Any() ? string.Join("<br>", reqTextList) : "<span style='color:var(--text-muted);'>No assets</span>";

                if (incoming)
                {
                    return $@"
                    <div style='display:flex; justify-content:space-between; gap:15px; margin-top:10px;'>
                        <div class='trade-assets-box offer'>
                            <div class='trade-assets-title'>THEIR OFFER</div>
                            <div style='font-size:11px; font-family:""Space Mono"",monospace;'>{offerBox}</div>
                        </div>
                        <div class='trade-assets-box request'>
                            <div class='trade-assets-title'>THEIR REQUEST (FROM YOU)</div>
                            <div style='font-size:11px; font-family:""Space Mono"",monospace;'>{reqBox}</div>
                        </div>
                    </div>";
                }
                else
                {
                    return $@"
                    <div style='display:flex; justify-content:space-between; gap:15px; margin-top:10px;'>
                        <div class='trade-assets-box offer'>
                            <div class='trade-assets-title'>YOUR OFFER</div>
                            <div style='font-size:11px; font-family:""Space Mono"",monospace;'>{offerBox}</div>
                        </div>
                        <div class='trade-assets-box request'>
                            <div class='trade-assets-title'>YOUR REQUEST</div>
                            <div style='font-size:11px; font-family:""Space Mono"",monospace;'>{reqBox}</div>
                        </div>
                    </div>";
                }
            }

            if (incomingTrades.Any())
            {
                foreach (var t in incomingTrades)
                {
                    var details = FormatTradeDetails(t, incoming: true);
                    incomingHtml += $@"
                    <div class='trade-proposal-card' id='trade-row-{t.Id}'>
                        <div class='trade-card-header'>
                            <div>
                                <span class='trade-partner'>Operative: {t.SenderProfile?.Nickname ?? "Unknown"}</span>
                                <span class='trade-partner-lvl'>[Lv. {t.SenderProfile?.Level ?? 1}]</span>
                            </div>
                            <span class='trade-time'>Proposed: {t.CreatedAt.ToString("yyyy-MM-dd HH:mm")}</span>
                        </div>
                        {details}
                        <div style='display:flex; gap:10px; margin-top:15px;'>
                            <button class='trade-action-btn accept' onclick='respondTrade({t.Id}, ""accept"", this)'>🟢 AUTHORIZE EXCHANGE</button>
                            <button class='trade-action-btn decline' onclick='respondTrade({t.Id}, ""decline"", this)'>🔴 REFUSE PROPOSAL</button>
                        </div>
                    </div>";
                }
            }
            else
            {
                incomingHtml = "<div class='no-records-card'>No pending material exchange proposals in incoming mainframe queues.</div>";
            }

            if (outgoingTrades.Any())
            {
                foreach (var t in outgoingTrades)
                {
                    var details = FormatTradeDetails(t, incoming: false);
                    outgoingHtml += $@"
                    <div class='trade-proposal-card' id='trade-row-{t.Id}'>
                        <div class='trade-card-header'>
                            <div>
                                <span class='trade-partner'>Operative: {t.ReceiverProfile?.Nickname ?? "Unknown"}</span>
                                <span class='trade-partner-lvl'>[Lv. {t.ReceiverProfile?.Level ?? 1}]</span>
                            </div>
                            <span class='trade-time'>Proposed: {t.CreatedAt.ToString("yyyy-MM-dd HH:mm")}</span>
                        </div>
                        {details}
                        <div style='display:flex; gap:10px; margin-top:15px;'>
                            <button class='trade-action-btn cancel' onclick='respondTrade({t.Id}, ""cancel"", this)'>🔴 ABORT CONSIGNMENT</button>
                        </div>
                    </div>";
                }
            }
            else
            {
                outgoingHtml = "<div class='no-records-card'>No outgoing trade proposals registered in tactical logs.</div>";
            }

            if (tradeHistory.Any())
            {
                foreach (var t in tradeHistory)
                {
                    bool isSender = t.SenderProfileId == profile.Id;
                    var otherPartner = isSender ? t.ReceiverProfile?.Nickname : t.SenderProfile?.Nickname;
                    var direction = isSender ? "➡️ Sent to" : "⬅️ Recv from";
                    var statusColor = t.Status switch
                    {
                        "Completed" => "#10b981",
                        "Declined" => "#ef4444",
                        "Canceled" => "#f59e0b",
                        _ => "#9ca3af"
                    };

                    var details = FormatTradeDetails(t, incoming: !isSender);
                    historyHtml += $@"
                    <div class='trade-proposal-card' style='opacity: 0.8;'>
                        <div class='trade-card-header'>
                            <div>
                                <span class='trade-partner'>{direction} Operative {otherPartner}</span>
                                <span class='trade-status' style='color:{statusColor}; border-color:{statusColor}; font-size:8px; padding:1px 4px; border:1px solid; border-radius:3px; margin-left:8px; font-weight:bold;'>{t.Status.ToUpper()}</span>
                            </div>
                            <span class='trade-time'>Date: {(t.CompletedAt ?? t.CreatedAt).ToString("yyyy-MM-dd HH:mm")}</span>
                        </div>
                        {details}
                    </div>";
                }
            }
            else
            {
                historyHtml = "<div class='no-records-card'>No historical exchange operations in archive databanks.</div>";
            }

            var replacements = new Dictionary<string, string>
            {
                { "incomingProposalsHtml", incomingHtml },
                { "outgoingProposalsHtml", outgoingHtml },
                { "tradeHistoryHtml", historyHtml },
                { "agentName", profile.Nickname },
                { "level", profile.Level.ToString() },
                { "tradeMinLevel", GameplaySettings.TradeMinLevel.ToString() }
            };

            return Content(RenderTemplate("trade_center.html", replacements), "text/html");
        }

        [HttpGet("trade/propose/{receiverId}")]
        public IActionResult ServeTradeProposalPage(int receiverId)
        {
            _logger.LogInformation($"[Cygames] ServeTradeProposalPage called for target receiver: {receiverId}");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            var profile = GetPlayerProfile(profileId, includeInventory: true);
            if (profile == null) return RedirectToAction("ServeGameTopPage");

            if (profile.Level < GameplaySettings.TradeMinLevel)
            {
                return Content("Clearance Level Insufficient.", "text/plain");
            }

            var receiver = _dbContext.Profiles
                .Include(p => p.Cards)
                    .ThenInclude(c => c.CardTemplate)
                .FirstOrDefault(p => p.Id == receiverId);

            if (receiver == null) return NotFound("Receiver agent profile not located.");

            var eligibility = _tradeEngine.CheckTradingEligibility(profile.Id, receiver.Id);
            if (!eligibility.Success)
            {
                return Content($"<html><body style='background:#030712; color:#ef4444; font-family:sans-serif; text-align:center; padding-top:50px;'><h2>⚠️ TRADE BLOCKED</h2><p>{eligibility.Message}</p><a href='/ultimate/mypage/agent/{receiverId}' style='color:#00f0ff;'>Return to Dossier</a></body></html>", "text/html");
            }

            // Build sender's cards select list (only cards not locked in trade, not leader, and not in decks)
            var senderCards = profile.Cards
                .Where(c => !c.IsInTrade && !c.IsLeader && !c.IsInAttackDeck && !c.IsInDefenseDeck)
                .Select(c => new {
                    id = c.Id,
                    title = c.CardTemplate?.Title ?? "Unknown Card",
                    rarity = c.CardTemplate?.Rarity ?? "Normal",
                    level = c.CurrentLevel,
                    image = c.CardTemplate?.VisualTitle ?? "Standard_Shield"
                }).ToList();

            // Build receiver's cards select list (only cards not locked in trade, not leader, and not in decks)
            var receiverCards = receiver.Cards
                .Where(c => !c.IsInTrade && !c.IsLeader && !c.IsInAttackDeck && !c.IsInDefenseDeck)
                .Select(c => new {
                    id = c.Id,
                    title = c.CardTemplate?.Title ?? "Unknown Card",
                    rarity = c.CardTemplate?.Rarity ?? "Normal",
                    level = c.CurrentLevel,
                    image = c.CardTemplate?.VisualTitle ?? "Standard_Shield"
                }).ToList();

            // Filter out sender's items
            var senderItems = profile.InventoryItems
                .Where(i => i.Quantity > 0 && i.ItemTemplate != null && i.ItemTemplate.Type != "GachaTicket")
                .Select(i => new {
                    id = i.ItemTemplateId,
                    name = i.ItemTemplate?.Name ?? "Unknown Item",
                    quantity = i.Quantity,
                    image = i.ItemTemplate?.ImageFileName ?? "item_energy_full.png"
                }).ToList();

            // All possible tradeable items templates for receiver request select list
            var allItems = _dbContext.ItemTemplates
                .Where(t => t.Type != "GachaTicket")
                .Select(t => new {
                    id = t.Id,
                    name = t.Name,
                    image = t.ImageFileName
                }).ToList();

            var replacements = new Dictionary<string, string>
            {
                { "receiverId", receiverId.ToString() },
                { "receiverName", receiver.Nickname },
                { "receiverLevel", receiver.Level.ToString() },
                { "agentName", profile.Nickname },
                { "senderSilver", profile.SilverBalance.ToString() },
                { "senderCardsJson", JsonSerializer.Serialize(senderCards) },
                { "receiverCardsJson", JsonSerializer.Serialize(receiverCards) },
                { "senderItemsJson", JsonSerializer.Serialize(senderItems) },
                { "allItemsJson", JsonSerializer.Serialize(allItems) },
                { "tradeMaxCards", GameplaySettings.TradeMaxCards.ToString() }
            };

            return Content(RenderTemplate("trade_propose.html", replacements), "text/html");
        }

        public class ProposeTradeRequest
        {
            public long OfferedSilver { get; set; }
            public long RequestedSilver { get; set; }
            public List<int>? OfferedCardIds { get; set; }
            public List<int>? RequestedCardIds { get; set; }
            public List<TradeItemOffer>? OfferedItems { get; set; }
            public List<TradeItemOffer>? RequestedItems { get; set; }
        }

        [HttpPost("trade/propose/{receiverId}")]
        public IActionResult SubmitTradeProposal(int receiverId, [FromBody] ProposeTradeRequest req)
        {
            _logger.LogInformation($"[Cygames] SubmitTradeProposal called for target receiver: {receiverId}");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            var offeredCardIds = req.OfferedCardIds ?? new List<int>();
            var requestedCardIds = req.RequestedCardIds ?? new List<int>();
            var offeredItems = req.OfferedItems ?? new List<TradeItemOffer>();
            var requestedItems = req.RequestedItems ?? new List<TradeItemOffer>();

            var res = _tradeEngine.ProposeTrade(
                profileId, 
                receiverId, 
                req.OfferedSilver, 
                req.RequestedSilver, 
                offeredCardIds, 
                requestedCardIds, 
                offeredItems, 
                requestedItems);

            if (res.Success)
            {
                return Ok(new { success = true, message = res.Message });
            }
            return Ok(new { success = false, message = res.Message });
        }

        [HttpPost("trade/accept/{tradeId}")]
        public IActionResult AcceptTradeProposal(int tradeId)
        {
            _logger.LogInformation($"[Cygames] AcceptTradeProposal called for trade: {tradeId}");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            var res = _tradeEngine.AcceptTrade(tradeId, profileId);
            if (res.Success)
            {
                return Ok(new { success = true, message = res.Message });
            }
            return Ok(new { success = false, message = res.Message });
        }

        [HttpPost("trade/decline/{tradeId}")]
        public IActionResult DeclineTradeProposal(int tradeId)
        {
            _logger.LogInformation($"[Cygames] DeclineTradeProposal called for trade: {tradeId}");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            var res = _tradeEngine.DeclineTrade(tradeId, profileId);
            if (res.Success)
            {
                return Ok(new { success = true, message = res.Message });
            }
            return Ok(new { success = false, message = res.Message });
        }

        [HttpPost("trade/cancel/{tradeId}")]
        public IActionResult CancelTradeProposal(int tradeId)
        {
            _logger.LogInformation($"[Cygames] CancelTradeProposal called for trade: {tradeId}");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            var res = _tradeEngine.CancelTrade(tradeId, profileId);
            if (res.Success)
            {
                return Ok(new { success = true, message = res.Message });
            }
            return Ok(new { success = false, message = res.Message });
        }

        [HttpGet("mypage/commendations")]
        public IActionResult ServeLoginCommendationsHub()
        {
            _logger.LogInformation("[Cygames] ServeLoginCommendationsHub called.");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            var profile = GetPlayerProfile(profileId);
            if (profile == null) return RedirectToAction("ServeGameTopPage");

            var progressDtos = _loginCommendationEngine.GetPlayerProgress(profileId);
            var calendarsHtmlList = new List<string>();

            foreach (var dto in progressDtos)
            {
                var campaign = dto.Campaign;
                var total = dto.TotalLogins;
                var claimed = dto.ClaimedDays;
                var alreadyLoggedToday = dto.AlreadyLoggedToday;
                var nextClaimDay = dto.NextDayToClaim;

                var boxesHtmlList = new List<string>();
                foreach (var reward in campaign.Rewards.OrderBy(r => r.Day))
                {
                    var isClaimed = claimed.Contains(reward.Day);
                    var isActive = !alreadyLoggedToday && reward.Day == nextClaimDay;

                    var boxClass = "day-box";
                    if (isClaimed) boxClass += " claimed";
                    else if (isActive) boxClass += " active";
                    else boxClass += " locked";

                    var rewardText = "";
                    var rewardIcon = "🎁";
                    if (string.Equals(reward.RewardType, "Silver", StringComparison.OrdinalIgnoreCase))
                    {
                        rewardText = $"{reward.RewardValue:N0}<br>Silver";
                        rewardIcon = "🪙";
                    }
                    else if (string.Equals(reward.RewardType, "RallyPoints", StringComparison.OrdinalIgnoreCase))
                    {
                        rewardText = $"{reward.RewardValue:N0}<br>Rally Pts";
                        rewardIcon = "⚡";
                    }
                    else if (string.Equals(reward.RewardType, "MobaCoin", StringComparison.OrdinalIgnoreCase))
                    {
                        rewardText = $"{reward.RewardValue:N0}<br>MobaCoins";
                        rewardIcon = "🪙";
                    }
                    else if (string.Equals(reward.RewardType, "CardStock", StringComparison.OrdinalIgnoreCase))
                    {
                        rewardText = $"+{reward.RewardValue}<br>Hero Slots";
                        rewardIcon = "📦";
                    }
                    else if (string.Equals(reward.RewardType, "Item", StringComparison.OrdinalIgnoreCase))
                    {
                        var itemName = reward.RewardValue switch
                        {
                            1 => "Energy ISO-8",
                            2 => "Ultimate Ticket",
                            3 => "Attack ISO-8",
                            5 => "Shield Barrier",
                            _ => "Restorative"
                        };
                        rewardText = $"{reward.RewardQuantity}x<br>{itemName}";
                        rewardIcon = "📦";
                    }
                    else if (string.Equals(reward.RewardType, "Card", StringComparison.OrdinalIgnoreCase))
                    {
                        var cardName = reward.RewardValue switch
                        {
                            0 => "Ho Ho Ho Spider-Man",
                            _ => "Hero Card"
                        };
                        rewardText = $"{reward.RewardQuantity}x<br>{cardName}";
                        rewardIcon = "🃏";
                    }

                    var statusLabel = "";
                    if (isClaimed) statusLabel = "<span class=\"status-lbl\">STAMPED ✓</span>";
                    else if (isActive) statusLabel = "<span class=\"status-lbl pulsing-cyan\">READY TODAY</span>";
                    else statusLabel = "<span class=\"status-lbl\">LOCKED</span>";

                    boxesHtmlList.Add($"""
                    <div class="{boxClass}">
                        <div class="day-num">DAY {reward.Day}</div>
                        <div class="day-icon">{rewardIcon}</div>
                        <div class="day-reward">{rewardText}</div>
                        {statusLabel}
                    </div>
                    """);
                }

                var timerHtml = "";
                if (dto.SecondsUntilReset > 0)
                {
                    timerHtml = $"<div class=\"commendation-timer\" data-seconds=\"{dto.SecondsUntilReset}\">⏱️ NEXT CYCLE STAMP IN: CALCULATING...</div>";
                }
                else
                {
                    timerHtml = "<div class=\"commendation-timer infinite\">⏱️ UNRESTRICTED ACCESS ACTIVE</div>";
                }

                var statusHeaderMsg = alreadyLoggedToday 
                    ? "<span style='color: var(--hud-green);'>✓ SECURED TODAY</span>" 
                    : "<span style='color: var(--hud-blue);' class='pulsing-cyan'>📡 STAMP PENDING</span>";

                calendarsHtmlList.Add($"""
                <div class="campaign-container">
                    <div class="campaign-header-row">
                        <div>
                            <h2 class="campaign-title">{campaign.Title}</h2>
                            <p class="campaign-desc">{campaign.Description}</p>
                        </div>
                        <div style="text-align: right;">
                            <div class="campaign-status">{statusHeaderMsg}</div>
                            {timerHtml}
                        </div>
                    </div>
                    <div class="commendations-grid">
                        {string.Join("\n", boxesHtmlList)}
                    </div>
                </div>
                """);
            }

            var replacements = new Dictionary<string, string>
            {
                { "level", profile.Level.ToString() },
                { "agentName", profile.Nickname },
                { "energyCur", profile.EnergyCurrent.ToString() },
                { "energyMax", profile.EnergyMax.ToString() },
                { "energyPct", ((double)profile.EnergyCurrent / profile.EnergyMax * 100).ToString("N0") },
                { "commendationsHtml", string.Join("\n", calendarsHtmlList) }
            };

            return Content(RenderTemplate("login_commendations.html", replacements), "text/html");
        }

        [HttpGet("mypage/assignments")]
        public IActionResult ServeAssignmentsHub()
        {
            _logger.LogInformation("[Cygames] ServeAssignmentsHub called.");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            var profile = GetPlayerProfile(profileId);
            if (profile == null) return RedirectToAction("ServeGameTopPage");

            var progressDtos = _assignmentEngine.GetPlayerProgress(profileId);

            var initialHtmlList = new List<string>();
            var levelHtmlList = new List<string>();
            var specialBatches = new Dictionary<(string GroupName, int Batch), List<PlayerAssignmentProgressDto>>();

            foreach (var dto in progressDtos)
            {
                if (string.Equals(dto.Template.GroupName, "Initial", StringComparison.OrdinalIgnoreCase))
                {
                    initialHtmlList.Add(RenderAssignmentCard(dto));
                }
                else if (string.Equals(dto.Template.GroupName, "Level", StringComparison.OrdinalIgnoreCase))
                {
                    levelHtmlList.Add(RenderAssignmentCard(dto));
                }
                else
                {
                    var key = (dto.Template.GroupName, dto.Template.Batch);
                    if (!specialBatches.ContainsKey(key))
                    {
                        specialBatches[key] = new List<PlayerAssignmentProgressDto>();
                    }
                    specialBatches[key].Add(dto);
                }
            }

            var specialHtmlList = new List<string>();

            foreach (var kvp in specialBatches.OrderBy(b => b.Key.GroupName).ThenBy(b => b.Key.Batch))
            {
                var groupName = kvp.Key.GroupName;
                var batchNum = kvp.Key.Batch;
                var batchQuests = kvp.Value;

                var regularQuests = batchQuests.Where(q => !q.Template.IsCompletionBonus).OrderBy(q => q.Template.Id).ToList();
                var completionBonus = batchQuests.FirstOrDefault(q => q.Template.IsCompletionBonus);

                var regularHtml = string.Join("\n", regularQuests.Select(RenderAssignmentCard));
                var bonusHtml = "";

                if (completionBonus != null)
                {
                    var temp = completionBonus.Template;
                    var isClaimed = completionBonus.IsClaimed;
                    var isCompleted = completionBonus.IsCompleted;

                    var cardClass = "bonus-card";
                    if (isClaimed) cardClass += " claimed";
                    else if (isCompleted) cardClass += " completed";
                    else cardClass += " locked";

                    var rewardText = "";
                    if (string.Equals(temp.RewardType, "Card", StringComparison.OrdinalIgnoreCase))
                    {
                        var cardName = temp.RewardValue switch
                        {
                            0 when temp.GroupName.Contains("Special Assignment 1") => "[Leopardess] Tigra",
                            0 when temp.GroupName.Contains("Special Assignment 2") => "[Cosmic Energy] Havok",
                            _ => $"Hero Card {temp.RewardValue}"
                        };
                        rewardText = $"{cardName}";
                    }
                    else
                    {
                        rewardText = $"{temp.RewardType} x{temp.RewardQuantity}";
                    }

                    var statusLabel = "";
                    if (isClaimed) statusLabel = "<span class=\"bonus-status-tag claimed\">SECURED</span>";
                    else if (isCompleted) statusLabel = "<span class=\"bonus-status-tag ready\">READY</span>";
                    else statusLabel = "<span class=\"bonus-status-tag locked\">LOCKED</span>";

                    bonusHtml = $"""
                    <div class="{cardClass}" id="card-{temp.Id}">
                        <div class="bonus-glow"></div>
                        <div class="bonus-header">
                            <span class="bonus-title">// BATCH {batchNum} COMPLETION BONUS</span>
                            {statusLabel}
                        </div>
                        <div class="bonus-body">
                            <div class="bonus-asset-icon">🎁</div>
                            <div class="bonus-details">
                                <h4>{temp.Title}</h4>
                                <p>{temp.Description}</p>
                                <div class="bonus-reward-value">TACTICAL ASSET: <span>{rewardText}</span></div>
                            </div>
                        </div>
                    </div>
                    """;
                }

                var firstQuest = batchQuests.FirstOrDefault();
                var secondsRemaining = firstQuest?.SecondsRemaining ?? -1;
                var timerHtml = "";

                if (secondsRemaining > 0)
                {
                    timerHtml = $"<div class=\"batch-timer\" data-seconds=\"{secondsRemaining}\">⏱️ DETECTING WINDOW: CALCULATING...</div>";
                }
                else if (secondsRemaining == 0)
                {
                    timerHtml = "<div class=\"batch-timer expired\">⏱️ TIME-WINDOW EXPIRED</div>";
                }
                else
                {
                    timerHtml = "<div class=\"batch-timer infinite\">⏱️ UNRESTRICTED ACCESS ACTIVE</div>";
                }

                specialHtmlList.Add($"""
                <div class="special-batch-container">
                    <div class="batch-header-bar">
                        <div class="batch-title">// OPERATION: {groupName.ToUpper()} [BATCH {batchNum}]</div>
                        {timerHtml}
                    </div>
                    <div class="batch-quests-grid">
                        {regularHtml}
                    </div>
                    {bonusHtml}
                </div>
                """);
            }

            var replacements = new Dictionary<string, string>
            {
                { "level", profile.Level.ToString() },
                { "agentName", profile.Nickname },
                { "energyCur", profile.EnergyCurrent.ToString() },
                { "energyMax", profile.EnergyMax.ToString() },
                { "energyPct", ((double)profile.EnergyCurrent / profile.EnergyMax * 100).ToString("N0") },
                { "initialAssignmentsHtml", initialHtmlList.Count > 0 ? string.Join("\n", initialHtmlList) : "<div class='no-quests'>// ALL INITIAL ONBOARDING GOALS CLEARED</div>" },
                { "levelAssignmentsHtml", levelHtmlList.Count > 0 ? string.Join("\n", levelHtmlList) : "<div class='no-quests'>// LEVELING GOALS COMPLETED</div>" },
                { "specialAssignmentsHtml", specialHtmlList.Count > 0 ? string.Join("\n", specialHtmlList) : "<div class='no-quests'>// NO SPECIAL EVENTS ACTIVE IN THIS SECTOR</div>" }
            };

            return Content(RenderTemplate("assignments.html", replacements), "text/html");
        }

        [HttpPost("mypage/assignments/claim/{assignmentId}")]
        public IActionResult ClaimAssignment(string assignmentId)
        {
            _logger.LogInformation($"[Cygames] ClaimAssignment called for assignment: {assignmentId}");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;

            var result = _assignmentEngine.ClaimReward(profileId, assignmentId);
            return Ok(result);
        }

        private string RenderAssignmentCard(PlayerAssignmentProgressDto dto)
        {
            var temp = dto.Template;
            var isClaimed = dto.IsClaimed;
            var isCompleted = dto.IsCompleted;
            var current = dto.CurrentProgress;
            var target = temp.GoalTarget;

            var cardClass = "assignment-card";
            if (isClaimed) cardClass += " claimed";
            else if (isCompleted) cardClass += " completed";
            else cardClass += " active";

            var rewardText = "";
            if (string.Equals(temp.RewardType, "Silver", StringComparison.OrdinalIgnoreCase))
            {
                rewardText = $"🪙 {temp.RewardValue:N0} Silver";
            }
            else if (string.Equals(temp.RewardType, "RallyPoints", StringComparison.OrdinalIgnoreCase))
            {
                rewardText = $"⚡ {temp.RewardValue:N0} Rally Points";
            }
            else if (string.Equals(temp.RewardType, "MobaCoin", StringComparison.OrdinalIgnoreCase))
            {
                rewardText = $"🪙 {temp.RewardValue:N0} MobaCoins";
            }
            else if (string.Equals(temp.RewardType, "CardStock", StringComparison.OrdinalIgnoreCase))
            {
                rewardText = $"📦 +{temp.RewardValue} Hero Slots";
            }
            else if (string.Equals(temp.RewardType, "Item", StringComparison.OrdinalIgnoreCase))
            {
                var itemName = temp.RewardValue switch
                {
                    1 => "Energy ISO-8 (L)",
                    2 => "Ultimate Gacha Ticket",
                    3 => "Attack ISO-8 (L)",
                    5 => "Shield Barrier",
                    _ => $"Supply Item {temp.RewardValue}"
                };
                rewardText = $"📦 {itemName} (x{temp.RewardQuantity})";
            }
            else if (string.Equals(temp.RewardType, "Card", StringComparison.OrdinalIgnoreCase))
            {
                var cardName = temp.RewardValue switch
                {
                    0 when temp.GroupName.Contains("Special Assignment 1") => "[Leopardess] Tigra",
                    0 when temp.GroupName.Contains("Special Assignment 2") => "[Cosmic Energy] Havok",
                    _ => $"Hero Card {temp.RewardValue}"
                };
                rewardText = $"🃏 {cardName} (x{temp.RewardQuantity})";
            }

            var progressPct = target > 0 ? Math.Min(100, (double)current / target * 100) : 0;
            var progressText = $"{current} / {target}";

            var goalBadge = temp.GoalType switch
            {
                "DrawRallyPack" => "RECRUITMENT",
                "EnhanceCard" => "UPGRADE",
                "PvpBattle" => "COMBAT ENGAGEMENT",
                "CompleteOperation" => "STORY TARGET",
                "ShieldRequest" => "ALLIANCE REACH",
                "LoginTomorrow" => "DAILY LINK",
                "LevelUp" => "CLEARANCE PROGRESSION",
                "WinStreak" => "TACTICAL STREAK",
                "PvpWin" => "FIELD VICTORY",
                "SkillsActivated" => "ABILITY SYNCHRONIZATION",
                "MoraleWin" => "SYNERGY VICTORY",
                "StartMission" => "SECTOR DEPLOYMENT",
                "FuseCard" => "FUSION SYNTHESIS",
                _ => temp.GoalType.ToUpper()
            };

            var buttonHtml = "";
            if (isClaimed)
            {
                buttonHtml = "<button class=\"claim-btn claimed\" disabled>SECURED</button>";
            }
            else if (isCompleted)
            {
                buttonHtml = $"<button class=\"claim-btn ready\" onclick=\"claimAssignment('{temp.Id}', this)\">CLAIM REWARD</button>";
            }
            else
            {
                buttonHtml = "<button class=\"claim-btn locked\" disabled>IN PROGRESS</button>";
            }

            var progressBarHtml = "";
            if (!temp.IsCompletionBonus)
            {
                progressBarHtml = $"""
                <div class="progress-container">
                    <div class="progress-bar-wrapper">
                        <div class="progress-bar-fill" style="width: {progressPct:N0}%;"></div>
                    </div>
                    <div class="progress-label">{progressText}</div>
                </div>
                """;
            }
            else
            {
                progressBarHtml = """
                <div class="progress-container completion-bonus">
                    <div class="bonus-tag">// BATCH REWARD</div>
                </div>
                """;
            }

            return $"""
            <div class="{cardClass}" id="card-{temp.Id}">
                <div class="card-inner">
                    <div class="card-glow"></div>
                    <div class="card-header">
                        <span class="card-badge">{goalBadge}</span>
                        <span class="card-reward-label">{rewardText}</span>
                    </div>
                    <h3 class="card-title">{temp.Title}</h3>
                    <p class="card-desc">{temp.Description}</p>
                    {progressBarHtml}
                    <div class="card-action">
                        {buttonHtml}
                    </div>
                </div>
            </div>
            """;
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
