using MwohServer.Models;

namespace MwohServer.Services
{
    public interface IAuthService
    {
        UserAccount? ValidateUser(string username, string password);
        UserAccount? GetUserByToken(string token);
        UserAccount? GetUserBySessionId(string sessionId);
        string GenerateToken(UserAccount account);
        string GenerateSession(UserAccount account);
        UserAccount RegisterUser(string username, string password);
        void MapTemporaryToken(string tempToken, string username);
        UserAccount? GetAndConsumeUserByTemporaryToken(string tempToken);
        UserAccount ResolveContext(string? oauthHeader, string? cookieSid, string? gauthToken);
        SessionEstablishmentResult Reestablish(string? gamertag, string? password, string? authToken);
    }

    public class SessionEstablishmentResult
    {
        public bool Success { get; set; }
        public string ActiveToken { get; set; } = string.Empty;
        public string SessionId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
