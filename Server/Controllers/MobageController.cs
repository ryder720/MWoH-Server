using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MwohServer.Services;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using MwohServer.Models;

namespace MwohServer.Controllers
{
    [ApiController]
    [Route("")]
    public class MobageController : ControllerBase
    {
        private readonly ILogger<MobageController> _logger;
        private readonly IAuthService _authService;

        public MobageController(ILogger<MobageController> logger, IAuthService authService)
        {
            _logger = logger;
            _authService = authService;
        }

        // 1. Stub Mobage SDK Social API: People `@me`
        [HttpGet("social/api/rest/people/@me/@self")]
        [HttpGet("social/api/rest/people/{userId}/@self")]
        public IActionResult GetPerson(string? userId = null)
        {
            _logger.LogInformation($"[Mobage] GetPerson called for userId: {userId ?? "@me"}");

            var response = new
            {
                entry = new
                {
                    id = userId ?? "123456",
                    displayName = "TestAgent",
                    nickname = "TestAgent",
                    aboutMe = "Marvel: War of Heroes Private Server Agent",
                    age = 25,
                    gender = "male",
                    thumbnailUrl = "http://10.0.2.2:5000/assets/default_avatar.png",
                    hasApp = true
                }
            };

            return Ok(response);
        }

        // 2. Catch-all for other Mobage Social REST APIs to ensure no 404 SDK errors
        [HttpGet("social/api/rest/{*url}")]
        [HttpPost("social/api/rest/{*url}")]
        public IActionResult SocialRestFallback(string url)
        {
            _logger.LogInformation($"[Mobage] Social REST unhandled call: {Request.Method} /social/api/rest/{url}");
            
            var response = new
            {
                entry = new { },
                success = true
            };
            return Ok(response);
        }

        // 3. Stub Mobage Billing API Catch-all
        [HttpGet("bank/api/rest/{*url}")]
        [HttpPost("bank/api/rest/{*url}")]
        public IActionResult BankRestFallback(string url)
        {
            _logger.LogInformation($"[Mobage] Bank REST unhandled call: {Request.Method} /bank/api/rest/{url}");
            
            var response = new
            {
                balance = 999999,
                success = true
            };
            return Ok(response);
        }

        // 4. Serves the login page (HTML/CSS) inside WebView when SDK requires login
        [HttpGet("")]
        [HttpGet("login/{*wildcard}")]
        [HttpGet("index.html")]
        public IActionResult ServeLoginPage([FromQuery] string? oauth_token = null)
        {
            _logger.LogInformation("[Mobage] Serving WebView Login/Registration Page.");

            var html = $@"
<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>S.H.I.E.L.D. Terminal | MWoH Server</title>
    <link href='https://fonts.googleapis.com/css2?family=Outfit:wght@300;400;600;800&display=swap' rel='stylesheet'>
    <style>
        :root {{
            --bg-color: #080c14;
            --panel-bg: rgba(13, 20, 35, 0.75);
            --border-color: rgba(226, 32, 44, 0.3);
            --border-focus: #e2202c;
            --text-color: #e2e8f0;
            --accent-gold: #f59e0b;
            --accent-red: #ef4444;
            --glass-reflection: linear-gradient(135deg, rgba(255,255,255,0.05) 0%, rgba(255,255,255,0) 100%);
        }}

        * {{
            box-sizing: border-box;
            margin: 0;
            padding: 0;
        }}

        body {{
            background-color: var(--bg-color);
            background-image: 
                radial-gradient(at 0% 0%, rgba(226, 32, 44, 0.15) 0px, transparent 50%),
                radial-gradient(at 100% 100%, rgba(245, 158, 11, 0.1) 0px, transparent 50%);
            color: var(--text-color);
            font-family: 'Outfit', sans-serif;
            min-height: 100vh;
            display: flex;
            justify-content: center;
            align-items: center;
            overflow-x: hidden;
            padding: 20px;
        }}

        .container {{
            width: 100%;
            max-width: 420px;
            background: var(--panel-bg);
            border: 1px solid var(--border-color);
            border-radius: 16px;
            padding: 35px 30px;
            backdrop-filter: blur(16px);
            box-shadow: 0 20px 50px rgba(0, 0, 0, 0.5), inset 0 1px 0 rgba(255, 255, 255, 0.05);
            position: relative;
            animation: fadeIn 0.8s ease-out;
        }}

        .container::before {{
            content: '';
            position: absolute;
            top: -2px; left: -2px; right: -2px; bottom: -2px;
            background: linear-gradient(135deg, var(--accent-red), var(--accent-gold));
            border-radius: 18px;
            z-index: -1;
            opacity: 0.15;
            filter: blur(10px);
        }}

        .header {{
            text-align: center;
            margin-bottom: 30px;
        }}

        .header h1 {{
            font-size: 26px;
            font-weight: 800;
            text-transform: uppercase;
            letter-spacing: 2px;
            background: linear-gradient(135deg, #ffffff 30%, #a5b4fc 100%);
            -webkit-background-clip: text;
            -webkit-text-fill-color: transparent;
            margin-bottom: 8px;
        }}

        .header p {{
            font-size: 13px;
            color: #94a3b8;
            letter-spacing: 0.5px;
        }}

        .tabs {{
            display: flex;
            background: rgba(0, 0, 0, 0.3);
            border-radius: 8px;
            margin-bottom: 25px;
            padding: 4px;
            border: 1px solid rgba(255, 255, 255, 0.05);
        }}

        .tab-btn {{
            flex: 1;
            background: none;
            border: none;
            color: #64748b;
            padding: 10px;
            font-size: 14px;
            font-weight: 600;
            cursor: pointer;
            border-radius: 6px;
            transition: all 0.3s ease;
        }}

        .tab-btn.active {{
            background: var(--accent-red);
            color: #ffffff;
            box-shadow: 0 4px 12px rgba(226, 32, 44, 0.3);
        }}

        .form-group {{
            margin-bottom: 20px;
            position: relative;
        }}

        .form-group label {{
            display: block;
            font-size: 12px;
            font-weight: 600;
            text-transform: uppercase;
            letter-spacing: 1px;
            color: #94a3b8;
            margin-bottom: 8px;
        }}

        .form-group input {{
            width: 100%;
            background: rgba(0, 0, 0, 0.4);
            border: 1px solid rgba(255, 255, 255, 0.1);
            border-radius: 8px;
            padding: 12px 16px;
            color: #ffffff;
            font-size: 15px;
            font-family: inherit;
            transition: all 0.3s ease;
        }}

        .form-group input:focus {{
            outline: none;
            border-color: var(--border-focus);
            box-shadow: 0 0 10px rgba(226, 32, 44, 0.2);
            background: rgba(0, 0, 0, 0.6);
        }}

        .submit-btn {{
            width: 100%;
            background: linear-gradient(135deg, var(--accent-red), #b91c1c);
            border: none;
            border-radius: 8px;
            padding: 14px;
            color: #ffffff;
            font-size: 15px;
            font-weight: 700;
            text-transform: uppercase;
            letter-spacing: 1px;
            cursor: pointer;
            transition: all 0.3s ease;
            box-shadow: 0 4px 15px rgba(226, 32, 44, 0.3);
            margin-top: 10px;
        }}

        .submit-btn:hover {{
            transform: translateY(-2px);
            box-shadow: 0 6px 20px rgba(226, 32, 44, 0.5);
            background: linear-gradient(135deg, #ef4444, var(--accent-red));
        }}

        .submit-btn:active {{
            transform: translateY(0);
        }}

        .info-box {{
            margin-top: 25px;
            padding: 12px 15px;
            border-radius: 8px;
            background: rgba(245, 158, 11, 0.05);
            border: 1px solid rgba(245, 158, 11, 0.15);
            font-size: 12px;
            color: var(--accent-gold);
            text-align: center;
            line-height: 1.5;
        }}

        @keyframes fadeIn {{
            from {{ opacity: 0; transform: translateY(15px); }}
            to {{ opacity: 1; transform: translateY(0); }}
        }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>S.H.I.E.L.D. Access</h1>
            <p>MWoH Private Server Authentication</p>
        </div>

        <div class='tabs'>
            <button type='button' class='tab-btn active' onclick='switchTab(""login"")'>Sign In</button>
            <button type='button' class='tab-btn' onclick='switchTab(""register"")'>Register</button>
        </div>

        <form id='authForm' method='POST' action='/mobage/login'>
            <input type='hidden' name='oauth_token' value='{oauth_token ?? "test_token_12345"}'>
            
            <div class='form-group'>
                <label for='username'>Agent Username</label>
                <input type='text' id='username' name='username' required placeholder='e.g. testuser'>
            </div>

            <div class='form-group'>
                <label for='password'>Security Keyphrase</label>
                <input type='password' id='password' name='password' required placeholder='••••••••'>
            </div>

            <button type='submit' id='submitBtn' class='submit-btn'>Initialize Link</button>
        </form>

        <div class='info-box'>
            &#128737; <strong>Note:</strong> A default account <code>testuser</code> with password <code>password</code> is preloaded for rapid local access.
        </div>
    </div>

    <script>
        function switchTab(mode) {{
            const form = document.getElementById('authForm');
            const submitBtn = document.getElementById('submitBtn');
            const tabs = document.querySelectorAll('.tab-btn');
            
            if (mode === 'login') {{
                form.action = '/mobage/login';
                submitBtn.textContent = 'Initialize Link';
                tabs[0].classList.add('active');
                tabs[1].classList.remove('active');
            }} else {{
                form.action = '/mobage/register';
                submitBtn.textContent = 'Create Profile';
                tabs[0].classList.remove('active');
                tabs[1].classList.add('active');
            }}
        }}
    </script>
</body>
</html>
";

            return Content(html, "text/html");
        }

        // 5. Action to handle Sign In post back
        [HttpPost("mobage/login")]
        [Consumes("application/x-www-form-urlencoded")]
        public IActionResult HandleLogin([FromForm] string username, [FromForm] string password, [FromForm] string oauth_token)
        {
            _logger.LogInformation($"[Mobage] WebView Login Attempt for: {username}");

            var user = _authService.ValidateUser(username, password);
            if (user == null)
            {
                var errorHtml = GetStatusHtml("ACCESS DENIED", "Invalid credential signature. Please double-check your agent identity and try again.", "/login", "Retry Authentication", true);
                return Content(errorHtml, "text/html");
            }

            _authService.GenerateToken(user);
            string sessionId = _authService.GenerateSession(user);

            // Set the critical "sid" Cookie in response for game WebView mapping
            Response.Cookies.Append("sid", sessionId, new Microsoft.AspNetCore.Http.CookieOptions
            {
                Path = "/",
                HttpOnly = false, // Must be readable by client web view CookieSync
                Secure = false
            });

            var callbackUrl = $"/callback?oauth_verifier=test_verifier_12345&oauth_token={user.ActiveToken}";
            return Redirect(callbackUrl);
        }

        // 6. Action to handle Registration post back
        [HttpPost("mobage/register")]
        [Consumes("application/x-www-form-urlencoded")]
        public IActionResult HandleRegister([FromForm] string username, [FromForm] string password, [FromForm] string oauth_token)
        {
            _logger.LogInformation($"[Mobage] WebView Registration Attempt for: {username}");

            if (username.Length < 3 || password.Length < 4)
            {
                var errorHtml = GetStatusHtml("REGISTRATION FAILED", "Username must be at least 3 chars and keyphrase at least 4 chars.", "/login", "Try Again", true);
                return Content(errorHtml, "text/html");
            }

            try
            {
                var user = _authService.RegisterUser(username, password);
                _authService.GenerateToken(user);
                string sessionId = _authService.GenerateSession(user);

                // Set the critical "sid" Cookie in response for game WebView mapping
                Response.Cookies.Append("sid", sessionId, new Microsoft.AspNetCore.Http.CookieOptions
                {
                    Path = "/",
                    HttpOnly = false, // Must be readable by client web view CookieSync
                    Secure = false
                });
                
                var callbackUrl = $"/callback?oauth_verifier=test_verifier_12345&oauth_token={user.ActiveToken}";
                return Redirect(callbackUrl);
            }
            catch (Exception ex)
            {
                var errorHtml = GetStatusHtml("IDENTITY CONFLICT", ex.Message, "/login", "Try Again", true);
                return Content(errorHtml, "text/html");
            }
        }

        // 7. Serves callback completion screen
        [HttpGet("callback")]
        public IActionResult ServeCallbackPage([FromQuery] string oauth_verifier, [FromQuery] string oauth_token)
        {
            _logger.LogInformation($"[Mobage] Callback loading. Verifier: {oauth_verifier}, Token: {oauth_token}");

            var user = _authService.GetUserByToken(oauth_token);
            if (user == null)
            {
                var errorHtml = GetStatusHtml(
                    "LINK FAILURE",
                    "Could not retrieve secure profile associated with this authorization token. Please try again.",
                    "/login",
                    "Retry Authentication",
                    true
                );
                return Content(errorHtml, "text/html");
            }

            string sessionId = user.Profile?.SessionId ?? _authService.GenerateSession(user);
            
            // Set the critical "sid" Cookie in response for game WebView mapping
            Response.Cookies.Append("sid", sessionId, new Microsoft.AspNetCore.Http.CookieOptions
            {
                Path = "/",
                HttpOnly = false,
                Secure = false
            });

            // Extract all credential values for native seed bridge
            string userId = user.Profile?.PlayerIdString ?? "123456";
            string userNickname = user.Profile?.Nickname ?? user.Username;
            string authToken = user.ActiveToken ?? oauth_token;
            string oauthToken = user.ActiveToken ?? oauth_token;
            string oauthSecret = "J7NgeGzFn6bjkuTm3pWh2cwm6EOgg"; // Cryptographic secret for signature verification
            string oauth2Token = user.ActiveToken ?? oauth_token;
            string guestNickname = user.Username;
            string guestPassword = user.PasswordHash;
            string cookie = $"sid={sessionId}";

            var successHtml = $@"
<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Link Secured | S.H.I.E.L.D. Secure Terminal</title>
    <link href='https://fonts.googleapis.com/css2?family=Outfit:wght@300;400;600;800&display=swap' rel='stylesheet'>
    <style>
        :root {{
            --bg-color: #080c14;
            --panel-bg: rgba(13, 20, 35, 0.75);
            --border-color: rgba(245, 158, 11, 0.3);
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
                radial-gradient(at 50% 0%, rgba(245, 158, 11, 0.15) 0px, transparent 50%);
            color: var(--text-color);
            font-family: 'Outfit', sans-serif;
            min-height: 100vh;
            display: flex;
            justify-content: center;
            align-items: center;
            padding: 20px;
        }}

        .container {{
            width: 100%;
            max-width: 420px;
            background: var(--panel-bg);
            border: 1px solid var(--border-color);
            border-radius: 16px;
            padding: 40px 30px;
            backdrop-filter: blur(16px);
            box-shadow: 0 20px 50px rgba(0, 0, 0, 0.6);
            text-align: center;
            animation: scaleUp 0.5s cubic-bezier(0.16, 1, 0.3, 1);
        }}

        .icon {{
            font-size: 48px;
            color: var(--accent-gold);
            margin-bottom: 20px;
            display: inline-block;
        }}

        h1 {{
            font-size: 24px;
            font-weight: 800;
            text-transform: uppercase;
            letter-spacing: 2px;
            color: #ffffff;
            margin-bottom: 15px;
        }}

        p {{
            font-size: 14px;
            color: #94a3b8;
            line-height: 1.6;
            margin-bottom: 30px;
        }}

        .action-btn {{
            display: block;
            width: 100%;
            background: var(--accent-gold);
            color: #ffffff;
            text-decoration: none;
            padding: 14px;
            border-radius: 8px;
            font-weight: 700;
            text-transform: uppercase;
            font-size: 14px;
            letter-spacing: 1px;
            box-shadow: 0 4px 15px rgba(0, 0, 0, 0.3);
            transition: all 0.3s ease;
        }}

        .action-btn:hover {{
            transform: translateY(-2px);
            box-shadow: 0 6px 20px rgba(255, 255, 255, 0.1);
        }}

        @keyframes scaleUp {{
            from {{ opacity: 0; transform: scale(0.95); }}
            to {{ opacity: 1; transform: scale(1); }}
        }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='icon'>&#128274;</div>
        <h1>LINK SECURED</h1>
        <p>Your device token has been cryptographically signed. The S.H.I.E.L.D. secure node is fully synchronized.<br><br>You can now return to the game client.</p>
        <a href='ngcore://closeWebView?status=success' class='action-btn'>Return to Game Client</a>
    </div>

    <script>
        function triggerBridgeCall(url) {{
            var iframe = document.createElement(""iframe"");
            iframe.setAttribute(""src"", url);
            iframe.style.display = ""none"";
            document.body.appendChild(iframe);
            setTimeout(function() {{
                iframe.parentNode.removeChild(iframe);
            }}, 500);
        }}

        var saved = false;
        window.respondToWeb = function(callbackId, status, data) {{
            console.log(""respondToWeb callback: "" + callbackId);
            if (callbackId === ""saveAuth"") {{
                saved = true;
                setTimeout(function() {{
                    window.location.href = ""ngcore://closeWebView?status=success"";
                }}, 100);
            }}
        }};

        // Trigger native Mobage SDK credentials saving bridge call
        var bridgeUrl = ""ngcore://saveAuthCredential/saveAuth"" + 
            ""?userId="" + encodeURIComponent(""{userId}"") +
            ""&userNickname="" + encodeURIComponent(""{userNickname}"") +
            ""&authToken="" + encodeURIComponent(""{authToken}"") +
            ""&oauthToken="" + encodeURIComponent(""{oauthToken}"") +
            ""&oauthSecret="" + encodeURIComponent(""{oauthSecret}"") +
            ""&oauth2Token="" + encodeURIComponent(""{oauth2Token}"") +
            ""&guestNickname="" + encodeURIComponent(""{guestNickname}"") +
            ""&guestPassword="" + encodeURIComponent(""{guestPassword}"") +
            ""&cookie="" + encodeURIComponent(""{cookie}"");

        setTimeout(function() {{
            triggerBridgeCall(bridgeUrl);
        }}, 100);

        // Fallback timeout to close WebView in case native side fails or doesn't invoke callback
        setTimeout(function() {{
            if (!saved) {{
                console.log(""saveAuth callback not received, closing via timeout."");
                window.location.href = ""ngcore://closeWebView?status=success"";
            }}
        }}, 1500);
    </script>
</body>
</html>
";

            return Content(successHtml, "text/html");
        }

        // 8. Stub Mobage OAuth Token Authorization Endpoint
        [HttpPost("1/{appKey}/oauth/authorize")]
        public IActionResult AuthorizeToken(string appKey, [FromQuery] string oauth_token, [FromQuery] string? authorize = null)
        {
            _logger.LogInformation($"[Mobage] AuthorizeToken called. AppKey: {appKey}, Token: {oauth_token}, Authorize: {authorize}");

            var user = ResolveMobageUser();
            if (user != null)
            {
                _authService.MapTemporaryToken(oauth_token, user.Username);
                _logger.LogInformation($"[Mobage] Mapped temporary token '{oauth_token}' to user '{user.Username}'");
            }

            var response = new
            {
                success = true,
                oauth_verifier = "test_verifier_12345"
            };

            return Ok(response);
        }

        // 9. OpenSocial endpoint for retrieving the active user profile
        [HttpGet("1/{appKey}/opensocial/people/@me/@self")]
        [HttpGet("1/{appKey}/opensocial/people/{userId}/@self")]
        public IActionResult GetOpenSocialPerson(string appKey, string? userId = null)
        {
            _logger.LogInformation($"[Mobage] GetOpenSocialPerson called. AppKey: {appKey}, userId: {userId ?? "@me"}");

            var user = ResolveMobageUser();

            var response = new
            {
                entry = new
                {
                    id = user.Profile?.PlayerIdString ?? "123456",
                    user_id = user.Profile?.PlayerIdString ?? "123456",
                    userId = user.Profile?.PlayerIdString ?? "123456",
                    displayName = user.Profile?.Nickname ?? user.Username,
                    nickname = user.Profile?.Nickname ?? user.Username,
                    aboutMe = "Marvel: War of Heroes Private Server Agent",
                    age = 25,
                    gender = "male",
                    thumbnailUrl = "http://10.0.2.2:5000/assets/default_avatar.png",
                    hasApp = true,
                    ageRestricted = false,
                    isFamous = false,
                    mutualFriends = "",
                    pendingFriendRequests = "",
                    addresses = "",
                    birthday = "",
                    gamertag = user.Username
                }
            };

            return Ok(response);
        }

        private string? ParseMobageOAuthToken()
        {
            if (Request.Headers.TryGetValue("Authorization", out var authHeaderValues))
            {
                var header = authHeaderValues.ToString();
                if (header.StartsWith("OAuth ", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = header.Substring(6).Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var part in parts)
                    {
                        var kv = part.Split(new[] { '=' }, 2);
                        if (kv.Length == 2)
                        {
                            var key = kv[0].Trim();
                            var val = kv[1].Trim().Trim('"');
                            if (key == "oauth_token")
                            {
                                return val;
                            }
                        }
                    }
                }
            }
            return null;
        }

        private UserAccount ResolveMobageUser()
        {
            var user = (UserAccount?)null;
            
            // 1. Try OAuth Token from Authorization Header
            var oauthToken = ParseMobageOAuthToken();
            if (!string.IsNullOrEmpty(oauthToken))
            {
                user = _authService.GetUserByToken(oauthToken);
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

        // 10. OpenSocial endpoint for AppData stubs (arbitrary client-side storage)
        [HttpGet("1/{appKey}/opensocial/appdata/@me/@self/@app")]
        [HttpPost("1/{appKey}/opensocial/appdata/@me/@self/@app")]
        public IActionResult HandleAppData(string appKey)
        {
            _logger.LogInformation($"[Mobage] HandleAppData called. AppKey: {appKey}, Method: {Request.Method}");
            
            var response = new
            {
                entry = new System.Collections.Generic.Dictionary<string, object>
                {
                    { "123456", new System.Collections.Generic.Dictionary<string, string>() }
                },
                success = true
            };
            return Ok(response);
        }

        // 11. OpenSocial Fallback & Catch-All for Leaderboards, Push Notifications, Bad Words, etc.
        [HttpGet("1/{appKey}/opensocial/{*url}")]
        [HttpPost("1/{appKey}/opensocial/{*url}")]
        [HttpPut("1/{appKey}/opensocial/{*url}")]
        [HttpDelete("1/{appKey}/opensocial/{*url}")]
        public IActionResult OpenSocialFallback(string appKey, string url)
        {
            _logger.LogInformation($"[Mobage] OpenSocial Fallback invoked: {Request.Method} /1/{appKey}/opensocial/{url}");
            
            var response = new
            {
                entry = new { },
                success = true
            };
            return Ok(response);
        }

        // Stub Mobage Bank Balance Inquiry
        [HttpGet("1/{appKey}/bank/balance")]
        public IActionResult GetBankBalance(string appKey)
        {
            var user = ResolveMobageUser();
            var balance = user.Profile?.MobaCoinBalance ?? 999999;
            
            _logger.LogInformation($"[Mobage] GetBankBalance called. AppKey: {appKey}, User: {user.Username}, Balance: {balance}");

            var response = new
            {
                balance = balance
            };
            return Ok(response);
        }

        // Stub Mobage Telemetry / Analytics Bulk Statistics Ingest
        [HttpPost("pipes/r.2/bulk_record_stats")]
        public IActionResult BulkRecordStats()
        {
            // Swallow Ngpipes telemetry stats silently to keep the logs clean
            return Ok(new { success = true });
        }

        // Stub browser favicon requests to avoid 404 console entries
        [HttpGet("favicon.ico")]
        public IActionResult Favicon()
        {
            return NoContent(); // HTTP 204 No Content
        }

        // 12. Native Mobage SDK Session Establishment & Persistence Endpoint
        [HttpPost("1/{appKey}/session")]
        public async Task<IActionResult> ReestablishSession(string appKey)
        {
            // 1. Read raw JSON body from request stream
            using var reader = new StreamReader(Request.Body);
            var bodyText = await reader.ReadToEndAsync();
            _logger.LogInformation($"[Mobage] ReestablishSession called. AppKey: {appKey}, Body: {bodyText}");

            string? gamertag = null;
            string? password = null;
            string? authToken = null;

            // 2. Parse incoming JSON to identify credentials or auth_token
            if (!string.IsNullOrEmpty(bodyText))
            {
                try
                {
                    using var jsonDoc = JsonDocument.Parse(bodyText);
                    var root = jsonDoc.RootElement;
                    if (root.TryGetProperty("gamertag", out var gtProp))
                    {
                        gamertag = gtProp.GetString();
                    }
                    if (root.TryGetProperty("password", out var pwdProp))
                    {
                        password = pwdProp.GetString();
                    }
                    if (root.TryGetProperty("auth_token", out var authProp))
                    {
                        authToken = authProp.GetString();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[Mobage] Error parsing session JSON payload.");
                }
            }

            // 3. Look up user account
            var user = (UserAccount?)null;
            if (!string.IsNullOrEmpty(authToken))
            {
                user = _authService.GetUserByToken(authToken);
            }
            else if (!string.IsNullOrEmpty(gamertag) && !string.IsNullOrEmpty(password))
            {
                user = _authService.ValidateUser(gamertag, password);
            }

            // Fallback to pre-seeded testuser in development if session is missing
            if (user == null)
            {
                _logger.LogWarning("[Mobage] Session re-establishment lookup failed. Falling back to testuser.");
                user = _authService.ValidateUser("testuser", "password");
            }

            if (user != null)
            {
                // Ensure active token and session ID are generated
                if (string.IsNullOrEmpty(user.ActiveToken))
                {
                    _authService.GenerateToken(user);
                }

                if (user.Profile == null || string.IsNullOrEmpty(user.Profile.SessionId))
                {
                    _authService.GenerateSession(user);
                }

                // Set "sid" cookie in case native SDK syncs cookies with WebView
                if (user.Profile != null)
                {
                    Response.Cookies.Append("sid", user.Profile.SessionId, new Microsoft.AspNetCore.Http.CookieOptions
                    {
                        Path = "/",
                        HttpOnly = false,
                        Secure = false
                    });
                }

                // 4. Return standard cryptographic token payload expected by Mobage SDK
                var response = new
                {
                    auth_token = user.ActiveToken,
                    oauth_token = user.ActiveToken,
                    oauth_secret = "J7NgeGzFn6bjkuTm3pWh2cwm6EOgg", // Hardcoded token secret matching GAuthValidationFilter
                    oauth2_token = user.ActiveToken
                };

                _logger.LogInformation($"[Mobage] Session re-established for user '{user.Username}'. Token: {user.ActiveToken}");
                return Ok(response);
            }

            return Unauthorized();
        }

        private string GetStatusHtml(string title, string message, string actionUrl, string buttonText, bool isError)
        {
            var accentColor = isError ? "var(--accent-red)" : "var(--accent-gold)";
            var buttonHtml = string.IsNullOrEmpty(actionUrl) 
                ? "<div class='status-success-badge'>&#10003; ACTIVE SESSION established</div>"
                : $"<a href='{actionUrl}' class='action-btn'>{buttonText}</a>";

            var iconHtml = isError ? "&#9888;" : "&#128274;";

            var redirectScript = "";
            if (!isError)
            {
                redirectScript = @"
    <script>
        setTimeout(function() {
            window.location.href = ""ngcore://closeWebView?status=success"";
        }, 1500);
    </script>";
            }

            return $@"
<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>{title} | S.H.I.E.L.D. Secure Terminal</title>
    <link href='https://fonts.googleapis.com/css2?family=Outfit:wght@300;400;600;800&display=swap' rel='stylesheet'>
    <style>
        :root {{
            --bg-color: #080c14;
            --panel-bg: rgba(13, 20, 35, 0.75);
            --border-color: {(isError ? "rgba(239, 68, 68, 0.3)" : "rgba(245, 158, 11, 0.3)")};
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
                radial-gradient(at 50% 0%, {(isError ? "rgba(239, 68, 68, 0.15)" : "rgba(245, 158, 11, 0.15)")} 0px, transparent 50%);
            color: var(--text-color);
            font-family: 'Outfit', sans-serif;
            min-height: 100vh;
            display: flex;
            justify-content: center;
            align-items: center;
            padding: 20px;
        }}

        .container {{
            width: 100%;
            max-width: 420px;
            background: var(--panel-bg);
            border: 1px solid var(--border-color);
            border-radius: 16px;
            padding: 40px 30px;
            backdrop-filter: blur(16px);
            box-shadow: 0 20px 50px rgba(0, 0, 0, 0.6);
            text-align: center;
            animation: scaleUp 0.5s cubic-bezier(0.16, 1, 0.3, 1);
        }}

        .icon {{
            font-size: 48px;
            color: {accentColor};
            margin-bottom: 20px;
            display: inline-block;
        }}

        h1 {{
            font-size: 24px;
            font-weight: 800;
            text-transform: uppercase;
            letter-spacing: 2px;
            color: #ffffff;
            margin-bottom: 15px;
        }}

        p {{
            font-size: 14px;
            color: #94a3b8;
            line-height: 1.6;
            margin-bottom: 30px;
        }}

        .action-btn {{
            display: block;
            width: 100%;
            background: {accentColor};
            color: #ffffff;
            text-decoration: none;
            padding: 14px;
            border-radius: 8px;
            font-weight: 700;
            text-transform: uppercase;
            font-size: 14px;
            letter-spacing: 1px;
            box-shadow: 0 4px 15px rgba(0, 0, 0, 0.3);
            transition: all 0.3s ease;
        }}

        .action-btn:hover {{
            transform: translateY(-2px);
            box-shadow: 0 6px 20px rgba(255, 255, 255, 0.1);
        }}

        .status-success-badge {{
            padding: 12px;
            background: rgba(16, 185, 129, 0.1);
            border: 1px solid rgba(16, 185, 129, 0.3);
            color: #34d399;
            border-radius: 8px;
            font-weight: 600;
            font-size: 14px;
            letter-spacing: 0.5px;
            text-transform: uppercase;
        }}

        @keyframes scaleUp {{
            from {{ opacity: 0; transform: scale(0.95); }}
            to {{ opacity: 1; transform: scale(1); }}
        }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='icon'>{iconHtml}</div>
        <h1>{title}</h1>
        <p>{message}</p>
        {buttonHtml}
    </div>
    {redirectScript}
</body>
</html>
";
        }
    }
}
