using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using MwohServer.Data;
using MwohServer.Models;

namespace MwohServer.Services
{
    public class AuthService : IAuthService
    {
        private readonly MwohDbContext _dbContext;

        public AuthService(MwohDbContext dbContext)
        {
            _dbContext = dbContext;
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
                    SessionId = sessionId
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
                SessionId = ""
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
    }
}
