# Marvel: War of Heroes (MWoH) Private Server Suite

Welcome to the **Marvel: War of Heroes (MWoH) Private Server Suite**. This repository provides everything needed to host a local private server emulation for the mobile card game. It features a high-fidelity SQLite-backed C# emulator, an automated APK patcher for client redirection, and a complete scraper suite to pull original game assets and illustrations.

---

## ⚙️ Where to Find Server Settings

All customizable gameplay rules, progression multipliers, and card structures are managed inside the [Server/Config](file:///c:/Projects/MWoH%20Server/Server/Config) directory:

1. **[gameplay_settings.json](file:///c:/Projects/MWoH%20Server/Server/Config/gameplay_settings.json)**
   * **Controls**: Real-time energy recovery intervals (`IntervalSeconds`, `AmountPerInterval`), leveling XP curves, starting deck capacities, card mastery gain rates, and boss loot drop chances.
2. **[gacha_config.json](file:///c:/Projects/MWoH%20Server/Server/Config/gacha_config.json)**
   * **Controls**: Available recruitment packs, cost currencies (Silver, MobaCoins, Rally Points, or Tickets), and rarity probability distributions.
3. **[assignments_config.json](file:///c:/Projects/MWoH%20Server/Server/Config/assignments_config.json)**
   * **Controls**: Active S.H.I.E.L.D. quest templates and progression achievements.
4. **[events_config.json](file:///c:/Projects/MWoH%20Server/Server/Config/events_config.json)**
   * **Controls**: Special raid operations schedules and milestone tier rewards.
5. **[login_commendations_config.json](file:///c:/Projects/MWoH%20Server/Server/Config/login_commendations_config.json)**
   * **Controls**: Daily login reward calendar schedules and items.

> [!TIP]
> **Hot-Reloading Settings**: You do not need to restart the server when changing configuration files. Type `reload` in the server terminal to instantly apply changes to `gameplay_settings.json` and `gacha_config.json`. Use `assignments reload`, `commendations reload`, or `events reload` for the others.

---

## 🚀 Getting Started (Essentials)

Follow these steps to patch the game client, pull the assets, and boot the emulator.

### Prerequisites
* **Python 3.x**
* **Java Runtime Environment (JRE)** (configured in your system `PATH`, needed to patch & sign APK smali bytecode)
* **.NET 10.0 SDK** (to build and host the C# server api)

---

### Step 1: Patch the Client APK
The game client needs to be modified to talk to your local server instead of defunct remote systems.

1. Create a directory named `APK/Base/` in the root of the project.
2. Copy your untouched base game APK into `APK/Base/` (e.g. `marvel-war-of-heroes-1-5-16-en-android.apk`).
3. Run the patcher script to compile a redirected, cleartext-enabled, and signed version:
   ```powershell
   cd Tools/Patcher
   python patcher.py --ip 10.0.2.2 --port 5000 "../../APK/Base/marvel-war-of-heroes-1-5-16-en-android.apk" --output "../../APK/Modified/marvel_woh_patched.apk"
   ```
   > [!NOTE]
   > The required compiler tools (`apktool.jar` and `uber-apk-signer.jar`) will be automatically downloaded into `Tools/Patcher/bin/` on the first run.

---

### Step 2: Scrape Game Assets & Artwork
Before launching the server, populate the database seeds and static files by running the crawler orchestrator:
```powershell
cd Tools/Scraper
python run_all_scrapers.py --full
```
This scrapes original hero cards, items, campaigns, and high-res card art, saving them directly into the server's static directory.

---

### Step 3: Launch the C# Private Server
Start the ASP.NET Core server. It automatically runs SQLite schema migrations, seeds the scraped game assets, and initializes the background console:
```powershell
cd Server
dotnet run
```
The server will start listening on `http://*:5000`.

---

### Step 4: Install and Play!
1. Start your Android Emulator (e.g., Bluestacks).
2. Drag-and-drop the generated `APK/Modified/marvel_woh_patched.apk` into the emulator window to install it.
3. Open the game and log in using the pre-seeded testing account:
   * **Username**: `testuser`
   * **Password**: `password`
   * *(To play on another profile, select the **Register** tab to create a new agent account).*

---

## 🛠️ Admin CLI Commands

While the private server is running, you can enter commands directly into the server terminal window to monitor server health, run test suites, or alter player profiles.

### 1. Global & Configuration Commands
| Command | Description |
|---|---|
| `help` | Display list of commands and details |
| `status` | Show active database connection and profiles metrics |
| `reload` | Hot-reload `gameplay_settings.json` and `gacha_config.json` |
| `assignments reload` | Reload all active player quest blueprints |
| `assignments list` | List currently configured quest blueprints |
| `commendations reload` | Reload daily login calendars and reward maps |
| `commendations list` | List currently configured login calendars |
| `events reload` | Reload active operations event templates |
| `events list` | List all event configurations and schedules |
| `events calculate <eventId>` | Force rank calculations and distribute event rewards |

### 2. Player Profile & Agent Management
*(Replace `<username>` with the target player's registered login username)*
| Command | Description |
|---|---|
| `<username> addcurrency <silver\|mobacoin> <n>` | Grant or deduct player currency balances safely |
| `<username> addcard <templateId> [lvl] [mst]` | Spawn a hero card into player stock (specify optional level and mastery) |
| `<username> setlevel <level>` | Scale player level and automatically recalculate max energy limits |
| `<username> resetattributes` | Revert player stats to baseline and refund all allocated Attribute Points |
| `<username> resetassignments` | Wipe all quest/mission progress history for the player |
| `<username> resetcommendations` | Reset player daily login calendars progress |
| `<username> addeventpoints <eventId> <points>` | Add or subtract event progression points for the player |

### 3. Integrated Test Suite Runners
You can execute custom S.H.I.E.L.D. service diagnostics and subsystem verification suites live:
* `runtests`: Verify card ability evaluation and fusion formulas.
* `runbattletests`: Run the PvP / Raid combat engine and stat resolution checks.
* `runalliancetests`: Verify alliance creation, donation leagues, and buffs.
* `runcombotests`: Validate tactical card combinations and active combos.
* `runtradetests`: Execute player-to-player card and item trade exchanges.
* `runassignmenttests`: Diagnose quest completion registers and milestone rewards.
* `runcommendationtests`: Verify daily calendar login increments and delivery.
* `runshieldtests`: Test player relationship requests, allies list, and co-op rally cycles.
* `runvaulttests`: Run resource donation ledger, drop lists, and redemption sets.
* `runprofiletests`: Test registration, profile setups, level-ups, and stat distribution.
* `runeventtests`: Validate baseline event mechanisms and active scheduling windows.
