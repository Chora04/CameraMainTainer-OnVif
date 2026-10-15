using CameraMaintainer.Core.Interfaces;
using CameraMaintainer.Core.Models;
using CameraMaintainer.Infrastructure.CircuitBreaker;
using CameraMaintainer.Infrastructure.Data;
using CameraMaintainer.Infrastructure.Data.Repositories;
using CameraMaintainer.Infrastructure.Security;
using CameraMaintainer.Onvif;
using CameraMaintainer.Services;
using CameraMaintainer.Web.Hubs;
using CameraMaintainer.Web.Security;
using CameraMaintainer.Workers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff zzz ";
    options.IncludeScopes = true;
});
if (builder.Environment.IsDevelopment())
    builder.Logging.AddDebug();

// Keep the native UI contract stable even when the executable is launched
// from a working directory where appsettings.json is not discovered.
if (string.IsNullOrWhiteSpace(builder.Configuration["Urls"]) &&
    string.IsNullOrWhiteSpace(builder.Configuration["ASPNETCORE_URLS"]))
{
    builder.WebHost.UseUrls("http://0.0.0.0:5100");
}

// ---- Configuration ----
var dbPath = builder.Configuration.GetValue<string>("Database:Path");
if (string.IsNullOrEmpty(dbPath))
{
    var dataDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    if (string.IsNullOrWhiteSpace(dataDirectory))
        dataDirectory = AppContext.BaseDirectory;

    dataDirectory = Path.Combine(dataDirectory, "CameraMaintainer");
    Directory.CreateDirectory(dataDirectory);
    dbPath = Path.Combine(dataDirectory, "cameramaintainer.db");

    // Migrate installations that kept the database beside the executable.
    var legacyDbPath = Path.Combine(AppContext.BaseDirectory, "cameramaintainer.db");
    if (!File.Exists(dbPath) && File.Exists(legacyDbPath) &&
        !string.Equals(Path.GetFullPath(dbPath), Path.GetFullPath(legacyDbPath), StringComparison.OrdinalIgnoreCase))
    {
        File.Copy(legacyDbPath, dbPath);
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            var legacySidecar = legacyDbPath + suffix;
            if (File.Exists(legacySidecar))
                File.Copy(legacySidecar, dbPath + suffix);
        }
    }
}
dbPath = Path.GetFullPath(dbPath);
var dbDirectory = Path.GetDirectoryName(dbPath);
if (!string.IsNullOrWhiteSpace(dbDirectory))
    Directory.CreateDirectory(dbDirectory);
var frameArchivePath = builder.Configuration.GetValue<string>("Snapshots:ArchivePath");
if (string.IsNullOrWhiteSpace(frameArchivePath))
    frameArchivePath = Path.Combine(dbDirectory ?? AppContext.BaseDirectory, "camera-frames");
else if (!Path.IsPathRooted(frameArchivePath))
    frameArchivePath = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, frameArchivePath));

var encryptionKey = builder.Configuration.GetValue<string>("Encryption:Key");
if (string.IsNullOrEmpty(encryptionKey))
{
    var keyPath = builder.Configuration.GetValue<string>("Encryption:KeyFile");
    if (string.IsNullOrEmpty(keyPath))
        keyPath = Path.ChangeExtension(dbPath, ".key");

    var legacyKeyPath = Path.Combine(AppContext.BaseDirectory, "cameramaintainer.key");
    if (!File.Exists(keyPath) && File.Exists(legacyKeyPath) &&
        !string.Equals(Path.GetFullPath(keyPath), Path.GetFullPath(legacyKeyPath), StringComparison.OrdinalIgnoreCase))
    {
        var keyDirectory = Path.GetDirectoryName(keyPath);
        if (!string.IsNullOrEmpty(keyDirectory))
            Directory.CreateDirectory(keyDirectory);
        File.Copy(legacyKeyPath, keyPath);
    }

    if (File.Exists(keyPath))
    {
        encryptionKey = (await File.ReadAllTextAsync(keyPath)).Trim();
    }
    else
    {
        encryptionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var keyDirectory = Path.GetDirectoryName(keyPath);
        if (!string.IsNullOrEmpty(keyDirectory))
            Directory.CreateDirectory(keyDirectory);
        await File.WriteAllTextAsync(keyPath, encryptionKey);
    }
}
var healthParallelism = CameraCheckParallelism.Detect();
var healthConcurrency = healthParallelism.MaxConcurrency;
var healthCycleSeconds = builder.Configuration.GetValue<int>("Health:CycleIntervalSeconds", 60);
var discoveryIntervalMinutes = builder.Configuration.GetValue<int>("Discovery:IntervalMinutes", 5);
var capabilityCacheTtlMinutes = builder.Configuration.GetValue<int>("Onvif:CapabilityCacheTtlMinutes", 30);

// ---- Database ----
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlite($"Data Source={dbPath};Mode=ReadWriteCreate;Cache=Shared;Pooling=True;Default Timeout=15");
});

// ---- Security ----
builder.Services.AddSingleton<ICredentialEncryptor>(new AesCredentialEncryptor(encryptionKey));
builder.Services.AddSingleton<AccountSessionStore>();
builder.Services
    .AddAuthentication(SessionAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(
        SessionAuthenticationHandler.SchemeName,
        _ => { });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AccessPolicies.ViewDashboard,
        policy => policy.RequireClaim(AccessPolicies.PermissionClaim, AccessPolicies.ViewDashboard));
    options.AddPolicy(AccessPolicies.ViewCameras,
        policy => policy.RequireClaim(AccessPolicies.PermissionClaim, AccessPolicies.ViewCameras));
    options.AddPolicy(AccessPolicies.ViewAlerts,
        policy => policy.RequireClaim(AccessPolicies.PermissionClaim, AccessPolicies.ViewAlerts));
    options.AddPolicy(AccessPolicies.ViewAlertHistory,
        policy => policy
            .RequireClaim(AccessPolicies.PermissionClaim, AccessPolicies.ViewAlerts)
            .RequireClaim(AccessPolicies.PermissionClaim, AccessPolicies.ViewAlertHistory));
    options.AddPolicy(AccessPolicies.ViewMaintenance,
        policy => policy
            .RequireClaim(AccessPolicies.PermissionClaim, AccessPolicies.ViewCameras)
            .RequireClaim(AccessPolicies.PermissionClaim, AccessPolicies.ViewMaintenance));
    options.AddPolicy(AccessPolicies.RunChecks,
        policy => policy.RequireClaim(AccessPolicies.PermissionClaim, AccessPolicies.RunChecks));
    options.AddPolicy(AccessPolicies.ManageCameras,
        policy => policy
            .RequireClaim(AccessPolicies.PermissionClaim, AccessPolicies.ViewCameras)
            .RequireClaim(AccessPolicies.PermissionClaim, AccessPolicies.ManageCameras));
    options.AddPolicy(AccessPolicies.ManageUsers,
        policy => policy
            .RequireRole("Admin")
            .RequireClaim(AccessPolicies.PermissionClaim, AccessPolicies.ManageUsers));
    options.AddPolicy(AccessPolicies.CheckCameras,
        policy => policy
            .RequireClaim(AccessPolicies.PermissionClaim, AccessPolicies.ViewCameras)
            .RequireClaim(AccessPolicies.PermissionClaim, AccessPolicies.RunChecks));
    options.AddPolicy(AccessPolicies.WorkAlerts,
        policy => policy
            .RequireClaim(AccessPolicies.PermissionClaim, AccessPolicies.ViewAlerts)
            .RequireClaim(AccessPolicies.PermissionClaim, AccessPolicies.RunChecks));
    options.AddPolicy(AccessPolicies.ManageMaintenance,
        policy => policy
            .RequireClaim(AccessPolicies.PermissionClaim, AccessPolicies.ViewCameras)
            .RequireClaim(AccessPolicies.PermissionClaim, AccessPolicies.ViewMaintenance)
            .RequireClaim(AccessPolicies.PermissionClaim, AccessPolicies.ManageCameras));
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("authentication", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "local",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 8,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

// ---- Circuit Breaker (with exponential backoff) ----
builder.Services.AddSingleton(new CircuitBreaker(
    failureThreshold: 3,
    cooldownSeconds: 120,
    backoffMultiplier: 1.5,
    maxCooldownSeconds: 1800));

// ---- ONVIF Capability Cache ----
builder.Services.AddSingleton<IOnvifCapabilityCache>(sp =>
    new OnvifCapabilityCache(
        sp.GetRequiredService<ILogger<OnvifCapabilityCache>>(),
        capabilityCacheTtlMinutes));

// ---- ONVIF ----
builder.Services.AddHttpClient("Onvif", client => client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddHttpClient("CameraSnapshots", client => client.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddHttpClient("CameraGeocoding", client =>
{
    client.Timeout = TimeSpan.FromSeconds(8);
    client.MaxResponseContentBufferSize = 64 * 1024;
    client.DefaultRequestHeaders.UserAgent.ParseAdd("CameraMaintainer/1.0 (camera-location-editor)");
});
builder.Services.AddSingleton<CameraMaintainer.Web.Services.CameraGeocodingService>();
builder.Services.AddSingleton<OnvifClient>(sp => new OnvifClient(
    httpClient: sp.GetRequiredService<IHttpClientFactory>().CreateClient("Onvif"),
    capabilityCache: sp.GetRequiredService<IOnvifCapabilityCache>()));
builder.Services.AddSingleton<IOnvifClient>(sp => sp.GetRequiredService<OnvifClient>());
// ---- Frame Processing (TurboJPEG via SkiaSharp) ----
builder.Services.AddSingleton<IFrameProcessor>(sp => new FrameProcessor(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("CameraSnapshots"),
    sp.GetRequiredService<ILogger<FrameProcessor>>()));

// ---- Camera Direction Analyzer ----
builder.Services.AddSingleton(_ => new CameraMaintainer.Web.Services.SnapshotCache(
    maximumBytes: Math.Clamp(builder.Configuration.GetValue("Snapshots:CacheMegabytes", 128), 16, 512) * 1024L * 1024,
    concurrentDownloads: builder.Configuration.GetValue("Snapshots:ConcurrentDownloads", 4)));
builder.Services.AddSingleton<ICameraDirectionAnalyzer, CameraDirectionAnalyzer>();

// ---- Repositories ----
builder.Services.AddScoped<ICameraRepository, CameraRepository>();
builder.Services.AddScoped<HealthRepository>();
builder.Services.AddScoped<AlertRepository>();
builder.Services.AddScoped<AlertBoardRepository>();
builder.Services.AddScoped<AlertRetentionService>();
builder.Services.AddScoped<CameraMonitoringPolicyRepository>();
builder.Services.AddScoped<ICameraFrameArchiveService>(sp => new CameraFrameArchiveService(
    sp.GetRequiredService<AppDbContext>(),
    sp.GetRequiredService<CameraMonitoringPolicyRepository>(),
    frameArchivePath,
    sp.GetRequiredService<ILogger<CameraFrameArchiveService>>()));

// ---- Services ----
builder.Services.AddScoped<IDiscoveryService, DiscoveryService>();
builder.Services.AddScoped<IAlertService, AlertService>();
builder.Services.AddScoped<IAlertHandoffService, AlertHandoffService>();
var firmwareBaselines = builder.Configuration
    .GetSection("Firmware:Baselines")
    .GetChildren()
    .Where(item => item.Value != null)
    .ToDictionary(item => item.Key, item => item.Value!, StringComparer.OrdinalIgnoreCase);
builder.Services.AddScoped<IFirmwareService>(sp => new FirmwareService(
    sp.GetRequiredService<AppDbContext>(),
    sp.GetRequiredService<ILogger<FirmwareService>>(),
    firmwareBaselines));
builder.Services.AddSingleton(healthParallelism);
builder.Services.AddSingleton(new HealthCheckConcurrencyGate(healthConcurrency));

// PTZ Auto-Correction (sweeps camera when wall obstruction detected)
builder.Services.AddScoped<PtzAutoCorrectionService>();

builder.Services.AddSingleton<SystemMetricsService>();

// LensObstructionDetector — now uses FrameProcessor + DirectionAnalyzer
builder.Services.AddScoped<LensObstructionDetector>(sp => new LensObstructionDetector(
    sp.GetRequiredService<IFrameProcessor>(),
    sp.GetRequiredService<ICameraDirectionAnalyzer>(),
    sp.GetRequiredService<ILogger<LensObstructionDetector>>()));

builder.Services.AddScoped<IAutoRecoveryService, AutoRecoveryService>();

builder.Services.AddScoped<IHealthService>(sp =>
{
    return new HealthService(
        sp.GetRequiredService<ICameraRepository>(),
        sp.GetRequiredService<HealthRepository>(),
        sp.GetRequiredService<AlertRepository>(),
        sp.GetRequiredService<OnvifClient>(),
        sp.GetRequiredService<IAutoRecoveryService>(),
        sp.GetRequiredService<IAlertService>(),
        sp.GetRequiredService<ICredentialEncryptor>(),
        sp.GetRequiredService<CircuitBreaker>(),
        sp.GetRequiredService<LensObstructionDetector>(),
        sp.GetRequiredService<IFrameProcessor>(),
        sp.GetRequiredService<ICameraDirectionAnalyzer>(),
        sp.GetRequiredService<IFirmwareService>(),
        sp.GetRequiredService<PtzAutoCorrectionService>(),
        sp.GetRequiredService<HealthCheckConcurrencyGate>(),
        sp.GetRequiredService<ILogger<HealthService>>(),
        sp.GetRequiredService<IServiceScopeFactory>(),
        sp.GetRequiredService<ICameraFrameArchiveService>()
    );
});
builder.Services.AddScoped<IPtzService, PtzService>();
builder.Services.AddScoped<IConfigService, ConfigService>();
builder.Services.AddScoped<IMaintenanceService>(sp => new MaintenanceService(
    sp.GetRequiredService<ICameraRepository>(),
    sp.GetRequiredService<AppDbContext>(),
    sp.GetRequiredService<OnvifClient>(),
    sp.GetRequiredService<ICredentialEncryptor>(),
    sp.GetRequiredService<ILogger<MaintenanceService>>(),
    sp.GetRequiredService<IMonitoringEvents>()));

// ---- Workers (background services) ----
builder.Services.AddHostedService(sp =>
    new DiscoveryWorker(sp.GetRequiredService<IServiceScopeFactory>(),
        sp.GetRequiredService<ILogger<DiscoveryWorker>>(),
        discoveryIntervalMinutes));

builder.Services.AddHostedService(sp =>
    new HealthWorker(sp.GetRequiredService<IServiceScopeFactory>(),
        sp.GetRequiredService<ILogger<HealthWorker>>(),
        healthCycleSeconds));

builder.Services.AddHostedService<BackupWorker>();
builder.Services.AddHostedService<FirmwareCheckWorker>();
builder.Services.AddHostedService<AlertRetentionWorker>();

// ---- Web ----
builder.Services.AddControllers(options => options.Filters.Add<WorkspaceChangeFilter>());
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        if (context.ProblemDetails.Status >= StatusCodes.Status500InternalServerError)
        {
            context.ProblemDetails.Title = "Операция не выполнена";
            context.ProblemDetails.Detail = null;
        }
    };
});
builder.Services.AddSignalR();
builder.Services.AddSingleton<EventBroadcaster>();
builder.Services.AddSingleton<IMonitoringEvents>(sp => sp.GetRequiredService<EventBroadcaster>());
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToPage("/Login");
});

var app = builder.Build();
_ = app.Services.GetRequiredService<SystemMetricsService>();

// ---- Apply database migrations ----
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var bootstrap = await LegacyDatabaseBootstrapper.PrepareAsync(db);
    if (bootstrap.BaselineCreated)
    {
        app.Logger.LogInformation(
            "Existing database registered as the EF migration baseline. Backup: {BackupPath}",
            bootstrap.BackupPath);
    }

    await db.Database.MigrateAsync();
    await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");

    if (!await db.AppUsers.AnyAsync(x => x.Username == "admin"))
    {
        var initialAdminPassword = builder.Configuration["BootstrapAdmin:Password"] ??
            Environment.GetEnvironmentVariable("CAMERAMAINTAINER_ADMIN_PASSWORD");
        if (string.IsNullOrWhiteSpace(initialAdminPassword))
        {
            if (!app.Environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    "Set CAMERAMAINTAINER_ADMIN_PASSWORD before the first production start.");
            }

            initialAdminPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
            app.Logger.LogWarning(
                "Development bootstrap administrator created with temporary password {TemporaryPassword}. " +
                "Change it immediately.",
                initialAdminPassword);
        }

        var password = PasswordHasher.Hash(initialAdminPassword);
        db.AppUsers.Add(new AppUser
        {
            Username = "admin",
            DisplayName = "Главный администратор",
            PasswordHash = password.Hash,
            PasswordSalt = password.Salt,
            Role = "Admin",
            CanViewDashboard = true,
            CanViewCameras = true,
            CanViewAlerts = true,
            CanViewAlertHistory = true,
            CanViewMaintenance = true,
            CanRunChecks = true,
            CanManageCameras = true,
            CanManageUsers = true,
            IsActive = true
        });
        await db.SaveChangesAsync();
    }
}

// ---- Middleware pipeline ----
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler();
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "DENY";
    context.Response.Headers["Referrer-Policy"] = "same-origin";

    // Dynamic pages and API responses must always reflect the latest database state.
    if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) ||
        !Path.HasExtension(context.Request.Path.Value ?? string.Empty))
    {
        context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers.Expires = "0";
    }

    await next();
});
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers().RequireAuthorization();
app.MapHub<EventsHub>("/hubs/events").RequireAuthorization();
app.MapRazorPages();
app.MapFallback((HttpContext context) =>
{
    var normalizedPath = context.Request.Path.Value?.TrimEnd('.', '/');
    var destination = string.Equals(normalizedPath, "/Alerts", StringComparison.OrdinalIgnoreCase)
        ? "/Alerts" + context.Request.QueryString
        : "/";
    return Results.Redirect(destination);
});

app.Logger.LogInformation("CameraMaintainer starting...");
app.Logger.LogInformation("Database: {DbPath}", dbPath);
app.Logger.LogInformation("Health: concurrency={Concurrency}, cycle={Cycle}s",
    healthConcurrency, healthCycleSeconds);
app.Logger.LogInformation("Health CPU policy: physical cores={PhysicalCores}, available logical CPUs={LogicalCpus}, checks/core={ChecksPerCore}, source={Source}",
    healthParallelism.PhysicalCoreCount, healthParallelism.LogicalProcessorCount,
    CameraCheckParallelism.ChecksPerCore, healthParallelism.DetectionSource);
if (healthParallelism.Warning != null)
    app.Logger.LogWarning("CPU topology detection failed; using one core / two check slots: {Reason}", healthParallelism.Warning);
if (builder.Configuration["Health:Concurrency"] != null)
    app.Logger.LogWarning("Health:Concurrency is ignored: check concurrency is always two times the detected available physical-core count.");
app.Logger.LogInformation("ONVIF: capability cache TTL={CacheTtl}min", capabilityCacheTtlMinutes);

await app.RunAsync();
