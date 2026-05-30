using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using MwohServer.Data;
using MwohServer.Models;
using MwohServer.Services;

namespace MwohServer.Tests
{
    public static class ShopTests
    {
        public static bool Run(IShopEngine engine, MwohDbContext context)
        {
            Console.WriteLine("\n==================================================");
            Console.WriteLine("🧪 INITIALIZING S.H.I.E.L.D. HELICARRIER SHOP TESTS");
            Console.WriteLine("==================================================");

            int passed = 0;
            int failed = 0;

            void AssertEquals(string testName, long expected, long actual)
            {
                if (expected == actual)
                {
                    passed++;
                    Console.WriteLine($"  Shop sync: ✅ [PASS] {testName} (Expected: {expected}, Actual: {actual})");
                }
                else
                {
                    failed++;
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"  Shop sync: ❌ [FAIL] {testName} (Expected: {expected}, Actual: {actual})");
                    Console.ResetColor();
                }
            }

            void AssertTrue(string testName, bool condition)
            {
                if (condition)
                {
                    passed++;
                    Console.WriteLine($"  Shop sync: ✅ [PASS] {testName}");
                }
                else
                {
                    failed++;
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"  Shop sync: ❌ [FAIL] {testName}");
                    Console.ResetColor();
                }
            }

            try
            {
                // Clean old test profiles & packages
                void CleanupTestData()
                {
                    var testProfiles = context.Profiles
                        .Where(p => p.Nickname.StartsWith("ShopTestAgent"))
                        .ToList();
                    var testProfileIds = testProfiles.Select(p => p.Id).ToList();

                    // Remove inventory items
                    var invItems = context.PlayerInventoryItems
                        .Where(pi => testProfileIds.Contains(pi.PlayerProfileId))
                        .ToList();
                    context.PlayerInventoryItems.RemoveRange(invItems);

                    // Remove profiles
                    context.Profiles.RemoveRange(testProfiles);

                    var testUsers = context.Users
                        .Where(u => u.Username.StartsWith("ShopTestUser"))
                        .ToList();
                    context.Users.RemoveRange(testUsers);

                    context.SaveChanges();
                }

                // Initial cleanup
                CleanupTestData();

                // Setup test agent profile
                var testUser = new UserAccount { Username = "ShopTestUserAlpha", PasswordHash = "hash" };
                context.Users.Add(testUser);
                context.SaveChanges();

                var profile = new PlayerProfile
                {
                    UserAccountId = testUser.Id,
                    Nickname = "ShopTestAgent",
                    Level = 10,
                    SilverBalance = 100000,
                    MobaCoinBalance = 100
                };
                context.Profiles.Add(profile);
                context.SaveChanges();

                // --------------------------------------------------
                // 1. Config Loading Verification
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 1: Configuration Loading & Parsing ---");
                engine.ReloadConfig();
                var packages = engine.GetShopPackages();
                if (packages == null)
                {
                    AssertTrue("Shop packages list is loaded and not null", false);
                    return false;
                }
                
                AssertTrue("Shop packages list loaded successfully", packages.Count > 0);
                AssertTrue("Contains at least one MobaCoin package", packages.Any(p => p.Type.Equals("MobaCoin", StringComparison.OrdinalIgnoreCase)));
                AssertTrue("Contains at least one Item package", packages.Any(p => p.Type.Equals("Item", StringComparison.OrdinalIgnoreCase)));

                // --------------------------------------------------
                // 2. Fallback Auto-seeding Verification
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 2: Fallback ItemTemplate Seeding ---");
                
                // Temporarily delete Power Pack and Energy Pack templates if they exist to force seeder test
                var existingPowerPack = context.ItemTemplates.FirstOrDefault(t => t.Name.ToLower() == "power pack");
                if (existingPowerPack != null)
                {
                    // Clean references first
                    var invRefs = context.PlayerInventoryItems.Where(pi => pi.ItemTemplateId == existingPowerPack.Id).ToList();
                    context.PlayerInventoryItems.RemoveRange(invRefs);
                    context.ItemTemplates.Remove(existingPowerPack);
                    context.SaveChanges();
                }

                // Check seeder logic by buying a Power Pack (it should auto-create the template)
                var powerPackPkg = packages.FirstOrDefault(p => p.ItemTemplateName.Equals("Power Pack", StringComparison.OrdinalIgnoreCase));
                AssertTrue("Power Pack package configuration is registered", powerPackPkg != null);

                if (powerPackPkg != null)
                {
                    long initialSilver = profile.SilverBalance;
                    var purchaseRes = engine.PurchasePackage(profile.Id, powerPackPkg.Id);
                    
                    AssertTrue("Purchase of Power Pack resolved successfully", purchaseRes.Success);
                    
                    // Verify seeder created the template
                    var newTemplate = context.ItemTemplates.FirstOrDefault(t => t.Name.ToLower() == "power pack");
                    AssertTrue("Power Pack ItemTemplate was auto-seeded on first purchase", newTemplate != null);
                    if (newTemplate != null)
                    {
                        AssertEquals("Seeded item type is correct", 1, newTemplate.Type.Equals("AttackPowerRestorative", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
                        AssertEquals("Seeded item effect value is 100", 100, newTemplate.EffectValue);
                    }

                    // Verify balance deduction
                    long expectedSilver = initialSilver - powerPackPkg.SilverCost;
                    AssertEquals("Silver was deducted correctly for the purchase", expectedSilver, purchaseRes.NewSilverBalance);

                    // Verify inventory addition
                    var inventoryItem = context.PlayerInventoryItems.FirstOrDefault(pi => pi.PlayerProfileId == profile.Id && pi.ItemTemplateId == newTemplate!.Id);
                    AssertTrue("Power Pack was added to user's inventory", inventoryItem != null && inventoryItem.Quantity == powerPackPkg.Quantity);
                }

                // --------------------------------------------------
                // 3. Free Pack Claim Verification
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 3: Free Package Claims ---");
                var freePkg = packages.FirstOrDefault(p => p.IsFree);
                AssertTrue("Free package is registered in config", freePkg != null);

                if (freePkg != null)
                {
                    // Refresh profile
                    var currentProfile = context.Profiles.Find(profile.Id)!;
                    long silverBefore = currentProfile.SilverBalance;
                    
                    var purchaseRes = engine.PurchasePackage(currentProfile.Id, freePkg.Id);
                    
                    AssertTrue("Claimed free package successfully", purchaseRes.Success);
                    AssertEquals("Silver balance remains unchanged for free claim", silverBefore, purchaseRes.NewSilverBalance);

                    // Verify item quantity
                    var freePkgNameLower = freePkg.ItemTemplateName.ToLower();
                    var itemTemplate = context.ItemTemplates.FirstOrDefault(t => t.Name.ToLower() == freePkgNameLower)!;
                    var invItem = context.PlayerInventoryItems.FirstOrDefault(pi => pi.PlayerProfileId == currentProfile.Id && pi.ItemTemplateId == itemTemplate.Id);
                    AssertTrue("Free item successfully registered to inventory", invItem != null && invItem.Quantity >= freePkg.Quantity);
                }

                // --------------------------------------------------
                // 4. MobaCoins Purchases
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 4: MobaCoin Packs Purchases ---");
                var mobaPkg = packages.FirstOrDefault(p => p.Type.Equals("MobaCoin", StringComparison.OrdinalIgnoreCase));
                AssertTrue("MobaCoin package is registered in config", mobaPkg != null);

                if (mobaPkg != null)
                {
                    var currentProfile = context.Profiles.Find(profile.Id)!;
                    long silverBefore = currentProfile.SilverBalance;
                    long mobaBefore = currentProfile.MobaCoinBalance;

                    var purchaseRes = engine.PurchasePackage(currentProfile.Id, mobaPkg.Id);

                    AssertTrue("Purchased MobaCoins successfully", purchaseRes.Success);
                    AssertEquals("Silver deducted correctly", silverBefore - mobaPkg.SilverCost, purchaseRes.NewSilverBalance);
                    AssertEquals("MobaCoins balance increased correctly", mobaBefore + mobaPkg.Quantity, purchaseRes.NewMobaCoinBalance);
                }

                // --------------------------------------------------
                // 5. Deduction Threshold Safety Limits
                // --------------------------------------------------
                Console.WriteLine("\n--- Phase 5: Transaction Pricing and Threshold Limits ---");
                
                // Artificially drop user Silver balance to 0
                var dryProfile = context.Profiles.Find(profile.Id)!;
                dryProfile.SilverBalance = 0;
                context.SaveChanges();

                var expensivePkg = packages.FirstOrDefault(p => !p.IsFree && p.SilverCost > 0);
                AssertTrue("Located premium costing package", expensivePkg != null);

                if (expensivePkg != null)
                {
                    var failedPurchase = engine.PurchasePackage(dryProfile.Id, expensivePkg.Id);
                    
                    AssertTrue("Purchase blocked due to insufficient Silver balance", !failedPurchase.Success);
                    AssertTrue("Purchase returned correct error warning details", failedPurchase.Message.Contains("INSUFFICIENT SILVER"));
                    AssertEquals("Silver balance remains intact at 0", 0, dryProfile.SilverBalance);
                }

                // Clean up after tests
                CleanupTestData();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"💥 EXCEPTION OCCURRED DURING SHOP TESTS: {ex.Message}\n{ex.StackTrace}");
                Console.ResetColor();
                return false;
            }

            Console.WriteLine("==================================================");
            Console.WriteLine($"🧪 SHOP TESTS COMPLETED: {passed} PASSED // {failed} FAILED");
            Console.WriteLine("==================================================");

            return failed == 0;
        }
    }
}
