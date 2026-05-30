using MwohServer.Models;
using System.Collections.Generic;

namespace MwohServer.Services
{
    public interface IShopEngine
    {
        List<ShopPackage> GetShopPackages();
        ShopPurchaseResult PurchasePackage(int profileId, string packageId);
        void ReloadConfig();
    }

    public class ShopPackage
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty; // "MobaCoin" or "Item"
        public string ItemTemplateName { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public long SilverCost { get; set; }
        public bool IsFree { get; set; }
    }

    public class ShopPurchaseResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public long NewSilverBalance { get; set; }
        public long NewMobaCoinBalance { get; set; }
        public int AddedQuantity { get; set; }
        public string ItemName { get; set; } = string.Empty;
    }
}
