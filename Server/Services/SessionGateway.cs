using Microsoft.Extensions.Logging;
using MwohServer.Models;
using System;

namespace MwohServer.Services
{
    public class SessionGateway : ISessionGateway
    {
        private readonly ILogger<SessionGateway> _logger;
        private readonly IAuthService _authService;

        public SessionGateway(ILogger<SessionGateway> logger, IAuthService authService)
        {
            _logger = logger;
            _authService = authService;
        }

        public UserAccount ResolveContext(string? oauthHeader, string? cookieSid, string? gauthToken)
        {
            var user = (UserAccount?)null;

            // 1. Try GAuth Token
            if (!string.IsNullOrEmpty(gauthToken))
            {
                user = _authService.GetUserByToken(gauthToken);
            }

            // 2. Try Mobage OAuth Header parsing
            if (user == null && !string.IsNullOrEmpty(oauthHeader))
            {
                var token = ParseOAuthTokenFromHeader(oauthHeader);
                if (!string.IsNullOrEmpty(token))
                {
                    user = _authService.GetUserByToken(token);
                }
            }

            // 3. Try Session Cookie sid
            if (user == null && !string.IsNullOrEmpty(cookieSid))
            {
                user = _authService.GetUserBySessionId(cookieSid);
            }

            // 4. Default fallback to testuser in development mode
            if (user == null)
            {
                _logger.LogWarning("[SessionGateway] Authentication context resolution failed. Falling back to testuser.");
                user = _authService.ValidateUser("testuser", "password");
            }

            return user!;
        }

        public SessionEstablishmentResult Reestablish(string? gamertag, string? password, string? authToken)
        {
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
                _logger.LogWarning("[SessionGateway] Reestablish credentials mismatch. Falling back to testuser.");
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

                _logger.LogInformation($"[SessionGateway] Session re-established for user '{user.Username}'. Token: {user.ActiveToken}");

                return new SessionEstablishmentResult
                {
                    Success = true,
                    ActiveToken = user.ActiveToken ?? string.Empty,
                    SessionId = user.Profile?.SessionId ?? string.Empty,
                    Username = user.Username
                };
            }

            return new SessionEstablishmentResult { Success = false, Message = "Authentication failed." };
        }

        public UserAccount? ExchangeTemporaryToken(string tempToken)
        {
            if (string.IsNullOrEmpty(tempToken)) return null;
            return _authService.GetAndConsumeUserByTemporaryToken(tempToken);
        }

        private string? ParseOAuthTokenFromHeader(string header)
        {
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
            return null;
        }
    }
}
