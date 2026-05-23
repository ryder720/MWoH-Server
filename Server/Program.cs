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
    options.UseSqlite("Data Source=mwoh.db"));

// Register Custom Services and Filters
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<GAuthValidationFilter>();

var app = builder.Build();

// 3. Automatically ensure SQLite database exists and is seeded
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<MwohDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Program>>();
    
    dbContext.Database.EnsureCreated();
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

