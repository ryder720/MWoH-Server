using System;
using System.IO;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MwohServer.Data;
using MwohServer.Filters;
using MwohServer.Services;

// 0. Redirect stdout/stderr to both console and local Logs/latest_run.log file
var logPath = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "latest_run.log");
var dualWriter = new DualWriter(Console.Out, logPath);
Console.SetOut(dualWriter);
Console.SetError(dualWriter);

var builder = WebApplication.CreateBuilder(args);


// 1. Force Kestrel to run on port 5000 listening on all interfaces
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5000);
});

// 2. Add services to the container.
builder.Services.AddControllers();

// Register SQLite DbContext
builder.Services.AddDbContext<MwohDbContext>(options =>
    options.UseSqlite("Data Source=Data/mwoh.db"));

// Register Custom Services and Filters
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IGachaSummoner, GachaSummoner>();
builder.Services.AddScoped<ICardGrowthEngine, CardGrowthEngine>();
builder.Services.AddScoped<IMissionEngine, MissionEngine>();
builder.Services.AddScoped<IItemLedger, ItemLedger>();
builder.Services.AddScoped<ISessionGateway, SessionGateway>();
builder.Services.AddScoped<IDeckManager, DeckManager>();
builder.Services.AddScoped<ILeaderManager, LeaderManager>();
builder.Services.AddSingleton<ICardAbilityEvaluator, CardAbilityEvaluator>();
builder.Services.AddScoped<IBattleEngine, BattleEngine>();
builder.Services.AddScoped<GAuthValidationFilter>();


// Load Gameplay Custom Configurations
MwohServer.Models.GameplaySettings.Load();

var app = builder.Build();

// 3. Automatically ensure SQLite database exists and is seeded
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<MwohDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Program>>();
    
    dbContext.Database.EnsureCreated();
    // 1. Migrate PlayerCards - Add AbilityLevel column if missing
    try
    {
        var hasAbilityLevel = false;
        var conn = dbContext.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
        {
            dbContext.Database.OpenConnection();
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info(PlayerCards);";
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (reader["name"].ToString() == "AbilityLevel")
                    {
                        hasAbilityLevel = true;
                        break;
                    }
                }
            }
        }

        if (!hasAbilityLevel)
        {
            dbContext.Database.ExecuteSqlRaw("ALTER TABLE PlayerCards ADD COLUMN AbilityLevel INTEGER NOT NULL DEFAULT 1;");
            logger.LogInformation("Database migration: Added AbilityLevel column to PlayerCards.");
        }
        else
        {
            logger.LogInformation("Database migration check finished (AbilityLevel): Already exists.");
        }
    }
    catch (Exception ex)
    {
        logger.LogError($"Database migration failed (AbilityLevel): {ex.Message}");
    }

    // 2. Migrate Profiles - Add LastEnergyRecoveryTime column if missing
    try
    {
        var hasLastEnergyRecoveryTime = false;
        var conn = dbContext.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
        {
            dbContext.Database.OpenConnection();
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info(Profiles);";
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (reader["name"].ToString() == "LastEnergyRecoveryTime")
                    {
                        hasLastEnergyRecoveryTime = true;
                        break;
                    }
                }
            }
        }

        if (!hasLastEnergyRecoveryTime)
        {
            dbContext.Database.ExecuteSqlRaw("ALTER TABLE Profiles ADD COLUMN LastEnergyRecoveryTime TEXT NOT NULL DEFAULT '2026-05-23 00:00:00';");
            logger.LogInformation("Database migration: Added LastEnergyRecoveryTime column to Profiles.");
        }
        else
        {
            logger.LogInformation("Database migration check finished (LastEnergyRecoveryTime): Already exists.");
        }
    }
    catch (Exception ex)
    {
        logger.LogError($"Database migration failed (LastEnergyRecoveryTime): {ex.Message}");
    }

    // 2b. Migrate Profiles - Add S.H.I.E.L.D. Team removal penalty columns if missing
    try
    {
        var hasLastRemovalTime = false;
        var hasRemovalsInLast24Hours = false;
        var conn = dbContext.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
        {
            dbContext.Database.OpenConnection();
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info(Profiles);";
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    var colName = reader["name"].ToString();
                    if (colName == "LastRemovalTime") hasLastRemovalTime = true;
                    if (colName == "RemovalsInLast24Hours") hasRemovalsInLast24Hours = true;
                }
            }
        }

        if (!hasLastRemovalTime)
        {
            dbContext.Database.ExecuteSqlRaw("ALTER TABLE Profiles ADD COLUMN LastRemovalTime TEXT NULL;");
            logger.LogInformation("Database migration: Added LastRemovalTime column to Profiles.");
        }
        if (!hasRemovalsInLast24Hours)
        {
            dbContext.Database.ExecuteSqlRaw("ALTER TABLE Profiles ADD COLUMN RemovalsInLast24Hours INTEGER NOT NULL DEFAULT 0;");
            logger.LogInformation("Database migration: Added RemovalsInLast24Hours column to Profiles.");
        }
    }
    catch (Exception ex)
    {
        logger.LogError($"Database migration failed (Profiles S.H.I.E.L.D. Team penalty fields): {ex.Message}");
    }

    // 2c. Create ShieldTeamMembers table if not exists
    try
    {
        dbContext.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ShieldTeamMembers (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ProfileId INTEGER NOT NULL,
                MemberProfileId INTEGER NOT NULL,
                Status TEXT NOT NULL DEFAULT 'Pending',
                CreatedAt TEXT NOT NULL,
                FOREIGN KEY (ProfileId) REFERENCES Profiles(Id) ON DELETE CASCADE,
                FOREIGN KEY (MemberProfileId) REFERENCES Profiles(Id) ON DELETE CASCADE
            );
        ");
        logger.LogInformation("Database migration: Ensured ShieldTeamMembers table exists.");
    }
    catch (Exception ex)
    {
        logger.LogError($"Database migration failed (ShieldTeamMembers table): {ex.Message}");
    }

    // 2d. Migrate Profiles - Add RallyPoints column if missing
    try
    {
        var hasRallyPoints = false;
        var conn = dbContext.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
        {
            dbContext.Database.OpenConnection();
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info(Profiles);";
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (reader["name"].ToString() == "RallyPoints")
                    {
                        hasRallyPoints = true;
                        break;
                    }
                }
            }
        }

        if (!hasRallyPoints)
        {
            dbContext.Database.ExecuteSqlRaw("ALTER TABLE Profiles ADD COLUMN RallyPoints INTEGER NOT NULL DEFAULT 0;");
            logger.LogInformation("Database migration: Added RallyPoints column to Profiles.");
        }
    }
    catch (Exception ex)
    {
        logger.LogError($"Database migration failed (Profiles RallyPoints field): {ex.Message}");
    }

    // 2e. Create RallyLogs table if not exists
    try
    {
        dbContext.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS RallyLogs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SenderProfileId INTEGER NOT NULL,
                ReceiverProfileId INTEGER NOT NULL,
                RalliedAt TEXT NOT NULL,
                FOREIGN KEY (SenderProfileId) REFERENCES Profiles(Id) ON DELETE CASCADE,
                FOREIGN KEY (ReceiverProfileId) REFERENCES Profiles(Id) ON DELETE CASCADE
            );
        ");
        logger.LogInformation("Database migration: Ensured RallyLogs table exists.");
    }
    catch (Exception ex)
    {
        logger.LogError($"Database migration failed (RallyLogs table): {ex.Message}");
    }

    // 3. Migrate CardTemplates - Add MaxMastery column if missing
    try
    {
        var hasMaxMastery = false;
        var conn = dbContext.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
        {
            dbContext.Database.OpenConnection();
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info(CardTemplates);";
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (reader["name"].ToString() == "MaxMastery")
                    {
                        hasMaxMastery = true;
                        break;
                    }
                }
            }
        }

        if (!hasMaxMastery)
        {
            dbContext.Database.ExecuteSqlRaw("ALTER TABLE CardTemplates ADD COLUMN MaxMastery INTEGER NOT NULL DEFAULT 100;");
            logger.LogInformation("Database migration: Added MaxMastery column to CardTemplates.");
        }
        else
        {
            logger.LogInformation("Database migration check finished (MaxMastery): Already exists.");
        }
    }
    catch (Exception ex)
    {
        logger.LogError($"Database migration failed (MaxMastery): {ex.Message}");
    }

    // 4. Migrate PlayerCards - Add FusionBonusAtk and FusionBonusDef columns if missing
    try
    {
        var hasFusionBonusAtk = false;
        var hasFusionBonusDef = false;
        var conn = dbContext.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
        {
            dbContext.Database.OpenConnection();
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info(PlayerCards);";
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    var colName = reader["name"].ToString();
                    if (colName == "FusionBonusAtk") hasFusionBonusAtk = true;
                    if (colName == "FusionBonusDef") hasFusionBonusDef = true;
                }
            }
        }

        if (!hasFusionBonusAtk)
        {
            dbContext.Database.ExecuteSqlRaw("ALTER TABLE PlayerCards ADD COLUMN FusionBonusAtk INTEGER NOT NULL DEFAULT 0;");
            logger.LogInformation("Database migration: Added FusionBonusAtk column to PlayerCards.");
        }
        if (!hasFusionBonusDef)
        {
            dbContext.Database.ExecuteSqlRaw("ALTER TABLE PlayerCards ADD COLUMN FusionBonusDef INTEGER NOT NULL DEFAULT 0;");
            logger.LogInformation("Database migration: Added FusionBonusDef column to PlayerCards.");
        }
        
        if (hasFusionBonusAtk && hasFusionBonusDef)
        {
            logger.LogInformation("Database migration check finished (FusionBonusAtk/Def): Already exists.");
        }
    }
    catch (Exception ex)
    {
        logger.LogError($"Database migration failed (FusionBonusAtk/Def): {ex.Message}");
    }

    // 5. Migrate Profiles - Add Battle Recovery and Current Power fields if missing
    try
    {
        var hasAttackPowerCurrent = false;
        var hasDefensePowerCurrent = false;
        var hasLastBattlePowerRecoveryTime = false;
        var conn = dbContext.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
        {
            dbContext.Database.OpenConnection();
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info(Profiles);";
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    var colName = reader["name"].ToString();
                    if (colName == "AttackPowerCurrent") hasAttackPowerCurrent = true;
                    if (colName == "DefensePowerCurrent") hasDefensePowerCurrent = true;
                    if (colName == "LastBattlePowerRecoveryTime") hasLastBattlePowerRecoveryTime = true;
                }
            }
        }

        if (!hasAttackPowerCurrent)
        {
            dbContext.Database.ExecuteSqlRaw("ALTER TABLE Profiles ADD COLUMN AttackPowerCurrent INTEGER NOT NULL DEFAULT 100;");
            logger.LogInformation("Database migration: Added AttackPowerCurrent column to Profiles.");
        }
        if (!hasDefensePowerCurrent)
        {
            dbContext.Database.ExecuteSqlRaw("ALTER TABLE Profiles ADD COLUMN DefensePowerCurrent INTEGER NOT NULL DEFAULT 100;");
            logger.LogInformation("Database migration: Added DefensePowerCurrent column to Profiles.");
        }
        if (!hasLastBattlePowerRecoveryTime)
        {
            dbContext.Database.ExecuteSqlRaw("ALTER TABLE Profiles ADD COLUMN LastBattlePowerRecoveryTime TEXT NOT NULL DEFAULT '2026-05-23 00:00:00';");
            logger.LogInformation("Database migration: Added LastBattlePowerRecoveryTime column to Profiles.");
        }
    }
    catch (Exception ex)
    {
        logger.LogError($"Database migration failed (Profiles Battle Power fields): {ex.Message}");
    }

    // 6. Create BattleRecords table if not exists
    try
    {
        dbContext.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS BattleRecords (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                AttackerProfileId INTEGER NOT NULL,
                DefenderProfileId INTEGER NOT NULL,
                WinnerProfileId INTEGER NOT NULL,
                AttackerFinalPower INTEGER NOT NULL,
                DefenderFinalPower INTEGER NOT NULL,
                SilverExchanged INTEGER NOT NULL,
                MasteryEarned INTEGER NOT NULL,
                BattleTime TEXT NOT NULL,
                IsSparring INTEGER NOT NULL DEFAULT 0,
                DetailsJson TEXT NOT NULL DEFAULT '[]',
                FOREIGN KEY (AttackerProfileId) REFERENCES Profiles(Id) ON DELETE CASCADE,
                FOREIGN KEY (DefenderProfileId) REFERENCES Profiles(Id) ON DELETE CASCADE
            );
        ");
        logger.LogInformation("Database migration: Ensured BattleRecords table exists.");
    }
    catch (Exception ex)
    {
        logger.LogError($"Database migration failed (BattleRecords table): {ex.Message}");
    }

    // 7. Ensure healthy default Attack/Defense deck capacity limits for all profiles
    try
    {
        var minLimit = Math.Min(MwohServer.Models.GameplaySettings.DefaultAttackPower, MwohServer.Models.GameplaySettings.DefaultDefensePower) * 0.8;
        var lowCapProfiles = dbContext.Profiles.Where(p => p.AttackPower < minLimit || p.DefensePower < minLimit).ToList();
        if (lowCapProfiles.Any())
        {
            foreach (var p in lowCapProfiles)
            {
                if (p.AttackPower < minLimit) p.AttackPower = MwohServer.Models.GameplaySettings.DefaultAttackPower;
                if (p.DefensePower < minLimit) p.DefensePower = MwohServer.Models.GameplaySettings.DefaultDefensePower;
            }
            dbContext.SaveChanges();
            logger.LogInformation($"Database auto-healing: Restored {lowCapProfiles.Count} player profiles with healthy default deck capacity limits (min {minLimit}).");
        }
    }
    catch (Exception ex)
    {
        logger.LogError($"Database auto-healing failed (Deck Power limits): {ex.Message}");
    }


    DatabaseSeeder.SeedCards(dbContext, logger);
    DatabaseSeeder.SeedItems(dbContext, logger);
    DatabaseSeeder.SeedRivals(dbContext, logger);
    
    // Start background banner downloader task (non-blocking)
    _ = Task.Run(() => DatabaseSeeder.DownloadOperationBanners(logger));
}

// 4. Set up HTTP pipeline
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseRouting();
app.UseStaticFiles();

// Custom Request Logger Middleware
app.Use(async (context, next) =>
{
    var logger = context.RequestServices.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Program>>();
    logger.LogInformation($"[Incoming Request] {context.Request.Method} {context.Request.Path}{context.Request.QueryString}");
    await next();
    logger.LogInformation($"[Outgoing Response] {context.Response.StatusCode} for {context.Request.Method} {context.Request.Path}");
});

// Map controllers (Handles /ultimate/* Cygames APIs and /* Mobage platform endpoints)
app.MapControllers();

// Start background Admin Console CLI loop
AdminConsoleEngine.Start(app);

app.Run();

public static class AdminConsoleEngine
{
    public static void Start(WebApplication app)
    {
        Console.WriteLine("[Admin Console] Developer Command Terminal initialized. Type 'help' for options.");
        
        System.Threading.Tasks.Task.Run(() =>
        {
            while (true)
            {
                try
                {
                    var line = Console.ReadLine();
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    
                    using (var scope = app.Services.CreateScope())
                    {
                        var dbContext = scope.ServiceProvider.GetRequiredService<MwohDbContext>();
                        ExecuteCommand(line.Trim(), dbContext);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Admin Console] Terminal error: {ex.Message}");
                }
            }
        });
    }

    private static void ExecuteCommand(string commandLine, MwohDbContext db)
    {
        var args = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (args.Length == 0) return;

        var primary = args[0].ToLower();

        if (primary == "help")
        {
            Console.WriteLine("\n=== S.H.I.E.L.D. COMMAND TERMINAL ===");
            Console.WriteLine("  help                                           - Display help details");
            Console.WriteLine("  status                                         - Show active server/DB metrics");
            Console.WriteLine("  reload                                         - Reload gameplay & gacha configurations");
            Console.WriteLine("  runtests                                       - Execute card ability evaluation unit tests");
            Console.WriteLine("  runbattletests                                 - Execute S.H.I.E.L.D. Battle Engine unit tests");
            Console.WriteLine("  <username> addcurrency <silver|mobacoin> <n>    - Grant/deduct balances with safety guards");
            Console.WriteLine("  <username> addcard <templateId> [lvl] [mst]    - Spawn card directly into inventory");
            Console.WriteLine("  <username> setlevel <level>                    - Set agent level with capacity auto-scaling");
            Console.WriteLine("  <username> resetattributes                     - Revert agent parameters and refund Attribute Points\n");
            return;
        }

        if (primary == "runtests")
        {
            var evaluator = new CardAbilityEvaluator();
            var success = MwohServer.Tests.CardAbilityEvaluatorTests.Run(evaluator);
            if (success)
            {
                Console.WriteLine("[Admin Console] Card Ability Test Suite completed successfully!");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[Admin Console] ERROR: Card Ability Test Suite failed!");
                Console.ResetColor();
            }
            return;
        }

        if (primary == "runbattletests")
        {
            var evaluator = new CardAbilityEvaluator();
            var loggerFactory = new LoggerFactory();
            var logger = loggerFactory.CreateLogger<BattleEngine>();
            var battleEngine = new BattleEngine(logger, db, evaluator);
            var success = MwohServer.Tests.BattleEngineTests.Run(battleEngine, db);
            if (success)
            {
                Console.WriteLine("[Admin Console] S.H.I.E.L.D. Battle Engine Test Suite completed successfully!");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[Admin Console] ERROR: S.H.I.E.L.D. Battle Engine Test Suite failed!");
                Console.ResetColor();
            }
            return;
        }

        if (primary == "status")
        {
            var profiles = db.Profiles.Count();
            var cards = db.PlayerCards.Count();
            var items = db.PlayerInventoryItems.Count();
            Console.WriteLine($"[Admin Console] Status Check:");
            Console.WriteLine($"  - Total Profiles Registered: {profiles}");
            Console.WriteLine($"  - Total Cards in Stock: {cards}");
            Console.WriteLine($"  - Total Items Seeded: {items}");
            return;
        }

        if (primary == "reload")
        {
            MwohServer.Models.GameplaySettings.Load();
            Console.WriteLine("[Admin Console] Reloaded gameplay_settings.json configuration successfully.");
            return;
        }

        // Handles user-specific commands: <username> <action> <args...>
        if (args.Length < 2)
        {
            Console.WriteLine($"[Admin Console] Unknown or malformed command. Type 'help' for instructions.");
            return;
        }

        var username = args[0];
        var action = args[1].ToLower();

        var user = db.Users.Include(u => u.Profile).FirstOrDefault(u => u.Username.ToLower() == username.ToLower());
        if (user == null || user.Profile == null)
        {
            Console.WriteLine($"[Admin Console] Error: Player '{username}' not found.");
            return;
        }

        var profile = user.Profile;

        if (action == "addcurrency")
        {
            if (args.Length < 4)
            {
                Console.WriteLine("[Admin Console] Error: Syntax is '<username> addcurrency <silver|mobacoin> <amount>'");
                return;
            }

            var currencyType = args[2].ToLower();
            if (currencyType != "silver" && currencyType != "mobacoin")
            {
                Console.WriteLine("[Admin Console] Error: Currency type must be 'silver' or 'mobacoin'.");
                return;
            }

            if (!long.TryParse(args[3], out long amount))
            {
                Console.WriteLine("[Admin Console] Error: Amount must be a valid integer.");
                return;
            }

            if (currencyType == "silver")
            {
                long current = profile.SilverBalance;
                long next = current + amount;
                if (next > 999999999)
                {
                    Console.WriteLine($"[Admin Console] Error: Operation rejected. Overflows maximum capacity (999,999,999).");
                    return;
                }
                if (next < 0)
                {
                    Console.WriteLine($"[Admin Console] Error: Operation rejected. Underflows below zero.");
                    return;
                }
                profile.SilverBalance = next;
                db.SaveChanges();
                Console.WriteLine($"[Admin Console] Success: Granted {amount:N0} Silver to '{profile.Nickname}'. New Balance: {profile.SilverBalance:N0}");
            }
            else // mobacoin
            {
                long current = profile.MobaCoinBalance;
                long next = current + amount;
                if (next > 999999999)
                {
                    Console.WriteLine($"[Admin Console] Error: Operation rejected. Overflows maximum capacity (999,999,999).");
                    return;
                }
                if (next < 0)
                {
                    Console.WriteLine($"[Admin Console] Error: Operation rejected. Underflows below zero.");
                    return;
                }
                profile.MobaCoinBalance = next;
                db.SaveChanges();
                Console.WriteLine($"[Admin Console] Success: Granted {amount:N0} MobaCoins to '{profile.Nickname}'. New Balance: {profile.MobaCoinBalance:N0}");
            }
        }
        else if (action == "addcard")
        {
            if (args.Length < 3)
            {
                Console.WriteLine("[Admin Console] Error: Syntax is '<username> addcard <templateId> [lvl] [mst]'");
                return;
            }
            if (!int.TryParse(args[2], out int templateId))
            {
                Console.WriteLine("[Admin Console] Error: TemplateId must be an integer.");
                return;
            }

            var template = db.CardTemplates.FirstOrDefault(t => t.Id == templateId);
            if (template == null)
            {
                Console.WriteLine($"[Admin Console] Error: Card template ID {templateId} does not exist in database.");
                return;
            }

            // Count current cards to verify inventory cap
            var cardCount = db.PlayerCards.Count(pc => pc.PlayerProfileId == profile.Id);
            if (cardCount >= 250)
            {
                Console.WriteLine($"[Admin Console] Warning: Player stock is at capacity ({cardCount}/250). Card added regardless by override.");
            }

            var level = 1;
            if (args.Length >= 4 && int.TryParse(args[3], out int parsedLvl))
            {
                level = Math.Clamp(parsedLvl, 1, 100);
            }

            var mastery = 0;
            if (args.Length >= 5 && int.TryParse(args[4], out int parsedMst))
            {
                mastery = Math.Clamp(parsedMst, 0, template.MaxMastery <= 0 ? 100 : template.MaxMastery);
            }

            // Interpolate stats based on level
            var maxLevel = 100;
            var progress = maxLevel > 1 ? (double)(level - 1) / (maxLevel - 1) : 0.0;
            var baseAtk = template.BaseAtk;
            var baseDef = template.BaseDef;
            var maxAtk = template.MaxAtk;
            var maxDef = template.MaxDef;
            var newBaseAtk = (int)Math.Round(baseAtk + (maxAtk - baseAtk) * progress);
            var newBaseDef = (int)Math.Round(baseDef + (maxDef - baseDef) * progress);

            var maxMastery = template.MaxMastery <= 0 ? 100 : template.MaxMastery;
            var activeMasteryAtk = maxMastery > 0 ? (template.MasteryBonusAtk * mastery) / maxMastery : 0;
            var activeMasteryDef = maxMastery > 0 ? (template.MasteryBonusDef * mastery) / maxMastery : 0;

            var newCard = new MwohServer.Models.PlayerCard
            {
                PlayerProfileId = profile.Id,
                CardTemplateId = templateId,
                CurrentLevel = level,
                CurrentMastery = mastery,
                CurrentAtk = newBaseAtk + activeMasteryAtk,
                CurrentDef = newBaseDef + activeMasteryDef,
                AbilityLevel = 1,
                FusionBonusAtk = 0,
                FusionBonusDef = 0
            };

            db.PlayerCards.Add(newCard);
            db.SaveChanges();

            Console.WriteLine($"[Admin Console] Success: Spawned '{template.Title}' [LVL {level}, MST {mastery}%] for '{profile.Nickname}'.");
        }
        else if (action == "setlevel")
        {
            if (args.Length < 3 || !int.TryParse(args[2], out int newLvl) || newLvl < 1 || newLvl > 200)
            {
                Console.WriteLine("[Admin Console] Error: Level must be an integer between 1 and 200.");
                return;
            }

            profile.Level = newLvl;
            
            // Auto-scale Max Stamina dynamically
            var baseEnergy = 50;
            var energyMax = baseEnergy + (newLvl * MwohServer.Models.GameplaySettings.EnergyMaxIncreasePerLevel);
            profile.EnergyMax = energyMax;
            profile.EnergyCurrent = energyMax; // Refill energy fully
            
            db.SaveChanges();
            Console.WriteLine($"[Admin Console] Success: Set level of '{profile.Nickname}' to {newLvl}. Max Energy scaled to {energyMax}.");
        }
        else if (action == "resetattributes")
        {
            var isTestAgent = profile.Id == 1;
            var baselineEnergy = isTestAgent ? 120 : 100;
            var baselineAttack = isTestAgent ? 180 : 100;
            var baselineDefense = isTestAgent ? 150 : 100;
            var initialStatPoints = isTestAgent ? 15 : 0;
            
            var earnedPoints = 3 * (profile.Level - 1);
            var totalRefundedPoints = initialStatPoints + earnedPoints;
            
            profile.EnergyMax = baselineEnergy;
            profile.EnergyCurrent = baselineEnergy;
            profile.AttackPower = baselineAttack;
            profile.DefensePower = baselineDefense;
            profile.StatPoints = totalRefundedPoints;
            
            db.SaveChanges();
            Console.WriteLine($"[Admin Console] Success: Reset Attribute Points for player '{profile.Nickname}'.");
            Console.WriteLine($"  - Reverted baselines: Energy {baselineEnergy}, Attack Power {baselineAttack}, Defense Power {baselineDefense}.");
            Console.WriteLine($"  - Refunded Total Attribute Points: {totalRefundedPoints} PTS.");
        }
        else
        {
            Console.WriteLine($"[Admin Console] Unknown action '{action}'. Type 'help' for options.");
        }
    }
}

public class DualWriter : TextWriter
{
    private readonly TextWriter _original;
    private readonly string _filePath;
    private readonly object _lock = new object();

    public DualWriter(TextWriter original, string filePath)
    {
        _original = original;
        _filePath = filePath;
        
        try
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(filePath, $"--- Log Session Started: {DateTime.Now} ---\n");
        }
        catch
        {
            // Fail silently if unable to initialize log file at startup
        }
    }

    public override Encoding Encoding => _original.Encoding;

    public override void Write(char value)
    {
        _original.Write(value);
        try
        {
            lock (_lock)
            {
                File.AppendAllText(_filePath, value.ToString());
            }
        }
        catch {}
    }

    public override void Write(string? value)
    {
        _original.Write(value);
        if (value != null)
        {
            try
            {
                lock (_lock)
                {
                    File.AppendAllText(_filePath, value);
                }
            }
            catch {}
        }
    }

    public override void WriteLine(string? value)
    {
        _original.WriteLine(value);
        try
        {
            lock (_lock)
            {
                File.AppendAllText(_filePath, (value ?? "") + Environment.NewLine);
            }
        }
        catch {}
    }
}

