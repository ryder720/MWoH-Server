# 🎒 S.H.I.E.L.D. Items Database & Future Polish TODO

This document tracks the current state of the game items database in the MWoH Private Server and outlines items, effects, and features to revisit during the final polishing phase.

---

## 📊 Current Database State
- **Scraped Catalog**: Successfully extracted **42 historical items** from the community Fandom Wiki.
- **Data File**: Saved in [`Tools/Scraper/items_db.json`](file:///c:/Projects/MWoH%20Server/Tools/Scraper/items_db.json).
- **Seeding Engine**: The [`DatabaseSeeder.cs`](file:///c:/Projects/MWoH%20Server/Server/Data/DatabaseSeeder.cs) automatically reads the catalog on startup, parses attributes, creates static `ItemTemplate` records, and populates every new user profile with a starting pack of **50x of each item** for testing.
- **Active Restoratives**: All restoratives (Energy, Attack, and Defense refills) dynamically update the player's SQLite profile in real-time when clicked via either the **in-game Items page** or the **S.H.I.E.L.D. Command Center browser console**.

---

## 🛠️ Items to Revisit & Polish Later
While all 42 items are cataloged in the database, several specialized item types are currently treated as general collectibles or have simplified effect stubs. Use this checklist during the final polish phase:

### 🎟️ 1. Gacha & Ticket Exchange Integration
- [ ] **Ultimate Card Pack Tickets & Gacha Tickets**:
  - *Current Status*: Seeded as `General` type items with `effect_value = 0`.
  - *Polish Goal*: Integrate with the virtual storefront and card gacha system. When used, they should deduct 1 ticket and invoke the C# random card generator to award the player a new card from the Gacha card pool.
  - *Target Items*:
    - `Ultimate Card Pack Ticket` (ID: 2)
    - `Gacha Ticket` (ID: 15)
    - `Half-Anniversary Ticket` (ID: 21)
    - `Super Hero Pack Ticket` (ID: 35)
    - `Bruiser Ticket` (ID: 39)
    - `Tactics Ticket` (ID: 40)
    - `Speed Ticket` (ID: 41)

### 🧪 2. ISO-8 Level-up & Mastery Serums
- [ ] **Level Up ISO-8 Serums**:
  - *Current Status*: Seeded as `General` with `effect_value = 0`.
  - *Polish Goal*: Connect to the Player Card inventory system. When a player uses a Level Up Serum, prompt them to select a card in their inventory and increase its `CurrentLevel` by the designated value (e.g., +3 levels).
  - *Target Items*:
    - `Level Up ISO-8 Serum` (ID: 10)
    - `Super Level Up ISO-8 Serum` (ID: 36)
- [ ] **Mastery ISO-8**:
  - *Current Status*: Fully functional refill, but could be enhanced with card-specific targeting.
  - *Polish Goal*: Implement a card selection modal to apply mastery points (+10 or full) to a designated card's `CurrentMastery` in SQLite.
  - *Target Items*:
    - `Mastery Iso-8` (ID: 7 in fallbacks, mapped to standard mastery in JSON)

### 🛡️ 3. Resource & Alliance Items
- [ ] **Shield Barriers**:
  - *Current Status*: Mapped as a general collectible.
  - *Polish Goal*: Implement Resource Raid protection logic in the mission/PvP database structures. When active, decrement a barrier to block incoming player attacks on active Silver/Resources.
  - *Target Item*: `Shield Barrier` (ID: 1)
- [ ] **Alliance & Co-op Items**:
  - *Current Status*: Seeded with descriptions and images.
  - *Polish Goal*: Create stubs or full logic for Alliance event stats multipliers.
  - *Target Items*:
    - `Co-op Power Pack` (ID: 12)
    - `Alliance Energy Kit` (ID: 28)
    - `Alliance Power Kit` (ID: 29)

### 📁 4. Inventory Expansion Items
- [ ] **Card Stock Expansion**:
  - *Current Status*: Mapped with auto-apply descriptions.
  - *Polish Goal*: When claimed or used, dynamically increase the player profile's maximum card inventory limit capacity in `Profiles`.
  - *Target Item*: `Card Stock` (ID: 11)

---

## 📈 Database Extension Protocol
If any hidden or unlisted game items are discovered in client smali code or assets during emulator execution:
1. **Append to JSON**: Add the new item to [`Tools/Scraper/items_db.json`](file:///c:/Projects/MWoH%20Server/Tools/Scraper/items_db.json):
   ```json
   {
       "id": 43,
       "name": "Custom Mysterious Collectible",
       "description": "A rare developer token found deep within S.H.I.E.L.D. archives.",
       "type": "General",
       "effect_value": 0,
       "image_url": ""
   }
   ```
2. **Reload Database**: Simply delete the local `Server/mwoh.db` file (or run a database update command if migrations are added) and restart the server (`dotnet run`). The seeder will automatically re-import the updated list and assign 50x to the active `testuser` account.
