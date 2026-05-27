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
        private readonly IBattleEngine _battleEngine;

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
            ILeaderManager leaderManager,
            IBattleEngine battleEngine)
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
            _battleEngine = battleEngine;
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
                        _ => "📦"
                    };

                    var color = temp.Type switch
                    {
                        "EnergyRestorative" => "#00f0ff",
                        "AttackPowerRestorative" => "#ef4444",
                        "DefensePowerRestorative" => "#10b981",
                        "LevelUpSerum" => "#a855f7",
                        "MasteryIso8" => "#a5b4fc",
                        _ => "#f59e0b"
                    };

                    var useButton = (temp.Type.EndsWith("Restorative") || temp.Type == "LevelUpSerum" || temp.Type == "MasteryIso8" || temp.Type == "InventoryExpansion") 
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

            var replacements = new Dictionary<string, string>
            {
                { "energyCur", energyCur.ToString() },
                { "energyMax", energyMax.ToString() },
                { "energyPct", energyPct.ToString() },
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
            var profile = GetPlayerProfile(profileId, includeInventory: true);
            if (profile == null) return BadRequest(new { success = false, message = "Profile mismatch." });

            var groupKey = Request.Form["group_key"].ToString();
            if (string.IsNullOrEmpty(groupKey))
            {
                return BadRequest(new { success = false, message = "Group key missing." });
            }

            string groupName = "";
            if (groupKey == "StormsCape") groupName = "Storm's Cape";
            else if (groupKey == "Suitcase") groupName = "Suitcase";
            else if (groupKey == "SwordOfProficiency") groupName = "Sword of Proficiency";
            else if (groupKey == "AssassinsChoker") groupName = "Assassin's Choker";
            else if (groupKey == "ChainBelt") groupName = "Chain Belt";
            else if (groupKey == "Geirr") groupName = "Geirr";
            else if (groupKey == "ProjectileArray") groupName = "Projectile Array";

            if (string.IsNullOrEmpty(groupName))
            {
                return BadRequest(new { success = false, message = "Invalid resource group." });
            }

            var redemptionsDict = new Dictionary<string, int>();
            if (!string.IsNullOrEmpty(profile.ResourceRedemptionsJson))
            {
                try { redemptionsDict = JsonSerializer.Deserialize<Dictionary<string, int>>(profile.ResourceRedemptionsJson) ?? new(); } catch {}
            }

            int count = 0;
            redemptionsDict.TryGetValue(groupKey, out count);
            if (count >= 3)
            {
                return Ok(new { success = false, message = "⚠️ MAXIMUM REDEMPTIONS REACHED // S.H.I.E.L.D. data caps reached." });
            }

            var groupTemplates = _dbContext.ItemTemplates
                .Where(t => t.Type == "Resource" && (
                    (groupKey == "StormsCape" && t.Name.Contains("Storm's") && t.Name.Contains("Cape")) ||
                    (groupKey == "Suitcase" && t.Name.Contains("Suitcase")) ||
                    (groupKey == "SwordOfProficiency" && t.Name.Contains("Sword")) ||
                    (groupKey == "AssassinsChoker" && t.Name.Contains("Assassin's") && t.Name.Contains("Choker")) ||
                    (groupKey == "ChainBelt" && t.Name.Contains("Chain Belt")) ||
                    (groupKey == "Geirr" && t.Name.Contains("Geirr")) ||
                    (groupKey == "ProjectileArray" && t.Name.Contains("Projectile Array"))
                )).ToList();

            if (groupTemplates.Count < 6)
            {
                return Ok(new { success = false, message = "⚠️ SYSTEM ERROR // Template files corrupted." });
            }

            var inventoryMatch = new List<PlayerInventoryItem>();
            foreach (var temp in groupTemplates)
            {
                var pItem = profile.InventoryItems.FirstOrDefault(pi => pi.ItemTemplateId == temp.Id && pi.Quantity >= 1);
                if (pItem == null)
                {
                    return Ok(new { success = false, message = $"⚠️ SET INCOMPLETE // Missing required colors." });
                }
                inventoryMatch.Add(pItem);
            }

            int setIndex = count + 1;
            string rewardMessage = "";
            string rewardCardTitle = "";
            bool isCardReward = true;

            if (setIndex == 1 || setIndex == 3)
            {
                if (groupKey == "StormsCape") rewardCardTitle = "Queen of Lightning Storm";
                else if (groupKey == "Suitcase") rewardCardTitle = "Legal Eagle She-Hulk";
                else if (groupKey == "SwordOfProficiency") rewardCardTitle = "Taskmaster";
                else if (groupKey == "AssassinsChoker") rewardCardTitle = "X-23";
                else if (groupKey == "ChainBelt") rewardCardTitle = "Knuckle Up Luke Cage";
                else if (groupKey == "Geirr") rewardCardTitle = "Escort of Souls Valkyrie";
                else if (groupKey == "ProjectileArray") rewardCardTitle = "Friend In Need War Machine";

                int currentCardCount = _dbContext.PlayerCards.Count(pc => pc.PlayerProfileId == profile.Id);
                if (currentCardCount >= profile.MaxCardCapacity)
                {
                    return Ok(new { success = false, message = $"⚠️ DEPLOYMENT REJECTED // SQUAD FILES FULL ({profile.MaxCardCapacity}/{profile.MaxCardCapacity})." });
                }
            }
            else
            {
                isCardReward = false;
            }

            foreach (var pItem in inventoryMatch)
            {
                pItem.Quantity = Math.Max(0, pItem.Quantity - 1);
            }

            if (isCardReward)
            {
                var cardTemplate = _dbContext.CardTemplates.FirstOrDefault(t => t.Title == rewardCardTitle);
                if (cardTemplate == null)
                {
                    cardTemplate = _dbContext.CardTemplates.FirstOrDefault();
                }

                if (cardTemplate != null)
                {
                    var newCard = new PlayerCard { PlayerProfileId = profile.Id };
                    newCard.InitializeStats(cardTemplate, GameplaySettings.DefaultMasteryPercentage);
                    _dbContext.PlayerCards.Add(newCard);
                    rewardMessage = $"RECOVERED HERO: {cardTemplate.VisualTitle ?? cardTemplate.Title} added to your deck roster!";
                }
            }
            else
            {
                var serumTemplate = _dbContext.ItemTemplates.FirstOrDefault(t => t.Type == "LevelUpSerum");
                if (serumTemplate != null)
                {
                    var pItem = _dbContext.PlayerInventoryItems.FirstOrDefault(pi => pi.PlayerProfileId == profile.Id && pi.ItemTemplateId == serumTemplate.Id);
                    if (pItem == null)
                    {
                        pItem = new PlayerInventoryItem
                        {
                            PlayerProfileId = profile.Id,
                            ItemTemplateId = serumTemplate.Id,
                            Quantity = 3
                        };
                        _dbContext.PlayerInventoryItems.Add(pItem);
                    }
                    else
                    {
                        pItem.Quantity += 3;
                    }
                    rewardMessage = $"SECURED SUPPLIES: Added 3x Level-Up ISO-8 Serums to tactical depot!";
                }
                else
                {
                    rewardMessage = "ISO-8 supply seeder failed.";
                }
            }

            redemptionsDict[groupKey] = count + 1;
            profile.ResourceRedemptionsJson = JsonSerializer.Serialize(redemptionsDict);

            _dbContext.SaveChanges();

            var updatedResources = profile.InventoryItems
                .Where(pi => pi.ItemTemplate != null && pi.ItemTemplate.Type == "Resource")
                .Select(pi => new {
                    id = pi.ItemTemplateId,
                    qty = pi.Quantity
                }).ToList();

            return Ok(new
            {
                success = true,
                message = $"CONGRATULATIONS // {rewardMessage}",
                redemptions = redemptionsDict,
                resources = updatedResources
            });
        }

        [HttpPost("resource/donate")]
        public IActionResult DonateResources()
        {
            _logger.LogInformation("[Cygames] DonateResources called.");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;
            var profile = GetPlayerProfile(profileId, includeInventory: true);
            if (profile == null) return BadRequest(new { success = false, message = "Profile mismatch." });

            var groupKey = Request.Form["group_key"].ToString();
            if (string.IsNullOrEmpty(groupKey))
            {
                return BadRequest(new { success = false, message = "Group key missing." });
            }

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
                return Ok(new { success = false, message = "⚠️ RESOURCE STOCK EMPTY // No excess drops to donate." });
            }

            long totalSilverGained = 0;
            int totalItemsDonated = 0;

            foreach (var pi in groupResources)
            {
                int quantity = pi.Quantity;
                int valuePerItem = pi.ItemTemplate?.EffectValue ?? 2000;
                totalSilverGained += (long)quantity * valuePerItem;
                totalItemsDonated += quantity;

                pi.Quantity = 0;
            }

            profile.SilverBalance += totalSilverGained;
            _dbContext.SaveChanges();

            var updatedResources = profile.InventoryItems
                .Where(pi => pi.ItemTemplate != null && pi.ItemTemplate.Type == "Resource")
                .Select(pi => new {
                    id = pi.ItemTemplateId,
                    qty = pi.Quantity
                }).ToList();

            return Ok(new
            {
                success = true,
                message = $"DONATION COMPLETE // Contributed {totalItemsDonated} assets to S.H.I.E.L.D. tactical mainframe. Credited +{totalSilverGained} Silver!",
                silverBalance = profile.SilverBalance,
                resources = updatedResources
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
            
            var sender = _dbContext.Profiles.FirstOrDefault(p => p.Id == profileId);
            var receiver = _dbContext.Profiles.FirstOrDefault(p => p.Id == targetId);

            if (sender == null) return BadRequest("Sender profile not found.");
            if (receiver == null) return BadRequest("Target profile not found.");
            if (sender.Id == receiver.Id) return BadRequest("You cannot rally yourself, Agent!");

            // Check standard 24h cooldown
            var cutoff = DateTime.UtcNow.AddHours(-24);
            var existingRally = _dbContext.RallyLogs
                .FirstOrDefault(rl => rl.SenderProfileId == sender.Id && rl.ReceiverProfileId == receiver.Id && rl.RalliedAt >= cutoff);

            if (existingRally != null)
            {
                var timeRemaining = existingRally.RalliedAt.AddHours(24) - DateTime.UtcNow;
                var hours = (int)timeRemaining.TotalHours;
                var minutes = timeRemaining.Minutes;
                return Ok(new
                {
                    success = false,
                    message = $"Cooldown active. You can rally this agent again in {hours}h {minutes}m."
                });
            }

            // Determine if they are S.H.I.E.L.D. Team members
            var isFriend = _dbContext.ShieldTeamMembers
                .Any(m => m.Status == "Accepted" &&
                         ((m.ProfileId == sender.Id && m.MemberProfileId == receiver.Id) ||
                          (m.ProfileId == receiver.Id && m.MemberProfileId == sender.Id)));

            int senderPoints = isFriend ? 20 : 10;
            int receiverPoints = isFriend ? 10 : 5;

            // Update points
            sender.RallyPoints += senderPoints;
            receiver.RallyPoints += receiverPoints;

            // Log rally activity
            var log = new RallyLog
            {
                SenderProfileId = sender.Id,
                ReceiverProfileId = receiver.Id,
                RalliedAt = DateTime.UtcNow
            };
            _dbContext.RallyLogs.Add(log);

            try
            {
                _dbContext.SaveChanges();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Cygames] Failed to process rally point transaction.");
                return Ok(new { success = false, message = "Database error occurred during rally point credit." });
            }

            return Ok(new
            {
                success = true,
                message = $"Successfully rallied {receiver.Nickname}! You gained +{senderPoints} Rally Points. {receiver.Nickname} gained +{receiverPoints}.",
                senderPoints = senderPoints,
                receiverPoints = receiverPoints,
                newRallyPoints = sender.RallyPoints
            });
        }

        [HttpPost("friend/propose")]
        public IActionResult ProposeTeamMember([FromForm] int targetId)
        {
            _logger.LogInformation($"[Cygames] ProposeTeamMember targetId: {targetId}");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;
            var profile = GetPlayerProfile(profileId);
            if (profile == null) return Ok(new { success = false, message = "Profile not synced." });

            if (profile.Id == targetId)
            {
                return Ok(new { success = false, message = "You cannot propose S.H.I.E.L.D. Team membership to yourself." });
            }

            var target = _dbContext.Profiles.FirstOrDefault(p => p.Id == targetId);
            if (target == null)
            {
                return Ok(new { success = false, message = "Proposed Agent could not be located in S.H.I.E.L.D. directory." });
            }

            // Check if relationship already exists
            var existing = _dbContext.ShieldTeamMembers
                .FirstOrDefault(m => (m.ProfileId == profile.Id && m.MemberProfileId == targetId) || (m.ProfileId == targetId && m.MemberProfileId == profile.Id));

            if (existing != null)
            {
                if (existing.Status == "Accepted")
                    return Ok(new { success = false, message = "This agent is already on your active S.H.I.E.L.D. Team." });
                else
                    return Ok(new { success = false, message = "A S.H.I.E.L.D. Team proposal is already pending with this agent." });
            }

            // Check capacity for both
            int myMax = profile.Level >= 10 ? Math.Min(50, 6 + (profile.Level - 10) / 2) : 5;
            int myCount = _dbContext.ShieldTeamMembers.Count(m => (m.ProfileId == profile.Id || m.MemberProfileId == profile.Id) && m.Status == "Accepted");
            if (myCount >= myMax)
            {
                return Ok(new { success = false, message = "⚠️ PROPOSAL DENIED // Your S.H.I.E.L.D. Team capacity has reached maximum limits." });
            }

            int targetMax = target.Level >= 10 ? Math.Min(50, 6 + (target.Level - 10) / 2) : 5;
            int targetCount = _dbContext.ShieldTeamMembers.Count(m => (m.ProfileId == targetId || m.MemberProfileId == targetId) && m.Status == "Accepted");
            if (targetCount >= targetMax)
            {
                return Ok(new { success = false, message = "⚠️ PROPOSAL DENIED // The target Agent has reached their maximum S.H.I.E.L.D. Team capacity." });
            }

            var proposal = new ShieldTeamMember
            {
                ProfileId = profile.Id,
                MemberProfileId = targetId,
                Status = "Pending",
                CreatedAt = DateTime.UtcNow
            };
            _dbContext.ShieldTeamMembers.Add(proposal);
            _dbContext.SaveChanges();

            return Ok(new { success = true, message = $"S.H.I.E.L.D. Team proposal successfully transmitted to agent {target.Nickname}." });
        }

        [HttpPost("friend/accept")]
        public IActionResult AcceptTeamProposal([FromForm] int proposerId)
        {
            _logger.LogInformation($"[Cygames] AcceptTeamProposal proposerId: {proposerId}");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;
            var profile = GetPlayerProfile(profileId);
            if (profile == null) return Ok(new { success = false, message = "Profile not synced." });

            var proposal = _dbContext.ShieldTeamMembers
                .FirstOrDefault(m => m.ProfileId == proposerId && m.MemberProfileId == profile.Id && m.Status == "Pending");

            if (proposal == null)
            {
                return Ok(new { success = false, message = "No pending S.H.I.E.L.D. Team proposal from this agent." });
            }

            var proposer = _dbContext.Profiles.FirstOrDefault(p => p.Id == proposerId);
            if (proposer == null)
            {
                return Ok(new { success = false, message = "Proposer profile not located." });
            }

            // Check capacity
            int myMax = profile.Level >= 10 ? Math.Min(50, 6 + (profile.Level - 10) / 2) : 5;
            int myCount = _dbContext.ShieldTeamMembers.Count(m => (m.ProfileId == profile.Id || m.MemberProfileId == profile.Id) && m.Status == "Accepted");
            if (myCount >= myMax)
            {
                return Ok(new { success = false, message = "⚠️ TRANSITION FAILED // Your S.H.I.E.L.D. Team has reached maximum limits. You must dismiss an agent first." });
            }

            int proposerMax = proposer.Level >= 10 ? Math.Min(50, 6 + (proposer.Level - 10) / 2) : 5;
            int proposerCount = _dbContext.ShieldTeamMembers.Count(m => (m.ProfileId == proposerId || m.MemberProfileId == proposerId) && m.Status == "Accepted");
            if (proposerCount >= proposerMax)
            {
                return Ok(new { success = false, message = "⚠️ TRANSITION FAILED // Proposer's S.H.I.E.L.D. Team capacity is at maximum limits." });
            }

            // Set to accepted and award points
            proposal.Status = "Accepted";
            
            profile.StatPoints += 5;
            proposer.StatPoints += 5;

            _dbContext.SaveChanges();

            return Ok(new { success = true, message = $"Proposal accepted! You are now team members with {proposer.Nickname}. Both agents have received 5 S.H.I.E.L.D. Attribute points!" });
        }

        [HttpPost("friend/ignore")]
        public IActionResult IgnoreTeamProposal([FromForm] int proposerId)
        {
            _logger.LogInformation($"[Cygames] IgnoreTeamProposal proposerId: {proposerId}");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;
            var profile = GetPlayerProfile(profileId);
            if (profile == null) return Ok(new { success = false, message = "Profile not synced." });

            var proposal = _dbContext.ShieldTeamMembers
                .FirstOrDefault(m => m.ProfileId == proposerId && m.MemberProfileId == profile.Id && m.Status == "Pending");

            if (proposal != null)
            {
                _dbContext.ShieldTeamMembers.Remove(proposal);
                _dbContext.SaveChanges();
            }

            return Ok(new { success = true, message = "S.H.I.E.L.D. Team proposal dismissed." });
        }

        [HttpPost("friend/remove")]
        public IActionResult RemoveTeamMember([FromForm] int memberId)
        {
            _logger.LogInformation($"[Cygames] RemoveTeamMember memberId: {memberId}");
            var user = ResolveCurrentUser();
            var profileId = user.Profile?.Id ?? 1;
            var profile = GetPlayerProfile(profileId);
            if (profile == null) return Ok(new { success = false, message = "Profile not synced." });

            var relation = _dbContext.ShieldTeamMembers
                .FirstOrDefault(m => ((m.ProfileId == profile.Id && m.MemberProfileId == memberId) || (m.ProfileId == memberId && m.MemberProfileId == profile.Id)) && m.Status == "Accepted");

            if (relation == null)
            {
                return Ok(new { success = false, message = "Agent is not currently a member of your S.H.I.E.L.D. Team." });
            }

            var other = _dbContext.Profiles.FirstOrDefault(p => p.Id == memberId);
            if (other == null)
            {
                return Ok(new { success = false, message = "Target profile not located." });
            }

            // Remove connection
            _dbContext.ShieldTeamMembers.Remove(relation);

            // Apply points deduction to active player
            int pointsToDeduct = 5;
            bool wasSubsequentRemovalPenaltyApplied = false;

            if (GameplaySettings.EnableFriendRemoval24HourPenalty)
            {
                if (profile.LastRemovalTime.HasValue && (DateTime.UtcNow - profile.LastRemovalTime.Value).TotalHours < 24)
                {
                    pointsToDeduct = 6;
                    wasSubsequentRemovalPenaltyApplied = true;
                }
            }

            for (int i = 0; i < pointsToDeduct; i++)
            {
                if (profile.EnergyMax >= profile.AttackPower && profile.EnergyMax >= profile.DefensePower)
                {
                    profile.EnergyMax = Math.Max(10, profile.EnergyMax - 1);
                    if (profile.EnergyCurrent > profile.EnergyMax)
                    {
                        profile.EnergyCurrent = profile.EnergyMax;
                    }
                }
                else if (profile.AttackPower >= profile.EnergyMax && profile.AttackPower >= profile.DefensePower)
                {
                    profile.AttackPower = Math.Max(1, profile.AttackPower - 1);
                }
                else
                {
                    profile.DefensePower = Math.Max(1, profile.DefensePower - 1);
                }
            }

            profile.LastRemovalTime = DateTime.UtcNow;
            profile.RemovalsInLast24Hours++;

            // Apply points deduction (exactly 5) to the dismissed other player
            for (int i = 0; i < 5; i++)
            {
                if (other.EnergyMax >= other.AttackPower && other.EnergyMax >= other.DefensePower)
                {
                    other.EnergyMax = Math.Max(10, other.EnergyMax - 1);
                    if (other.EnergyCurrent > other.EnergyMax)
                    {
                        other.EnergyCurrent = other.EnergyMax;
                    }
                }
                else if (other.AttackPower >= other.EnergyMax && other.AttackPower >= other.DefensePower)
                {
                    other.AttackPower = Math.Max(1, other.AttackPower - 1);
                }
                else
                {
                    other.DefensePower = Math.Max(1, other.DefensePower - 1);
                }
            }

            _dbContext.SaveChanges();

            string penaltyAlert = wasSubsequentRemovalPenaltyApplied 
                ? "⚠️ 24-HOUR DOUBLE DISMISSAL PENALTY ENFORCED: 6 S.H.I.E.L.D. points subtracted from parameters!"
                : "5 S.H.I.E.L.D. points subtracted from parameters.";

            return Ok(new { 
                success = true, 
                message = $"Dismissal completed. Agent {other.Nickname} has been dismissed from your S.H.I.E.L.D. Team. {penaltyAlert}" 
            });
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

            var replacements = new Dictionary<string, string>
            {
                { "currentPlayerName", currentPlayer.Nickname },
                { "currentPlayerLevel", currentPlayer.Level.ToString() },
                { "currentPlayerSilver", currentPlayer.SilverBalance.ToString("N0") },
                { "currentPlayerAttackPower", $"{currentPlayer.AttackPowerCurrent}/{currentPlayer.AttackPower}" },
                { "currentPlayerDefensePower", $"{currentPlayer.DefensePowerCurrent}/{currentPlayer.DefensePower}" },
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

            var replacements = new Dictionary<string, string>
            {
                { "currentPlayerId", currentPlayer.Id.ToString() },
                { "currentPlayerName", currentPlayer.Nickname },
                { "currentPlayerLevel", currentPlayer.Level.ToString() },
                { "currentPlayerAttackPower", currentPlayer.AttackPowerCurrent.ToString() },
                { "currentPlayerAttackPowerMax", currentPlayer.AttackPower.ToString() },
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
