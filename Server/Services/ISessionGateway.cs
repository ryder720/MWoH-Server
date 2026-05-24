using MwohServer.Models;

namespace MwohServer.Services
{
    public interface ISessionGateway
    {
        UserAccount ResolveContext(string? oauthHeader, string? cookieSid, string? gauthToken);
        SessionEstablishmentResult Reestablish(string? gamertag, string? password, string? authToken);
        UserAccount? ExchangeTemporaryToken(string tempToken);
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
