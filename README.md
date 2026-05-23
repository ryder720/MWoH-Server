# Marvel: War of Heroes (MWoH) Private Server Suite

Welcome to the **Marvel: War of Heroes (MWoH) Private Server Suite**. This repository provides a complete, high-fidelity local server emulation (ASP.NET Core / SQLite), a zero-dependency Python scraper to crawl original card illustrations and game metadata from the community wiki, and a fully automated Python APK bytecode patcher to customize and sign game clients for local gameplay on Android emulators.

---

## 📁 Repository Structure

```
MWoH Server/
├── Server/                      # ASP.NET Core 10.0 Web API Private Server
│   ├── Controllers/             # Cygames Game Logic and DeNA/Mobage Social APIs
│   ├── Data/                    # SQLite EF Core Database context and auto-seeder
│   ├── Filters/                 # Cryptographic GAuth Action Filters
│   └── wwwroot/                 # Static asset server directory (operations and card art)
├── Tools/
│   ├── Scraper/                 # Wiki Card Data & Artwork Crawler
│   │   ├── wiki_scraper.py      # Crawler script (MediaWiki API + HTML parsing)
│   │   ├── download_missing_images.py # Diagnostic downloader for missing illustrations
│   │   └── cards_db.json        # Output seeded card metadata database
│   └── Patcher/                 # Automated Client APK Bytecode Patcher
│       ├── patcher.py           # Patching script (manifest + smali + HTTPS downgrade)
│       └── bin/                 # Tooling jars (apktool.jar / uber-apk-signer.jar)
└── APK/
    ├── Base/                    # Place untouched original game APKs here
    └── Modified/                # Output directory for patched, signed clients
```

> [!NOTE]
> The entire `/APK/` folder (including original and patched clients), decompiled intermediates, downloaded artwork (`Server/wwwroot/images/cards/` and `/images/operations/`), local SQLite databases (`mwoh.db`), and logs are strictly ignored in Git via the root `.gitignore` to prevent any distribution of copyrighted assets. This keeps the repository extremely lean and legally clear.
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

## 🕸️ 2. Card & Media Wiki Scraper

To populate the private server with authentic card metadata, stats, and illustrations, run the isolated scraper to download the full templates and files. Doing this *before* starting the C# server ensures all 1,976 cards seed successfully on first DB generation!

### Scraper Instructions
1. Navigate to the scraper directory:
   ```powershell
   cd Tools/Scraper
   ```
2. To run a safe **Sandbox Test** (crawling the first 3 cards of each alignment to test connections):
   ```powershell
   python wiki_scraper.py --sandbox
   ```
3. To run a **Full Crawl** (crawls all speed, bruiser, and tactics cards):
   ```powershell
   python wiki_scraper.py
   ```
4. To run the **Diagnostic Asset Sync** (downloads any missing card illustrations to ensure 100% static cover in wwwroot):
   ```powershell
   python download_missing_images.py
   ```
5. To test parsing on a single custom card:
   ```powershell
   python wiki_scraper.py --test "Great Responsibility Spider-Man"
   ```

### Scraper Capabilities
* **Automatic Variant Matching**: Distinguishes base from fused variants (e.g. `Spider-Man` vs `Spider-Man+`) via context-sensitive page titles and MediaWiki images.
* **Widescreen Static Cache Feeding**: Downloads high-resolution original card illustrations and saves them directly into the server's static directory `Server/wwwroot/images/cards/` for offline-ready, high-speed serving.
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
   * Automatically seed a default player profile (`testuser` / `password`) with unlimited virtual currency (`999,999 MobaCoins`, `1,000,000 Silver`) and designated starting card lineups.
   * **Full Seed**: Locate and parse `Tools/Scraper/cards_db.json` (generated by the scraper) and automatically seed all 1,976 crawled cards directly into the SQLite database.
   * Start listening on `http://*:5000`.

---

## 🎮 4. Deploying and Playing on Bluestacks

1. Drag-and-drop the generated `marvel_woh_patched.apk` directly into your **Bluestacks** or standard Android Emulator window to install it.
2. Launch the **C# Private Server** (`dotnet run` in the `/Server` folder).
3. Open the game app on your emulator.
4. On the secure S.H.I.E.L.D. login panel, sign in with the pre-seeded account:
   * **Username**: `testuser`
   * **Password**: `password`
5. The game webview login will authenticate, close automatically, initialize native GAuth session parameters, and boot you straight into the S.H.I.E.L.D. tactical gameplay top page showing your dynamic profile details, SQLite card inventory, and silver balance!
