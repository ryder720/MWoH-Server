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

    // 5. Ensure healthy default Attack/Defense deck capacity limits for all profiles
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

app.Run();

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

