using MwohServer.Models;
using System.Collections.Generic;

namespace MwohServer.Services
{
    public interface IItemLedger
    {
        List<PlayerInventoryItem> GetInventory(int profileId);
        ItemUseResult UseItem(int profileId, int itemId, int targetCardId = 0);
    }

    public class ItemUseResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int RemainingQuantity { get; set; }
        public int Level { get; set; }
        public int EnergyMax { get; set; }
        public int EnergyCurrent { get; set; }
        public int AttackPowerCurrent { get; set; }
        public int AttackPowerMax { get; set; }
        public int DefensePowerCurrent { get; set; }
        public int DefensePowerMax { get; set; }
        public long Silver { get; set; }
        public long MobaCoin { get; set; }
        public object? UpdatedCard { get; set; }
    }
}
