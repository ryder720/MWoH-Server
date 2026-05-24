using MwohServer.Models;
using System.Collections.Generic;

namespace MwohServer.Services
{
    public interface IDeckManager
    {
        DeckSyncResult SyncDeck(int profileId, string mode, List<int> cardIds);
    }

    public class DeckSyncResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
