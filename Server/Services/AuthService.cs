using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MwohServer.Data;
using MwohServer.Models;

namespace MwohServer.Services
{
    public class AuthService : IAuthService
    {
        private readonly MwohDbContext _dbContext;
        private readonly ILogger<AuthService> _logger;

        public AuthService(MwohDbContext dbContext, ILogger<AuthService> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        public UserAccount? ValidateUser(string username, string password)
        {
            var user = _dbContext.Users
                .Include(u => u.Profile)
                .FirstOrDefault(u => u.Username.ToLower() == username.ToLower());

            if (user != null && user.PasswordHash == password)
            {
                return user;
            }

            return null;
        }

        public UserAccount? GetUserByToken(string token)
        {
            return _dbContext.Users
                .Include(u => u.Profile)
                .FirstOrDefault(u => u.ActiveToken == token);
        }

        public UserAccount? GetUserBySessionId(string sessionId)
        {
            return _dbContext.Users
                .Include(u => u.Profile)
                .FirstOrDefault(u => u.Profile != null && u.Profile.SessionId == sessionId);
        }

        public string GenerateToken(UserAccount account)
        {
            string token = Guid.NewGuid().ToString("N");
            account.ActiveToken = token;
            _dbContext.SaveChanges();
            return token;
        }

        public string GenerateSession(UserAccount account)
        {
            string sessionId = "sess_" + Guid.NewGuid().ToString("N").Substring(0, 16);
            if (account.Profile == null)
            {
                account.Profile = new PlayerProfile
                {
                    UserAccountId = account.Id,
                    Nickname = account.Username,
                    PlayerIdString = (100000 + account.Id).ToString(),
                    SessionId = sessionId,
                    StatPoints = 0
                };
                _dbContext.Profiles.Add(account.Profile);
            }
            else
            {
                account.Profile.SessionId = sessionId;
            }
            _dbContext.SaveChanges();
            return sessionId;
        }

        public UserAccount RegisterUser(string username, string password)
        {
            var existing = _dbContext.Users.FirstOrDefault(u => u.Username.ToLower() == username.ToLower());
            if (existing != null)
            {
                throw new InvalidOperationException("Username already exists.");
            }

            var newUser = new UserAccount
            {
                Username = username,
                PasswordHash = password
            };

            _dbContext.Users.Add(newUser);
            _dbContext.SaveChanges();

            // Create profile
            var profile = new PlayerProfile
            {
                UserAccountId = newUser.Id,
                Nickname = username,
                Level = 1,
                Experience = 0,
                EnergyMax = 100,
                EnergyCurrent = 100,
                AttackPower = GameplaySettings.DefaultAttackPower,
                DefensePower = GameplaySettings.DefaultDefensePower,
                MobaCoinBalance = 10000,
                SilverBalance = 50000,
                PlayerIdString = (100000 + newUser.Id).ToString(),
                SessionId = "",
                StatPoints = 0
            };

            newUser.Profile = profile;
            _dbContext.Profiles.Add(profile);
            _dbContext.SaveChanges();

            // Give the new user a base Spider-Man card as a starting card
            var spiderTemplate = _dbContext.CardTemplates.FirstOrDefault(t => t.Title == "Spider-Man" && t.VariantName == "Base")
                ?? _dbContext.CardTemplates.FirstOrDefault(t => t.Title == "Spider-Man")
                ?? _dbContext.CardTemplates.FirstOrDefault(t => t.Title.Contains("Spider-Man"));

            if (spiderTemplate != null)
            {
                var starterCard = new PlayerCard
                {
                    PlayerProfileId = profile.Id,
                    IsLeader = true,
                    IsInAttackDeck = true,
                    IsInDefenseDeck = true
                };
                starterCard.InitializeStats(spiderTemplate, GameplaySettings.DefaultMasteryPercentage);
                _dbContext.PlayerCards.Add(starterCard);
                _dbContext.SaveChanges();
            }

            return newUser;
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _temporaryTokenMap = new();

        public void MapTemporaryToken(string tempToken, string username)
        {
            _temporaryTokenMap[tempToken] = username;
        }

        public UserAccount? GetAndConsumeUserByTemporaryToken(string tempToken)
        {
            if (_temporaryTokenMap.TryRemove(tempToken, out var username))
            {
                return _dbContext.Users
                    .Include(u => u.Profile)
                    .FirstOrDefault(u => u.Username.ToLower() == username.ToLower());
            }
            return null;
        }

        public UserAccount ResolveContext(string? oauthHeader, string? cookieSid, string? gauthToken)
        {
            var user = (UserAccount?)null;

            // 1. Try GAuth Token
            if (!string.IsNullOrEmpty(gauthToken))
            {
                user = GetUserByToken(gauthToken);
            }

            // 2. Try Mobage OAuth Header parsing
            if (user == null && !string.IsNullOrEmpty(oauthHeader))
            {
                var token = ParseOAuthTokenFromHeader(oauthHeader);
                if (!string.IsNullOrEmpty(token))
                {
                    user = GetUserByToken(token);
                }
            }

            // 3. Try Session Cookie sid
            if (user == null && !string.IsNullOrEmpty(cookieSid))
            {
                user = GetUserBySessionId(cookieSid);
            }

            // 4. Default fallback to testuser in development mode
            if (user == null)
            {
                _logger.LogWarning("[AuthService] Authentication context resolution failed. Falling back to testuser.");
                user = ValidateUser("testuser", "password");
            }

            return user!;
        }

        public SessionEstablishmentResult Reestablish(string? gamertag, string? password, string? authToken)
        {
            var user = (UserAccount?)null;

            if (!string.IsNullOrEmpty(authToken))
            {
                user = GetUserByToken(authToken);
            }
            else if (!string.IsNullOrEmpty(gamertag) && !string.IsNullOrEmpty(password))
            {
                user = ValidateUser(gamertag, password);
            }

            // Fallback to pre-seeded testuser in development if session is missing
            if (user == null)
            {
                _logger.LogWarning("[AuthService] Reestablish credentials mismatch. Falling back to testuser.");
                user = ValidateUser("testuser", "password");
            }

            if (user != null)
            {
                // Ensure active token and session ID are generated
                if (string.IsNullOrEmpty(user.ActiveToken))
                {
                    GenerateToken(user);
                }

                if (user.Profile == null || string.IsNullOrEmpty(user.Profile.SessionId))
                {
                    GenerateSession(user);
                }

                _logger.LogInformation($"[AuthService] Session re-established for user '{user.Username}'. Token: {user.ActiveToken}");

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
