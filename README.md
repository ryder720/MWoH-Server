# Marvel: War of Heroes (MWoH) Private Server Suite

Welcome to the **Marvel: War of Heroes (MWoH) Private Server Suite**. This repository provides a complete, high-fidelity local server emulation (ASP.NET Core / SQLite), a comprehensive Python scraper suite to crawl original card illustrations, item metadata, and campaign operations from the community wiki, and a fully automated Python APK bytecode patcher to customize and sign game clients for local gameplay on Android emulators.

---

## 📁 Repository Structure

```
MWoH Server/
├── Server/                          # ASP.NET Core 10.0 Web API Private Server
│   ├── Controllers/
│   │   ├── CygamesController.cs     # Game logic API (/ultimate/* routes)
│   │   └── MobageController.cs      # DeNA/Mobage social & auth SDK stubs
│   ├── Data/                        # SQLite EF Core DbContext & auto-seeder
│   ├── Filters/                     # Cryptographic GAuth HMAC-SHA1 action filters
│   ├── Models/                      # Entity models (User, Cards, Items, Allies, Rally)
│   ├── Services/                    # Core gameplay service layer
│   │   ├── AuthService.cs           # User credential validation & token generation
│   │   ├── SessionGateway.cs        # Multi-strategy session resolution (GAuth/cookie/OAuth)
│   │   ├── MissionEngine.cs         # Campaign operations, sector attacks, boss battles
│   │   ├── CardGrowthEngine.cs      # Enhancement (ISO-8), fusion, ability level-ups
│   │   ├── GachaSummoner.cs         # JSON-configured gacha pulls (multi-currency)
│   │   ├── ItemLedger.cs            # Consumable item usage & inventory management
│   │   ├── DeckManager.cs           # Attack/Defense deck composition sync
│   │   └── LeaderManager.cs         # Leader card designation
│   ├── Config/
│   │   ├── gameplay_settings.json   # Tunable gameplay parameters
│   │   └── gacha_config.json        # Gacha pack definitions & rarity weights
│   ├── Views/                       # 16 server-rendered HTML game screens
│   ├── Logs/                        # Runtime log output (latest_run.log)
│   └── wwwroot/                     # Static asset server (card art, operations, items)
├── Tools/
│   ├── Scraper/                     # Wiki Data & Artwork Crawler Suite
│   │   ├── run_all_scrapers.py      # Master orchestrator (--test / --sandbox / --full)
│   │   ├── wiki_scraper.py          # Hero card data crawler → cards_db.json
│   │   ├── wiki_items_scraper.py    # Consumable item crawler → items_db.json
│   │   ├── wiki_operations_scraper.py # Campaign operations crawler → operations_db.json
│   │   ├── download_missing_images.py # Card artwork diagnostic downloader
│   │   ├── download_item_images.py  # Item artwork downloader
│   │   └── download_resource_images.py # Tactical resource artwork downloader
│   └── Patcher/                     # Automated Client APK Bytecode Patcher
│       ├── patcher.py               # Patching script (manifest + smali + HTTPS downgrade)
│       └── bin/                     # Tooling jars (apktool.jar / uber-apk-signer.jar)
├── APK/
│   ├── Base/                        # Place untouched original game APKs here
│   └── Modified/                    # Output directory for patched, signed clients
├── CONTEXT.md                       # Domain model & architectural decision context
└── README.md
```

> [!NOTE]
> The entire `/APK/` folder (including original and patched clients), decompiled intermediates, downloaded artwork (`Server/wwwroot/images/`), local SQLite databases (`mwoh.db`), and logs are strictly ignored in Git via the root `.gitignore` to prevent any distribution of copyrighted assets. This keeps the repository extremely lean and legally clear.
>
> **Getting Started Setup**: When first cloning this repository, you should create a folder named `APK/Base/` at the root and place your untouched game APK there. The automated patcher script will dynamically generate the `APK/Modified/` output directory for you during execution.

---

## 📱 1. Automated Client APK Patching

The automated patcher dynamically redirects game traffic, injects cleartext traffic rules, downgrades dynamic SSL handshakes, and debug-signs any base game APK for emulator deployment. This redirect must be established before launching or testing the client.

### Prerequisites
* **Python 3.x** installed.
* **Java Runtime Environment (JRE)** in your system PATH (required to execute Java-based compiler tools).

### Patching Instructions
1. Navigate to the patcher directory:
   ```powershell
   cd Tools/Patcher
   ```
2. Run the patcher script pointing to your untouched base APK:
   ```powershell
   python patcher.py --ip 10.0.2.2 --port 5000 "../../APK/Base/marvel-war-of-heroes-1-5-16-en-android.apk" --output "../../APK/Modified/marvel_woh_patched.apk"
   ```

> [!TIP]
> **No manual tooling setup required!** The patcher will automatically verify your Java environment and check for `bin/apktool.jar` and `bin/uber-apk-signer.jar`. If they are missing, it will dynamically download them from their official GitHub releases on first run.

### Behind the Scenes: What the Patcher Does
* **Cleartext Permission**: Injects `android:usesCleartextTraffic="true"` to the `AndroidManifest.xml` to allow standard HTTP queries.
* **Endpoint Redirection**: Replaces the standard Cygames and Mobage hostnames in `strings.xml` and `ServerMode.smali` with your private server IP/Port.
* **Telemetry Bypass**: Redirects ngpipes analytic servers directly to loopback and changes the connection ports, bypassing defunct telemetry SSL blocks that would otherwise crash the Mobage SDK initialization loop.
* **SSL Bytecode Downgrade**: Scans and patches all dynamic Smali bytecode connection files, downgrading any dynamic `https://` occurrences to standard plain-text `http://` configurations.
* **Debug-Signing**: Compiles resources back together and uses `uber-apk-signer` to zip-align and sign the final package with a standard debug certificate.

---

## 🕸️ 2. Card & Media Wiki Scraper Suite

To populate the private server with authentic card metadata, item catalogs, campaign operations, and illustrations, run the scraper suite to download all templates and artwork. Doing this *before* starting the C# server ensures all assets seed successfully on first DB generation.

### Prerequisites
* **Python 3.x** installed. All scrapers are zero-dependency (stdlib only).

### Quick Start: Run Everything
The master orchestrator script runs all three scrapers and all artwork downloaders in the correct sequence:
```powershell
cd Tools/Scraper
python run_all_scrapers.py --full
```

### Individual Scraper Commands

| Script | Command | Output |
|---|---|---|
| **Card Scraper** | `python wiki_scraper.py` | `cards_db.json` (~1,976 heroes) |
| **Item Scraper** | `python wiki_items_scraper.py` | `items_db.json` (consumable catalog) |
| **Operations Scraper** | `python wiki_operations_scraper.py` | `operations_db.json` (29 campaign ops) |
| **Card Artwork** | `python download_missing_images.py` | `Server/wwwroot/images/cards/` |
| **Item Artwork** | `python download_item_images.py` | `Server/wwwroot/images/items/` |
| **Resource Artwork** | `python download_resource_images.py` | `Server/wwwroot/images/resources/` |

### Testing & Sandbox Modes
```powershell
# Sandbox: crawl the first 3 cards of each alignment to test connections
python wiki_scraper.py --sandbox

# Test: parse a single card by name
python wiki_scraper.py --test "Great Responsibility Spider-Man"
```

### Scraper Capabilities
* **Automatic Variant Matching**: Distinguishes base from fused variants (e.g. `Spider-Man` vs `Spider-Man+`) via context-sensitive page titles and MediaWiki images.
* **Widescreen Static Cache Feeding**: Downloads high-resolution original card illustrations and saves them directly into the server's static directory for offline-ready, high-speed serving.
* **Polite Crawling**: Respects remote wiki servers by keeping a strict `1.5` second cooldown between requests to prevent IP blocks.

---

## 🚀 3. Private Server Setup & Execution

The server runs on **ASP.NET Core 10.0** with **EF Core SQLite** and listens on port `5000` to handle loopback traffic from Android emulators (`10.0.2.2`).

### Prerequisites
* Ensure you have the [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) installed.

### Launch Instructions
1. Open a terminal and navigate to the `Server` directory:
   ```powershell
   cd Server
   ```
2. Build and launch the application:
   ```powershell
   dotnet run
   ```
3. On startup, the server will:
   * Create and initialize the local SQLite database file `Server/mwoh.db`.
   * Run 9 automatic SQLite schema migrations to ensure all tables and columns are up-to-date.
   * Automatically seed a default player profile (`testuser` / `password`) with virtual currency (`999,999 MobaCoins`, `1,000,000 Silver`) and designated starting card lineups.
   * **Full Card Seed**: Parse `Tools/Scraper/cards_db.json` and seed all ~1,976 crawled card templates (with 6 fallback heroes if the file is missing).
   * **Item Seed**: Parse `Tools/Scraper/items_db.json` and seed all consumable item templates.
   * **Resource Seed**: Auto-generate 42 tactical resources (7 groups × 6 color variants).
   * **Operations Banners**: Background-download operation banner images from Fandom Wiki for all 29 campaigns.
   * Launch the **Admin CLI** console in the background for live server management.
   * Start listening on `http://*:5000` with dual logging to stdout and `Logs/latest_run.log`.

### Server Architecture

| Layer | Technology | Purpose |
|---|---|---|
| **Framework** | ASP.NET Core (Kestrel) | HTTP server on port 5000 |
| **Database** | SQLite via EF Core | Persistent game state |
| **Templating** | Custom `{{placeholder}}` rendering | Server-side HTML view injection |
| **Auth** | GAuth HMAC-SHA1 + session cookies | Request validation (dev-bypassed) |
| **Configuration** | `Config/*.json` | Tunable gameplay parameters |
| **Logging** | DualWriter | Simultaneous stdout + file output |

---

## 🛠️ 4. Admin CLI Console

The server includes a live administrative command-line interface that runs in the background during server operation. Type commands directly into the server terminal window.

| Command | Description |
|---|---|
| `help` | Display all available commands |
| `status` | Show current server status and connected players |
| `reload` | Hot-reload gameplay settings from `Config/gameplay_settings.json` |
| `<username> addcurrency <amount>` | Grant MobaCoins to a player |
| `<username> addcard <cardTemplateId>` | Give a specific card to a player |
| `<username> setlevel <level>` | Set a player's level directly |
| `<username> resetattributes` | Wipe and refund all allocated S.H.I.E.L.D. stat points |

---

## 🎖️ 5. Emulated Systems & Gameplay Features

This suite emulates the rich original game mechanics of *Marvel: War of Heroes*, complete with modern, premium glassmorphic overlays and robust database-backed operations.

### Card Collection & Growth
* **🎰 Gacha Summon Mainframes**: Spend virtual Silver, premium MobaCoins, collectible Gacha Summon Tickets, or co-op Rally Points at recruitment hubs to summon tactical card reinforcements. Three distinct packs available: *Ultimate Card Pack*, *S.H.I.E.L.D. Elite Node*, and *S.H.I.E.L.D. Rally Pack*.
* **📖 Card Catalog Browser**: Browse your full card collection with filtering by rarity, alignment (Speed/Bruiser/Tactics), and faction. View detailed card stats, abilities, mastery progress, and high-res artwork.
* **⚡ Card Enhancement (ISO-8 Forge)**: Level up cards via ISO-8 Serum items (+3 levels) or by sacrificing material cards for experience. Cards scale stats through 8 rarity tiers with level caps from 30 (Common) to 100 (Special Legend).
* **🔮 Card Fusion**: Fuse identical card templates together for permanent stat bonuses (+10% stat carry-over for Perfect Fusions). Stacks fusion bonus ATK/DEF onto the surviving card.
* **⚡ Card Mastery Grinding**: Roster cards in your Attack Deck or designated Leader slots organically earn mastery points (`+1` on mission clicks, `+5` in PvP battles), boosting stats and preparing cards for Perfect Fusions.

### Combat & Campaign
* **🗺️ Campaign Operations**: 29 unique campaign operations loaded from scraped wiki data, each containing multiple mission sectors with energy costs, XP/Silver/mastery rewards, and possible card and resource drops.
* **⚔️ Boss Battles**: Triggered at 100% sector progress with S.H.I.E.L.D. team support selection — pick up to 2 active Allies whose leader cards' ATK and DEF values stack with yours during encounters.
* **⚡ Energy System**: Real-time energy recovery at configurable intervals. Energy is consumed per mission sector attack and restores passively over time.

### Social & Co-op
* **🤝 S.H.I.E.L.D. Allies Network**: Propose connection requests, manage incoming invites, and expand your co-op active squad size (scales from 5 up to 50 members). Accepting invites grants `+5` stat points to both players. Dismissing team members incurs a point penalty, keeping alliances competitive.
* **🟢 Quick-Rally Co-op Economy**: Rally agents in your squad roster or search directory via responsive AJAX controls. Earn `+20`/`+10` (teammates) or `+10`/`+5` (strangers) co-op Rally Points. Runs a strict 24-hour cycle cooldown per player combination.
* **📂 Agent Dossier Files**: Inspect other players' dossiers detailing operative levels, representative leader cards, alliance relationship status, and direct rally/propose controls.

### Inventory & Resources
* **🧪 Targeted ISO-8 Serums**: Apply Level-Up ISO-8 Serums (+3 levels) or Mastery ISO-8 Serums (+10 mastery points) directly to target heroes in your roster via an interactive card selector modal.
* **🎒 Slot Capacity Expansion**: Consume `Card Stock` items inside the Items Depot to permanently expand your maximum card inventory beyond the standard 250 capacity.
* **💎 Exchange Vault & Vault Collections**: Defeat operations bosses to drop 42 distinct resources (7 unique sets of 6 colors). Donate excess resource drops for direct Silver credits, or redeem complete sets (up to 3 times each) at the Exchange Vault for authentic Rare reward cards or ISO-8 Serums.

### Player Progression
* **📊 Manual RPG Parameter Allocation**: Manually allocate unassigned S.H.I.E.L.D. stat points earned on Level-Up (+3 points) to customize your Energy, ATK, or DEF pools.
* **🏗️ Attack & Defense Deck Builder**: Organize your hero roster into Attack and Defense decks with power cost validation against your allocated stats.
* **🌟 Leader Card System**: Designate a single leader card that represents you to other players in dossiers and co-op battles.

### Authentication & Social SDK
* **🔐 Login & Registration**: Full Mobage SDK emulation with a glassmorphic webview login/register portal, OAuth token exchange, and native `ngcore://` bridge callbacks for seamless session persistence.
* **🌐 Community Hub**: Configurable redirect to your community page (default: GitHub repository).

---

## ⚙️ 6. Configuration Reference

All gameplay parameters are hot-reloadable via the `reload` admin CLI command.

### `Config/gameplay_settings.json`

```jsonc
{
  "Gameplay": {
    "EnergyRecovery": {
      "IntervalSeconds": 3,        // Seconds between energy ticks
      "AmountPerInterval": 1        // Energy restored per tick
    },
    "LevelUp": {
      "BaseXpRequirement": 1000,    // XP needed for level 2
      "XpIncrementPerLevel": 500,   // Additional XP per subsequent level
      "EnergyMaxIncreasePerLevel": 2 // Max energy increase per level-up
    },
    "DefaultDeckCapacity": {
      "AttackPower": 100,           // Starting ATK deck capacity
      "DefensePower": 100           // Starting DEF deck capacity
    },
    "CardGrowth": {
      "DefaultMasteryPercentage": 0, // Starting mastery % for new cards
      "MasteryGainPerMissionClick": 1,
      "MasteryGainPerPvPBattle": 5
    },
    "CommunityUrl": "https://github.com/ryder720/MWoH-Server",
    "ResourceDropRatePercentage": 100,
    "EnableFriendRemoval24HourPenalty": true  // Toggle 6th-removal stat penalty
  }
}
```

### `Config/gacha_config.json`

Defines available gacha packs with rarity weight pools and multi-currency cost structures. Currently ships with three packs:

| Pack | Currencies | Rates (N / R / SR / L) |
|---|---|---|
| **Ultimate Card Pack** | 300 MC / 10,000 Ag / 1 Ticket | 70 / 25 / 4 / 1 |
| **S.H.I.E.L.D. Elite Node** | 900 MC / 30,000 Ag / 1 Ticket | 40 / 45 / 12 / 3 |
| **S.H.I.E.L.D. Rally Pack** | 200 Rally Points | 75 / 20 / 4.5 / 0.5 |

---

## 🗄️ 7. Database Schema

The SQLite database (`mwoh.db`) contains 8 entity tables managed by EF Core:

| Table | Purpose |
|---|---|
| `Users` | Account credentials, active tokens |
| `Profiles` | Player persona: level, currencies, energy, stats, mission progress |
| `CardTemplates` | Static hero blueprints (~1,976 cards across 8 rarity tiers) |
| `PlayerCards` | Dynamic card instances: level, mastery, fusion bonuses, deck assignments |
| `ItemTemplates` | Static item catalog (restoratives, serums, tickets, resources) |
| `PlayerInventoryItems` | Owned item quantities per player |
| `ShieldTeamMembers` | Friend/ally relationships (Pending → Accepted workflow) |
| `RallyLogs` | 24-hour cooldown rally point exchange history |

### Card Rarity Tiers & Level Caps

| Rarity | Max Level |
|---|---|
| Common / Normal | 30 |
| High Normal / Uncommon | 40 |
| Rare | 50 |
| High Rare | 60 |
| Super Rare | 70 |
| Ultra Rare | 80 |
| Legend / Legendary | 90 |
| Special Legend | 100 |

---

## 🎮 8. Deploying and Playing on Bluestacks

1. Drag-and-drop the generated `marvel_woh_patched.apk` directly into your **Bluestacks** or standard Android Emulator window to install it.
2. Launch the **C# Private Server** (`dotnet run` in the `/Server` folder).
3. Open the game app on your emulator.
4. On the secure S.H.I.E.L.D. login panel, sign in with the pre-seeded account:
   * **Username**: `testuser`
   * **Password**: `password`
5. The game webview login will authenticate, close automatically, initialize native GAuth session parameters, and boot you straight into the S.H.I.E.L.D. tactical gameplay top page showing your dynamic profile details, SQLite card inventory, and silver balance!

> [!TIP]
> You can register additional accounts directly from the login screen's "Register" tab. Each new account receives its own independent profile, card inventory, and currency balances.

---

## 🖥️ 9. Game Screens

The server renders 16 HTML game views, each featuring premium glassmorphic UI design:

| Screen | Route | Description |
|---|---|---|
| Top Page | `/ultimate` | Game entry splash screen |
| My Page | `/ultimate/mypage` | Player dashboard with leader card, stats, energy/XP bars |
| Menu Hub | `/ultimate/menu` | Central navigation to all game features |
| Deck Builder | `/ultimate/mypage/deck` | Attack/Defense deck composition editor |
| Card Catalog | `/ultimate/mypage/catalog` | Full collection browser with rarity/alignment filters |
| Enhancement Forge | `/ultimate/mypage/enhance` | Card leveling via ISO-8 serums or material cards |
| Fusion Chamber | `/ultimate/card_union` | Merge identical heroes for stat bonuses |
| Gacha Hub | `/ultimate/gacha` | Multi-pack recruitment with animated card reveals |
| Items Depot | `/ultimate/item` | Consumable inventory with USE actions |
| Resource Vault | `/ultimate/resource` | Tactical resource set redemption & Silver donation |
| Operations Hub | `/ultimate/mypage/missions` | Campaign mission selection across 29 operations |
| Mission Play | `/ultimate/mypage/missions/play/{id}` | Active battle screen with progress bar & boss encounters |
| S.H.I.E.L.D. Team | `/ultimate/friend` | Ally management, invites, search, and quick-rally |
| Agent Dossier | `/ultimate/mypage/agent/{id}` | Player profile inspection with rally/propose actions |
| Community | `/ultimate/community_redirect` | Redirect to configured community URL |
| Stub Portal | Various | Placeholder for upcoming features (shop, trade, archive) |
