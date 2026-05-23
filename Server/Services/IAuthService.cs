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
    }
}
