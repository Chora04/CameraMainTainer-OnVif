using System.Security.Claims;
using System.Globalization;
using System.Text.Json;
using System.Net;
using System.Net.Http.Json;
using CameraMaintainer.Core.Interfaces;
using CameraMaintainer.Core.Enums;
using CameraMaintainer.Core.Models;
using CameraMaintainer.Infrastructure.Data;
using CameraMaintainer.Infrastructure.Data.Repositories;
using CameraMaintainer.Services;
using CameraMaintainer.Web.Controllers;
using CameraMaintainer.Web.Hubs;
using CameraMaintainer.Web.Security;
using CameraMaintainer.Web.Models;
using CameraMaintainer.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CameraMaintainer.Tests;

public sealed class AlertAccessRegressionTests
{
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task HttpBoard_RendersAndPersistsStageMoves_WithRealSessionAuthorization()
    {
        await using var f = await Fixture.CreateAsync();
        f.Db.Credentials.Add(new Credential { CameraId = 1, Username = "camera-test", PasswordEnc = "test-encrypted-value" });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        Assert.Null(Assert.Single(await new CameraRepository(f.Db).GetAllAsync()).Credential);
        var webContentRoot = FindWebContentRoot();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(AlertsController).Assembly.FullName,
            ContentRootPath = webContentRoot
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(f.Db);
        builder.Services.AddSingleton(f.Board);
        var policyRepository = new CameraMonitoringPolicyRepository(f.Db);
        var frameArchive = new CameraFrameArchiveService(
            f.Db,
            policyRepository,
            Path.Combine(Path.GetTempPath(), "CameraMaintainer.Tests", Guid.NewGuid().ToString("N")),
            NullLogger<CameraFrameArchiveService>.Instance);
        builder.Services.AddSingleton(policyRepository);
        builder.Services.AddSingleton<ICameraFrameArchiveService>(frameArchive);
        builder.Services.AddSingleton<ICameraRepository>(new CameraRepository(f.Db));
        builder.Services.AddSingleton(new HealthCheckConcurrencyGate(2));
        builder.Services.AddSingleton<IHealthService>(new PageHealthService());
        builder.Services.AddSingleton<IAlertService>(f.Service);
        builder.Services.AddSingleton<IAlertHandoffService>(f.Handoffs);
        builder.Services.AddSingleton<EventBroadcaster>();
        builder.Services.AddSignalR();
        var sessions = new AccountSessionStore();
        builder.Services.AddSingleton(sessions);
        builder.Services.AddAuthentication(SessionAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(SessionAuthenticationHandler.SchemeName, _ => { });
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy(AccessPolicies.ViewCameras, policy => policy.RequireClaim(AccessPolicies.PermissionClaim, AccessPolicies.ViewCameras));
            options.AddPolicy(AccessPolicies.ManageCameras, policy => policy.RequireClaim(AccessPolicies.PermissionClaim, AccessPolicies.ManageCameras));
            options.AddPolicy(AccessPolicies.ViewDashboard, policy => policy.RequireClaim(AccessPolicies.PermissionClaim, AccessPolicies.ViewDashboard));
            options.AddPolicy(AccessPolicies.ViewMaintenance, policy => policy.RequireClaim(AccessPolicies.PermissionClaim, AccessPolicies.ViewMaintenance));
            options.AddPolicy(AccessPolicies.ViewAlerts, policy => policy.RequireClaim(AccessPolicies.PermissionClaim, AccessPolicies.ViewAlerts));
            options.AddPolicy(AccessPolicies.ViewAlertHistory, policy => policy.RequireClaim(AccessPolicies.PermissionClaim, AccessPolicies.ViewAlerts).RequireClaim(AccessPolicies.PermissionClaim, AccessPolicies.ViewAlertHistory));
            options.AddPolicy(AccessPolicies.WorkAlerts, policy => policy.RequireClaim(AccessPolicies.PermissionClaim, AccessPolicies.ViewAlerts).RequireClaim(AccessPolicies.PermissionClaim, AccessPolicies.RunChecks));
            options.AddPolicy(AccessPolicies.ManageUsers, policy => policy.RequireRole("Admin").RequireClaim(AccessPolicies.PermissionClaim, AccessPolicies.ManageUsers));
            options.AddPolicy(AccessPolicies.ManageMaintenance, policy => policy
                .RequireClaim(AccessPolicies.PermissionClaim, AccessPolicies.ViewCameras)
                .RequireClaim(AccessPolicies.PermissionClaim, AccessPolicies.ViewMaintenance)
                .RequireClaim(AccessPolicies.PermissionClaim, AccessPolicies.ManageCameras));
        });
        builder.Services.AddControllers(options => options.Filters.Add<WorkspaceChangeFilter>()).AddControllersAsServices();
        builder.Services.AddSingleton<SnapshotCache>();
        builder.Services.AddTransient<CamerasController>(provider => new(new CameraRepository(f.Db), null!, null!, null!, null!, null!, null!,
            NullLogger<CamerasController>.Instance, provider.GetRequiredService<SnapshotCache>()));
        builder.Services.AddRazorPages();
        await using var app = builder.Build();
        app.UseWebSockets();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapRazorPages();
        app.MapHub<EventsHub>("/hubs/events").RequireAuthorization();
        await app.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/hubs/events/negotiate?negotiateVersion=1", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/alerts/1/stage", new { stage = "waiting" })).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/settings/camera-monitoring")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/cameras/1/frames")).StatusCode);
            var adminSession = sessions.Create(12, TimeSpan.FromHours(1));
            var technicianSession = sessions.Create(11, TimeSpan.FromHours(1));
            client.DefaultRequestHeaders.Authorization = new("Bearer", adminSession);
            var cameraLocation = new CameraLocationInput { Latitude = 41.311081, Longitude = 69.240562, StreetAddress = "Тестовая улица, 7" };
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/cameras/1", new { location = new { latitude = 91, longitude = 69 } })).StatusCode);
            Assert.True((await client.PutAsJsonAsync("/api/cameras/1", new { location = cameraLocation })).IsSuccessStatusCode);
            f.Db.ChangeTracker.Clear();
            Assert.Equal(cameraLocation, (await client.GetFromJsonAsync<CameraSettingsDto>("/api/cameras/1/settings"))!.Location);
            Assert.True((await client.PutAsJsonAsync("/api/cameras/1", new { notes = "Location unchanged" })).IsSuccessStatusCode);
            f.Db.ChangeTracker.Clear();
            Assert.Equal(cameraLocation.StreetAddress, (await f.Db.Cameras.FindAsync(1))!.StreetAddress);
            client.DefaultRequestHeaders.Authorization = new("Bearer", technicianSession);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync("/api/cameras/1", new { location = cameraLocation })).StatusCode);
            client.DefaultRequestHeaders.Authorization = new("Bearer", adminSession);
            await using var adminSocket = await SignalRTestClient.ConnectAsync(client.BaseAddress, adminSession);
            await using var technicianSocket = await SignalRTestClient.ConnectAsync(client.BaseAddress, technicianSession);
            var monitoringPolicy = await client.GetFromJsonAsync<CameraMonitoringPolicy>("/api/settings/camera-monitoring");
            Assert.Equal(new CameraMonitoringPolicy(), monitoringPolicy);
            var changedPolicy = new CameraMonitoringPolicy { HealthyChecksRequired = 7, StableCheckEveryCycles = 3 };
            Assert.True((await client.PutAsJsonAsync("/api/settings/camera-monitoring", changedPolicy)).IsSuccessStatusCode);
            await adminSocket.ExpectWorkspaceAsync("settings");
            await technicianSocket.ExpectWorkspaceAsync("settings");
            Assert.Equal(changedPolicy, await client.GetFromJsonAsync<CameraMonitoringPolicy>("/api/settings/camera-monitoring"));
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/settings/camera-monitoring", changedPolicy with { HealthyChecksRequired = 0 })).StatusCode);
            foreach (var stage in new[] { "new", "assigned", "working", "waiting" })
            {
                var response = await client.PostAsJsonAsync("/api/alerts/1/stage", new { stage });
                Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
                f.Db.ChangeTracker.Clear();
                Assert.Equal(stage, (await f.Db.Alerts.AsNoTracking().SingleAsync(item => item.Id == 1)).WorkflowStage);
                await adminSocket.ExpectWorkspaceAsync("alerts");
                await technicianSocket.ExpectWorkspaceAsync("alerts");
            }
            var markup = await client.GetStringAsync("/Alerts?view=active");
            Assert.Contains("/js/localization", markup);
            Assert.Contains("data-language-selector", markup);
            Assert.Contains("value=\"ru\"", markup);
            Assert.Contains("value=\"uz-Latn\"", markup);
            Assert.Contains("value=\"uz-Cyrl\"", markup);
            Assert.Contains("data-alert-stage=\"waiting\"", markup);
            Assert.Contains("function initAlertDragAndDrop()", markup);
            Assert.Contains("document.addEventListener('dblclick'", markup);
            Assert.Contains("<button class=\"error-crm-summary-chevron\"", markup);
            Assert.Contains("if (openButton || event.detail === 0)", markup);
            Assert.Contains("class=\"queue-workspace\"", markup);
            Assert.Contains("function scrollAlertBoard(direction)", markup);
            var settingsMarkup = await client.GetStringAsync("/Settings/Monitoring");
            Assert.Contains("monitoring-healthy-checks", settingsMarkup);
            var cameraMarkup = await client.GetStringAsync("/Cameras");
            Assert.Contains("id=\"inspector-frame-image\"", cameraMarkup);
            Assert.Contains("id=\"metric-onvif\"", cameraMarkup);
            Assert.Contains("data-summary-value=\"total\"", cameraMarkup);
            Assert.DoesNotContain("class=\"incident-list\"", cameraMarkup);
            Assert.DoesNotContain("class=\"incident-section\"", cameraMarkup);
            Assert.DoesNotContain("title=\"Обзор\"", cameraMarkup);
            Assert.DoesNotContain("data-camera-card=", cameraMarkup);
            var historyStart = new DateTime(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc);
            var historyRecords = new[] {
                new Alert { Id = 101, CameraId = 1, Title = "Ошибка проверки камеры 1", AutoResolved = true, AssignedToUserId = 10,
                    Description = "timeout: A task was canceled.",
                    CreatedAt = historyStart, ResolvedAt = historyStart.AddHours(3), RecoveryDetectedAt = historyStart.AddHours(1),
                    Issues = [new AlertIssue { Title = "Network", Description = "timeout: A task was canceled.", Category = ErrorCategory.Technical, StartedAt = historyStart },
                        new AlertIssue { Title = "Lens", Category = ErrorCategory.Physical, StartedAt = historyStart }] },
                new Alert { Id = 102, CameraId = 1, Title = "Проблема с объективом камеры", AlertType = AlertType.LensObstructed, Category = ErrorCategory.Physical,
                    AutoResolved = true, AssignedToUserId = 10, CreatedAt = historyStart, ResolvedAt = historyStart.AddHours(3) },
                new Alert { Id = 103, Title = "System history", AutoResolved = true, AssignedToUserId = 11,
                    CreatedAt = historyStart, ResolvedAt = historyStart.AddMinutes(30) }
            };
            f.Db.Alerts.AddRange(historyRecords);
            await f.Db.SaveChangesAsync();
            var historyMarkup = await client.GetStringAsync("/Alerts?view=history");
            Assert.Contains("data-history-camera=\"1\"", historyMarkup);
            Assert.Contains("data-history-camera=\"system\"", historyMarkup);
            Assert.DoesNotContain("data-alert-id=\"101\"", historyMarkup);
            var folderMarkup = await client.GetStringAsync("/Alerts?view=history&camera=1");
            Assert.Contains("data-alert-id=\"101\"", folderMarkup);
            Assert.Contains("data-alert-id=\"102\"", folderMarkup);
            Assert.DoesNotContain("data-alert-id=\"103\"", folderMarkup);
            Assert.Contains("Превышено время ожидания ответа камеры.", WebUtility.HtmlDecode(folderMarkup));
            Assert.DoesNotContain("timeout: A task was canceled.", folderMarkup);
            client.DefaultRequestHeaders.Authorization = new("Bearer", technicianSession);
            var technicianHistory = await client.GetStringAsync("/Alerts?view=history");
            Assert.DoesNotContain("data-history-camera=\"1\"", technicianHistory);
            Assert.DoesNotContain("data-alert-id=\"101\"", await client.GetStringAsync("/Alerts?view=history&camera=1"));
            client.DefaultRequestHeaders.Authorization = new("Bearer", adminSession);
            f.Db.Alerts.RemoveRange(historyRecords);
            await f.Db.SaveChangesAsync();
            using var landingClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = client.BaseAddress };
            landingClient.DefaultRequestHeaders.Authorization = new("Bearer", adminSession);
            var landing = await landingClient.GetAsync("/");
            Assert.Equal(HttpStatusCode.Redirect, landing.StatusCode);
            Assert.Equal("/Cameras", landing.Headers.Location?.OriginalString);
            Assert.DoesNotContain("camera-test", cameraMarkup);
            Assert.DoesNotContain("test-encrypted-value", cameraMarkup);
            foreach (var unsafeUrl in new[] { "//example.invalid", "/\\example.invalid", "https://example.invalid", "/\n/example.invalid" })
            {
                var loginPage = await client.GetStringAsync("/Login?returnUrl=" + Uri.EscapeDataString(unsafeUrl));
                Assert.Contains("const returnUrl = \"/\";", loginPage);
            }
            var localReturnPage = await client.GetStringAsync("/Login?returnUrl=" + Uri.EscapeDataString("/Alerts?view=active"));
            Assert.Contains("const returnUrl = \"/Alerts?view=active\";", localReturnPage);
            Assert.Contains("/js/localization", localReturnPage);
            Assert.Contains("data-language-selector", localReturnPage);
            await frameArchive.RecordAsync(new CameraFrameCapture
            {
                CameraId = 1,
                CapturedAt = new DateTime(2026, 10, 15, 5, 30, 0, DateTimeKind.Utc),
                DirectionStatus = "Normal",
                ObstructionType = "none",
                MeanBrightness = 100,
                BrightnessStdDev = 35,
                Entropy = 6.4,
                EdgeDensity = 8,
                TextureUniformityScore = 40,
                DirectionConfidence = 0.9,
                EncodedImage = [0xFF, 0xD8, 0xFF, 0xD9]
            });
            var frameApi = await client.GetFromJsonAsync<JsonElement[]>("/api/cameras/1/frames")
                ?? throw new InvalidDataException("Frame history API returned an empty response body.");
            Assert.Single(frameApi);
            Assert.False(frameApi[0].GetProperty("hasImage").GetBoolean());
            var detailMarkup = await client.GetStringAsync("/Cameras/Detail/1");
            Assert.Contains("class=\"frame-history-item normal\"", detailMarkup);
            var usersMarkup = await client.GetStringAsync("/Users");
            var keys = (await f.Board.GetColumnsAsync()).Select(column => column.Key).Reverse().ToArray();
            Assert.True((await client.PutAsJsonAsync("/api/alert-board/columns/reorder", new { keys })).IsSuccessStatusCode);
            Assert.Equal(keys, (await f.Board.GetColumnsAsync()).Select(column => column.Key));
            await adminSocket.ExpectWorkspaceAsync("alerts");
            await technicianSocket.ExpectWorkspaceAsync("alerts");
            var beforeAccess = await client.GetFromJsonAsync<JsonElement>("/api/workspace/access");
            client.DefaultRequestHeaders.Authorization = new("Bearer", technicianSession);
            var technicianAccess = await client.GetFromJsonAsync<JsonElement>("/api/workspace/access");
            Assert.NotEqual(beforeAccess.GetProperty("version").GetString(), technicianAccess.GetProperty("version").GetString());
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/alerts/1")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync("/api/settings/camera-monitoring", changedPolicy)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync("/api/alert-board/columns/reorder", new { keys })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/alerts/1/stage", new { stage = "working" })).StatusCode);
            var techMarkup = await client.GetStringAsync("/Alerts?view=active");
            Assert.DoesNotContain("ERR-1</span>", techMarkup);
            var technician = await f.Db.AppUsers.SingleAsync(user => user.Id == 11);
            technician.CanViewDashboard = false;
            technician.CanViewAlerts = false;
            await f.Db.SaveChangesAsync();
            var camerasOnly = await client.GetStringAsync("/Cameras");
            Assert.Contains("data-summary-value=\"total\"", camerasOnly);
            Assert.DoesNotContain("class=\"incident-section\"", camerasOnly);
            landingClient.DefaultRequestHeaders.Authorization = new("Bearer", technicianSession);
            Assert.Equal("/Cameras", (await landingClient.GetAsync("/")).Headers.Location?.OriginalString);
            technician.CanViewDashboard = true;
            technician.CanViewCameras = false;
            technician.CanViewAlerts = true;
            await f.Db.SaveChangesAsync();
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/Cameras")).StatusCode);
            Assert.Equal("/Alerts?view=active", (await landingClient.GetAsync("/")).Headers.Location?.OriginalString);
            await WriteRenderedMarkupAsync(new Dictionary<string, string>
            {
                ["alerts-admin"] = markup,
                ["alerts-technician"] = techMarkup,
                ["settings-admin"] = settingsMarkup,
                ["cameras-admin"] = cameraMarkup,
                ["cameras-technician"] = camerasOnly,
                ["history-admin"] = historyMarkup,
                ["history-camera-admin"] = folderMarkup,
                ["detail-admin"] = detailMarkup,
                ["users-admin"] = usersMarkup,
                ["login"] = localReturnPage
            });
        }
        finally { await app.StopAsync(); }
    }

    private static async Task WriteRenderedMarkupAsync(IReadOnlyDictionary<string, string> pages)
    {
        var outputDirectory = Environment.GetEnvironmentVariable("CAMERAMAINTAINER_RENDERED_MARKUP_DIR");
        if (string.IsNullOrWhiteSpace(outputDirectory)) return;

        Directory.CreateDirectory(outputDirectory);
        foreach (var (name, markup) in pages)
        {
            await File.WriteAllTextAsync(Path.Combine(outputDirectory, name + ".html"), markup);
        }
    }

    private static string FindWebContentRoot(
        [System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
    {
        var sourceDirectory = Path.GetDirectoryName(sourceFilePath)
            ?? throw new DirectoryNotFoundException("Could not locate the test source directory.");
        foreach (var startPath in new[] { sourceDirectory, Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(startPath); directory is not null; directory = directory.Parent)
            {
                var candidate = Path.Combine(directory.FullName, "src", "CameraMaintainer.Web");
                if (File.Exists(Path.Combine(directory.FullName, "CameraMaintainer.slnx")) &&
                    Directory.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        throw new DirectoryNotFoundException("Could not locate the CameraMaintainer.Web content root.");
    }

    [Fact]
    public void ClosedNewCard_IsNotSharedHistory()
    {
        var alert = new Alert { WorkflowStage = "new" };
        Assert.True(AlertVisibilityPolicy.IsVisibleToOperator(alert, 11, "tech"));
        alert.AutoResolved = true;
        Assert.False(AlertVisibilityPolicy.IsVisibleToOperator(alert, 11, "tech"));
    }

    [Fact]
    public async Task MergedLink_ResolvesCanonicalCardButDoesNotBypassOwnership()
    {
        await using var f = await Fixture.CreateAsync();
        await f.HideWorkingFromTechnician();
        (await f.Db.Alerts.FindAsync(2))!.MergedIntoAlertId = 1;
        await f.Db.SaveChangesAsync();
        var resolved = JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(await f.Controller(10).GetById(2)).Value);
        Assert.Equal(1, resolved.GetProperty("alert").GetProperty("Id").GetInt32());
        Assert.Equal(403, Assert.IsType<ObjectResult>(await f.Controller(13).GetById(2)).StatusCode);
    }

    [Fact]
    public void AssignedId_TakesPrecedenceOverReusedUsername()
    {
        var alert = new Alert { WorkflowStage = "working", AssignedToUserId = 10, AssignedToUsername = "tech" };
        Assert.False(AlertVisibilityPolicy.IsVisibleToOperator(alert, 11, "tech"));
    }

    [Fact]
    public async Task HiddenHandoff_OpensOnlyTransferredCard_NotColumnOrOtherCards()
    {
        await using var f = await Fixture.CreateAsync();
        await f.HideWorkingFromTechnician();
        var handoff = await f.Handoffs.CreateAsync(1, 11, 1, "Replace power supply", 24, 10);
        var controller = f.Controller(11);

        var details = JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(await controller.GetById(1)).Value,
            WebJsonOptions);
        Assert.False(details.GetProperty("canEdit").GetBoolean());
        Assert.False(details.GetProperty("canCreateHandoff").GetBoolean());
        Assert.Empty(details.GetProperty("stages").EnumerateArray());
        Assert.Empty(Assert.IsType<List<Alert>>(Assert.IsType<OkObjectResult>(await controller.GetAll(filter: "active")).Value));
        Assert.Equal(403, Assert.IsType<ObjectResult>(await controller.GetById(2)).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await f.Controller(13).GetById(1)).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await controller.MoveStage(1, new() { Stage = "waiting" })).StatusCode);

        Assert.IsType<OkObjectResult>(await controller.GetHandoffs(1));
        Assert.IsType<OkObjectResult>(await controller.AcceptHandoff(1, handoff.Id, new() { Comment = "Accepted" }));
        Assert.NotNull(handoff.ViewedAt);
        Assert.NotNull(handoff.AcceptedAt);
        Assert.Equal("working", (await f.Db.Alerts.FindAsync(1))!.WorkflowStage);
        Assert.IsType<OkObjectResult>(await controller.GetHistory(1));
    }

    [Fact]
    public async Task FullCard_ReturnsOnlyPermittedActionsAndStages()
    {
        await using var f = await Fixture.CreateAsync();
        var columns = await f.Board.GetColumnsAsync();
        columns.Single(column => column.Key == "working").VisibleToRoles = ["Admin", "Operator"];
        columns.Single(column => column.Key == "waiting").VisibleToRoles = ["Admin"];
        await f.Board.SaveColumnsAsync(columns);
        var details = JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(await f.Controller(10).GetById(1)).Value,
            WebJsonOptions);
        Assert.True(details.GetProperty("canEdit").GetBoolean());
        Assert.True(details.GetProperty("canCreateHandoff").GetBoolean());
        Assert.False(details.GetProperty("canAssignSelf").GetBoolean());
        Assert.Equal("192.0.2.1", details.GetProperty("cameraIp").GetString());
        Assert.DoesNotContain(details.GetProperty("stages").EnumerateArray(), item => item.GetProperty("key").GetString() == "waiting");

        (await f.Db.Alerts.FindAsync(1))!.AutoResolved = true;
        await f.Db.SaveChangesAsync();
        var closed = JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(await f.Controller(10).GetById(1)).Value,
            WebJsonOptions);
        Assert.False(closed.GetProperty("canEdit").GetBoolean());
        Assert.Equal("Закрыта", closed.GetProperty("stageTitle").GetString());
        Assert.Empty(closed.GetProperty("stages").EnumerateArray());
    }

    [Fact]
    public async Task MismatchedHandoffId_Returns404BeforeAnyMutation()
    {
        await using var f = await Fixture.CreateAsync();
        var handoff = await f.Handoffs.CreateAsync(1, 11, 1, "Check", 24, 10);
        var other = (await f.Db.Alerts.FindAsync(2))!;
        other.AssignedToUserId = 11;
        await f.Db.SaveChangesAsync();
        var count = await f.Db.AlertAuditEvents.CountAsync();

        Assert.IsType<NotFoundObjectResult>(await f.Controller(11).AcceptHandoff(2, handoff.Id, new()));

        Assert.Equal(AlertHandoffStates.Pending, handoff.State);
        Assert.Null(handoff.AcceptedAt);
        Assert.Null(handoff.ViewedAt);
        Assert.Equal(count, await f.Db.AlertAuditEvents.CountAsync());
        Assert.Null((await f.Db.UserNotifications.SingleAsync()).ReadAt);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task RevokedWorkPermission_BlocksAccept(bool canView, bool canWork)
    {
        await using var f = await Fixture.CreateAsync();
        var handoff = await f.Handoffs.CreateAsync(1, 11, 1, "Check", 24, 10);
        var user = (await f.Db.AppUsers.FindAsync(11))!;
        user.CanViewAlerts = canView;
        user.CanRunChecks = canWork;
        await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Handoffs.AcceptAsync(handoff.Id, null, 11));
        Assert.Equal(AlertHandoffStates.Pending, handoff.State);
    }

    [Fact]
    public async Task RecipientWithoutAlertsAccess_CannotReceiveWork()
    {
        await using var f = await Fixture.CreateAsync();
        (await f.Db.AppUsers.FindAsync(11))!.CanViewAlerts = false;
        await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Handoffs.CreateAsync(1, 11, 1, "Check", 24, 10));
        Assert.Empty(f.Db.AlertHandoffs);
        Assert.Empty(f.Db.UserNotifications);
    }

    [Fact]
    public async Task HandoffAudit_ContainsCommentsActorsAndTimestamps_InCameraHistoryJson()
    {
        await using var f = await Fixture.CreateAsync();
        var handoff = await f.Handoffs.CreateAsync(1, 11, 1, "Replace power supply", 24, 10);
        await f.Handoffs.ViewAsync(handoff.Id, 11);
        await f.Handoffs.AcceptAsync(handoff.Id, "Parts ordered", 11);
        await f.Handoffs.CompleteAsync(handoff.Id, "Replaced and tested", 11);
        var audits = await f.Db.AlertAuditEvents.OrderBy(item => item.Id).ToListAsync();
        Assert.Equal(new[] { "handoff_created", "handoff_viewed", "handoff_accepted", "handoff_completed" }, audits.Select(item => item.EventType));
        using var completed = JsonDocument.Parse(audits.Last().NewValuesJson!);
        var detail = completed.RootElement.GetProperty("handoff");
        Assert.Equal("Replaced and tested", detail.GetProperty("completionComment").GetString());
        Assert.Equal("Parts ordered", detail.GetProperty("acceptanceComment").GetString());
        Assert.Equal("Replace power supply", detail.GetProperty("comment").GetString());
        Assert.Equal(11, detail.GetProperty("toUserId").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, detail.GetProperty("acceptedAt").ValueKind);
        var response = Assert.IsType<OkObjectResult>(await f.Controller(12).GetCameraHistory(1));
        var json = JsonSerializer.SerializeToElement(response.Value, WebJsonOptions);
        var history = json.EnumerateArray().Single(item => item.GetProperty("id").GetInt32() == 1);
        Assert.Equal(4, history.GetProperty("auditEvents").GetArrayLength());
        Assert.Equal("working", history.GetProperty("workflowStage").GetString());
    }

    [Fact]
    public async Task ClosedPersonalHistory_RequiresHistoryPermission()
    {
        await using var f = await Fixture.CreateAsync();
        await f.Handoffs.CreateAsync(1, 11, 1, "Check", 24, 10);
        (await f.Db.Alerts.FindAsync(1))!.AutoResolved = true;
        (await f.Db.AppUsers.FindAsync(11))!.CanViewAlertHistory = false;
        await f.Db.SaveChangesAsync();
        Assert.Equal(403, Assert.IsType<ObjectResult>(await f.Controller(11).GetHistory(1)).StatusCode);
    }

    [Fact]
    public async Task SqlScope_MatchesOwnershipAndAccountColumns()
    {
        await using var f = await Fixture.CreateAsync();
        var tech = (await f.Db.AppUsers.FindAsync(11))!;
        f.Db.Alerts.AddRange(
            new Alert { Id = 3, WorkflowStage = "new", Title = "Shared intake" },
            new Alert { Id = 4, WorkflowStage = "new", AutoResolved = true, Title = "Private closed intake" },
            new Alert { Id = 5, WorkflowStage = "working", AssignedToUserId = 11, Title = "Own work" },
            new Alert { Id = 6, WorkflowStage = "working", AssignedToUserId = 10, AssignedToUsername = tech.Username, Title = "Reused username" });
        await f.Db.SaveChangesAsync();
        var columns = await f.Board.GetColumnsAsync();
        var query = AlertVisibilityPolicy.ApplyToQuery(f.Db.Alerts, tech, columns);
        Assert.Equal(new[] { 3, 5 }, await query.OrderBy(item => item.Id).Select(item => item.Id).ToArrayAsync());
        tech.AlertBoardColumnKeys = "new";
        Assert.Equal(new[] { 3 }, await AlertVisibilityPolicy.ApplyToQuery(f.Db.Alerts, tech, columns).Select(item => item.Id).ToArrayAsync());
    }

    [Fact]
    public async Task PaginationAndSummary_DoNotLoseOldActiveCardsAfter5000ClosedRecords()
    {
        await using var f = await Fixture.CreateAsync();
        f.Db.Alerts.AddRange(Enumerable.Range(3, 5001).Select(id => new Alert
        {
            Id = id,
            Title = "Archived",
            AutoResolved = true,
            ResolvedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            WorkflowStage = "ready"
        }));
        await f.Db.SaveChangesAsync();
        var controller = f.Controller(12);
        var active = Assert.IsType<List<Alert>>(Assert.IsType<OkObjectResult>(await controller.GetAll(filter: "active")).Value);
        Assert.Equal(new[] { 1, 2 }, active.Select(item => item.Id).OrderBy(id => id));
        var last = Assert.IsType<List<Alert>>(Assert.IsType<OkObjectResult>(await controller.GetAll(page: 101, perPage: 50)).Value);
        Assert.Equal(3, last.Count);
        var summary = JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(await controller.GetSummary()).Value);
        Assert.Equal(2, summary.GetProperty("active").GetInt32());
        Assert.Equal(5001, summary.GetProperty("history").GetInt32());
    }

    [Fact]
    public async Task RecoveryNotification_IsReadOnceRemainsUntilClosureAndRespectsPreferences()
    {
        await using var f = await Fixture.CreateAsync();
        var repository = new AlertRepository(f.Db);
        await repository.MarkConnectivityRecoveredAsync(1);
        var controller = f.Notifications(10);
        var feed = Assert.IsType<AlertNotificationFeedDto>(Assert.IsType<OkObjectResult>((await controller.GetNotifications()).Result).Value);
        var notification = feed.Items.Single(item => item.AlertId == 1 && item.Type == "recovery");
        Assert.False(notification.IsRead);
        await controller.MarkAsRead(new() { NotificationId = notification.NotificationId });

        await repository.MarkConnectivityRecoveredAsync(1);
        (await f.Db.Alerts.FindAsync(1))!.UpdatedAt = DateTime.UtcNow.AddHours(1);
        await f.Db.SaveChangesAsync();
        feed = Assert.IsType<AlertNotificationFeedDto>(Assert.IsType<OkObjectResult>((await controller.GetNotifications()).Result).Value);
        Assert.True(feed.Items.Single(item => item.NotificationId == notification.NotificationId).IsRead);
        var otherAccount = Assert.IsType<AlertNotificationFeedDto>(Assert.IsType<OkObjectResult>((await f.Notifications(13).GetNotifications()).Result).Value);
        Assert.Empty(otherAccount.Items);

        await controller.UpdatePreferences(new() { ReviewRequestsEnabled = false });
        feed = Assert.IsType<AlertNotificationFeedDto>(Assert.IsType<OkObjectResult>((await controller.GetNotifications()).Result).Value);
        Assert.DoesNotContain(feed.Items, item => item.Type == "recovery");
        await controller.UpdatePreferences(new() { ReviewRequestsEnabled = true });
        (await f.Db.Alerts.FindAsync(1))!.AutoResolved = true;
        await f.Db.SaveChangesAsync();
        feed = Assert.IsType<AlertNotificationFeedDto>(Assert.IsType<OkObjectResult>((await controller.GetNotifications()).Result).Value);
        Assert.DoesNotContain(feed.Items, item => item.AlertId == 1);
    }

    [Fact]
    public async Task IssueChat_IsStoredPerIssueAndCountsAsWorkReportForClosure()
    {
        await using var f = await Fixture.CreateAsync();
        var issue = new AlertIssue
        {
            AlertId = 1,
            AlertType = AlertType.CameraOffline,
            Category = ErrorCategory.Technical,
            Severity = AlertSeverity.Warning,
            Title = "Camera offline",
            StartedAt = DateTime.UtcNow.AddHours(-1)
        };
        f.Db.AlertIssues.Add(issue);
        await f.Db.SaveChangesAsync();

        var response = await f.Controller(12).AddIssueMessage(1, issue.Id,
            new AddAlertIssueMessageDto { Message = "Проверил питание и соединение." });
        var ok = response as OkObjectResult;
        Assert.True(ok != null, JsonSerializer.Serialize((response as ObjectResult)?.Value));
        var saved = Assert.IsType<AlertIssueMessage>(ok.Value);
        Assert.Equal(issue.Id, saved.AlertIssueId);
        Assert.Equal("User 12", saved.AuthorDisplayName);
        Assert.Single(await f.Db.AlertIssueMessages.Where(item => item.AlertIssueId == issue.Id).ToListAsync());

        await f.Service.MoveAlertAsync(1, "review", 12);
        await f.Service.MoveAlertAsync(1, "ready", 12);
        await f.Service.ResolveAlertAsync(1, 12);
        var resolved = await f.Db.AlertIssues.AsNoTracking().SingleAsync(item => item.Id == issue.Id);
        Assert.NotNull(resolved.RecoveredAt);
        Assert.Equal(12, resolved.ResolvedByUserId);
        Assert.Equal("User 12", resolved.ResolvedByDisplayName);
    }

    [Fact]
    public async Task CalendarNote_IsPrivateByDefault_AndUsesPersonalReadStateUntilCompleted()
    {
        await using var f = await Fixture.CreateAsync();
        var controller = f.Notes(10);
        var calendarJson = await GetWorkspaceNoteFeedAsync(controller);
        Assert.True(calendarJson.GetProperty("canEdit").GetBoolean());
        Assert.False(calendarJson.GetProperty("canShare").GetBoolean());
        var created = Assert.IsType<WorkspaceNote>(Assert.IsType<OkObjectResult>(await controller.Create(new()
        {
            Title = "Проверить резервный коммутатор",
            Body = "Согласовать окно работ",
            Tone = "warning",
            ScheduledFor = DateTimeOffset.UtcNow.AddHours(2)
        })).Value);
        Assert.False(created.IsVisibleToAll);
        Assert.Null(created.RecipientUserId);

        Assert.Empty((await GetWorkspaceNoteFeedAsync(f.Notes(11))).GetProperty("items").EnumerateArray());
        Assert.Empty((await GetWorkspaceNoteFeedAsync(f.Notes(12))).GetProperty("items").EnumerateArray());
        Assert.IsType<NotFoundObjectResult>(await f.Notes(11).Update(created.Id, new()));
        Assert.IsType<NotFoundObjectResult>(await f.Notes(11).Complete(created.Id, new()));
        Assert.IsType<NotFoundObjectResult>(await f.Notes(11).Delete(created.Id));

        var forbiddenShare = Assert.IsType<ObjectResult>(await controller.Create(new()
        {
            Title = "Недопустимая общая заметка",
            Tone = "info",
            ScheduledFor = DateTimeOffset.UtcNow.AddHours(3),
            IsVisibleToAll = true
        }));
        Assert.Equal(StatusCodes.Status403Forbidden, forbiddenShare.StatusCode);

        var notifications = f.Notifications(10);
        var feed = Assert.IsType<AlertNotificationFeedDto>(Assert.IsType<OkObjectResult>(
            (await notifications.GetNotifications()).Result).Value);
        var item = Assert.Single(feed.Items, item => item.Type == "calendar_note");
        Assert.Null(item.AlertId);
        Assert.False(item.IsRead);
        await notifications.MarkAsRead(new() { NotificationId = item.NotificationId });
        feed = Assert.IsType<AlertNotificationFeedDto>(Assert.IsType<OkObjectResult>(
            (await notifications.GetNotifications()).Result).Value);
        Assert.True(feed.Items.Single(entry => entry.NotificationId == item.NotificationId).IsRead);

        await notifications.UpdatePreferences(new() { CalendarNotesEnabled = false });
        feed = Assert.IsType<AlertNotificationFeedDto>(Assert.IsType<OkObjectResult>(
            (await notifications.GetNotifications()).Result).Value);
        Assert.DoesNotContain(feed.Items, entry => entry.NotificationId == item.NotificationId);
        await notifications.UpdatePreferences(new() { CalendarNotesEnabled = true });

        await controller.Complete(created.Id, new() { Completed = true });
        feed = Assert.IsType<AlertNotificationFeedDto>(Assert.IsType<OkObjectResult>(
            (await notifications.GetNotifications()).Result).Value);
        Assert.DoesNotContain(feed.Items, entry => entry.NotificationId == item.NotificationId);
    }

    [Fact]
    public async Task CalendarNote_AdminCanAddressOneAccount_WithoutGrantingModification()
    {
        await using var f = await Fixture.CreateAsync();
        var admin = f.Notes(12);
        var adminFeed = await GetWorkspaceNoteFeedAsync(admin);
        Assert.True(adminFeed.GetProperty("canShare").GetBoolean());
        Assert.Contains(adminFeed.GetProperty("recipients").EnumerateArray(),
            recipient => recipient.GetProperty("id").GetInt32() == 11);

        var created = Assert.IsType<WorkspaceNote>(Assert.IsType<OkObjectResult>(await admin.Create(new()
        {
            Title = "Проверить узел связи",
            Body = "Заметка для назначенного техника",
            Tone = "warning",
            ScheduledFor = DateTimeOffset.UtcNow.AddHours(2),
            RecipientUserId = 11
        })).Value);
        Assert.Equal(11, created.RecipientUserId);
        Assert.Equal("user11", created.RecipientUsername);
        Assert.False(created.IsVisibleToAll);

        var recipientItems = (await GetWorkspaceNoteFeedAsync(f.Notes(11)))
            .GetProperty("items").EnumerateArray().ToArray();
        var recipientItem = Assert.Single(recipientItems);
        Assert.False(recipientItem.GetProperty("canModify").GetBoolean());
        Assert.Empty((await GetWorkspaceNoteFeedAsync(f.Notes(10))).GetProperty("items").EnumerateArray());
        Assert.IsType<NotFoundObjectResult>(await f.Notes(11).Update(created.Id, new()));
        Assert.IsType<NotFoundObjectResult>(await f.Notes(11).Complete(created.Id, new()));
        Assert.IsType<NotFoundObjectResult>(await f.Notes(11).Delete(created.Id));

        var recipientNotifications = Assert.IsType<AlertNotificationFeedDto>(Assert.IsType<OkObjectResult>(
            (await f.Notifications(11).GetNotifications()).Result).Value);
        Assert.Contains(recipientNotifications.Items, item => item.Type == "calendar_note");
        var otherNotifications = Assert.IsType<AlertNotificationFeedDto>(Assert.IsType<OkObjectResult>(
            (await f.Notifications(10).GetNotifications()).Result).Value);
        Assert.DoesNotContain(otherNotifications.Items, item => item.Type == "calendar_note");
    }

    [Fact]
    public async Task CalendarNote_AdminCanShareWithEveryone()
    {
        await using var f = await Fixture.CreateAsync();
        var created = Assert.IsType<WorkspaceNote>(Assert.IsType<OkObjectResult>(await f.Notes(12).Create(new()
        {
            Title = "Общее обслуживание",
            Tone = "info",
            ScheduledFor = DateTimeOffset.UtcNow.AddHours(2),
            IsVisibleToAll = true
        })).Value);
        Assert.True(created.IsVisibleToAll);
        Assert.Null(created.RecipientUserId);

        foreach (var userId in new[] { 10, 11, 12, 13 })
        {
            var item = Assert.Single((await GetWorkspaceNoteFeedAsync(f.Notes(userId)))
                .GetProperty("items").EnumerateArray().ToArray());
            Assert.Equal(userId == 12, item.GetProperty("canModify").GetBoolean());
        }

        Assert.IsType<OkObjectResult>(await f.Notes(12).Complete(created.Id, new() { Completed = true }));
    }

    private static async Task<JsonElement> GetWorkspaceNoteFeedAsync(WorkspaceNotesController controller)
    {
        var result = Assert.IsType<OkObjectResult>(await controller.Get(null, null));
        return JsonSerializer.SerializeToElement(result.Value, WebJsonOptions);
    }

    [Fact]
    public async Task Recovery_PreservesOpenHandoffAndRequiresItsCompletionBeforeClosure()
    {
        await using var f = await Fixture.CreateAsync();
        await f.Handoffs.CreateAsync(1, 11, 1, "Check wiring", 24, 10);
        await new AlertRepository(f.Db).MarkConnectivityRecoveredAsync(1);

        Assert.Equal(AlertHandoffStates.Pending, (await f.Db.AlertHandoffs.SingleAsync()).State);
        Assert.Null((await f.Db.UserNotifications.SingleAsync()).ResolvedAt);
        Assert.Equal("ready", (await f.Db.Alerts.FindAsync(1))!.WorkflowStage);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.ResolveAlertAsync(1, 10));
    }

    [Fact]
    public async Task CheckQueueApi_PhysicalFaultIsDueAndNotDeferred()
    {
        await using var f = await Fixture.CreateAsync();
        var alert = (await f.Db.Alerts.FindAsync(1))!;
        alert.Category = ErrorCategory.Physical;
        var camera = (await f.Db.Cameras.FindAsync(1))!;
        camera.LastCheckAt = DateTime.UtcNow.AddHours(-2);
        camera.CheckIntervalSeconds = 3600;
        await f.Db.SaveChangesAsync();
        using var gate = new HealthCheckConcurrencyGate(2);
        var controller = new HealthController(new PageHealthService(), new CameraRepository(f.Db), null!, gate);

        var body = JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(await controller.GetCheckQueue()).Value);
        var item = body.GetProperty("items").EnumerateArray().Single();
        Assert.True(item.GetProperty("HasPhysicalFault").GetBoolean());
        Assert.True(item.GetProperty("DueNow").GetBoolean());
        Assert.Equal("due", item.GetProperty("QueueState").GetString());
    }

    [Fact]
    public async Task HiddenHandoffNotification_ReadRemainsVisibleUntilResolved()
    {
        await using var f = await Fixture.CreateAsync();
        await f.HideWorkingFromTechnician();
        await f.Handoffs.CreateAsync(1, 11, 1, "Check", 24, 10);
        var controller = f.Notifications(11);
        var feed = Assert.IsType<AlertNotificationFeedDto>(Assert.IsType<OkObjectResult>((await controller.GetNotifications()).Result).Value);
        var notification = Assert.Single(feed.Items);
        Assert.Contains("alert=1", notification.Url);
        Assert.False(notification.IsRead);
        await controller.MarkAsRead(new() { NotificationId = notification.NotificationId });
        feed = Assert.IsType<AlertNotificationFeedDto>(Assert.IsType<OkObjectResult>((await controller.GetNotifications()).Result).Value);
        Assert.True(Assert.Single(feed.Items).IsRead);
        Assert.Equal(0, feed.Count);
        (await f.Db.Alerts.FindAsync(1))!.AutoResolved = true;
        await f.Db.SaveChangesAsync();
        feed = Assert.IsType<AlertNotificationFeedDto>(Assert.IsType<OkObjectResult>((await controller.GetNotifications()).Result).Value);
        Assert.Empty(feed.Items);
    }

    [Fact]
    public async Task NotificationPreferences_ArePersonalAndDisableHandoffs()
    {
        await using var f = await Fixture.CreateAsync();
        await f.Handoffs.CreateAsync(1, 11, 1, "Check", 24, 10);
        await f.Notifications(11).UpdatePreferences(new() { HandoffsEnabled = false, SoundEnabled = false, PopupEnabled = false });
        var feed = Assert.IsType<AlertNotificationFeedDto>(Assert.IsType<OkObjectResult>((await f.Notifications(11).GetNotifications()).Result).Value);
        Assert.Empty(feed.Items);
        Assert.False(feed.SoundEnabled);
        Assert.False(feed.PopupEnabled);
        var preferences = Assert.IsType<AlertNotificationPreferencesDto>(Assert.IsType<OkObjectResult>((await f.Notifications(10).GetPreferences()).Result).Value);
        Assert.True(preferences.HandoffsEnabled);
        Assert.True(preferences.SoundEnabled);
        Assert.True(preferences.PopupEnabled);
    }

    [Fact]
    public async Task CompletedHandoffNotification_StaysGrayUntilMainCardIsResolved()
    {
        await using var f = await Fixture.CreateAsync();
        var handoff = await f.Handoffs.CreateAsync(1, 11, 1, "Check", 24, 10);
        await f.Handoffs.AcceptAsync(handoff.Id, null, 11);
        await f.Handoffs.CompleteAsync(handoff.Id, "Done", 11);
        var notification = await f.Db.UserNotifications.SingleAsync(item => item.UserId == 10);
        await f.Notifications(10).MarkAsRead(new() { NotificationId = $"n:{notification.Id}" });
        Assert.Null(notification.ResolvedAt);
        Assert.Null(notification.ReminderAt);
        var feed = Assert.IsType<AlertNotificationFeedDto>(Assert.IsType<OkObjectResult>((await f.Notifications(10).GetNotifications()).Result).Value);
        Assert.Contains(feed.Items, item => item.NotificationId == $"n:{notification.Id}" && item.IsRead);
    }

    [Fact]
    public async Task HandoffReminder_ReturnsReadNotificationToUnread()
    {
        await using var f = await Fixture.CreateAsync();
        await f.Handoffs.CreateAsync(1, 11, 1, "Check", 24, 10);
        var notification = await f.Db.UserNotifications.SingleAsync();
        notification.ReadAt = DateTime.UtcNow.AddDays(-2);
        notification.ReminderAt = DateTime.UtcNow.AddMinutes(-1);
        await f.Db.SaveChangesAsync();
        var feed = Assert.IsType<AlertNotificationFeedDto>(Assert.IsType<OkObjectResult>((await f.Notifications(11).GetNotifications()).Result).Value);
        Assert.False(Assert.Single(feed.Items).IsRead);
        Assert.Equal(1, notification.ReminderCount);
        Assert.True(notification.ReminderAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task InvalidCategory_DoesNotMutateAssignmentOrAudit()
    {
        await using var f = await Fixture.CreateAsync();
        var alert = (await f.Db.Alerts.FindAsync(1))!;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.UpdateAlertWorkAsync(1, (ErrorCategory)999, "Report", false, 10));
        Assert.Equal(10, alert.AssignedToUserId);
        Assert.Null(alert.WorkReport);
        Assert.Empty(f.Db.AlertAuditEvents);
        Assert.IsType<BadRequestObjectResult>(await f.Controller(10).UpdateWork(1, new() { Category = "999" }));
    }

    [Fact]
    public async Task ManagerCanReviewOwnCard_TechnicianCannotApprove()
    {
        await using var f = await Fixture.CreateAsync();
        var alert = (await f.Db.Alerts.FindAsync(1))!;
        alert.WorkflowStage = "review";
        alert.WorkReport = "Checked";
        await f.Db.SaveChangesAsync();
        await f.Service.MoveAlertAsync(1, "ready", 10);
        Assert.Equal("ready", alert.WorkflowStage);
        await f.Service.ResolveAlertAsync(1, 10);
        Assert.True(alert.AutoResolved);
    }

    [Fact]
    public async Task MovingBackToWork_KeepsExistingReport()
    {
        await using var f = await Fixture.CreateAsync();
        var alert = (await f.Db.Alerts.FindAsync(1))!;
        alert.AssignedToUserId = 11;
        alert.WorkflowStage = "waiting";
        alert.WorkReport = "Parts arrived";
        await f.Db.SaveChangesAsync();
        await f.Service.MoveAlertAsync(1, "working", 11);
        Assert.Equal("Parts arrived", alert.WorkReport);
    }

    [Fact]
    public async Task DeletingCustomColumn_AuditsActiveMigration_LeavesClosedHistoryIntact()
    {
        await using var f = await Fixture.CreateAsync();
        await f.Board.AddColumnAsync(new() { Key = "custom", Title = "Waiting", Color = "#808080" });
        var active = (await f.Db.Alerts.FindAsync(1))!;
        var closed = (await f.Db.Alerts.FindAsync(2))!;
        active.WorkflowStage = closed.WorkflowStage = "custom";
        closed.AutoResolved = true;
        await f.Db.SaveChangesAsync();
        await f.Board.DeleteColumnAsync("custom", "waiting", 12);
        Assert.Equal("waiting", active.WorkflowStage);
        Assert.Equal("custom", closed.WorkflowStage);
        var audit = await f.Db.AlertAuditEvents.SingleAsync();
        Assert.Equal(12, audit.ActorUserId);
        Assert.Equal("custom", audit.FromStage);
        Assert.Equal("waiting", audit.ToStage);
    }

    [Theory]
    [InlineData("new")]
    [InlineData("assigned")]
    [InlineData("working")]
    [InlineData("review")]
    [InlineData("ready")]
    public async Task RequiredWorkflowStages_CannotBeDeleted(string key)
    {
        await using var f = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Board.DeleteColumnAsync(key, "waiting", 12));
        Assert.Contains(await f.Board.GetColumnsAsync(), column => column.Key == key);
    }

    [Fact]
    public async Task Retention_KeepsAnniversaryDayAndActiveCases_ReusesExpiredHistoryOnly()
    {
        await using var f = await Fixture.CreateAsync();
        var closed = (await f.Db.Alerts.FindAsync(1))!;
        closed.AutoResolved = true;
        closed.ResolvedAt = new DateTime(2026, 8, 26, 10, 0, 0, DateTimeKind.Utc);
        await f.Db.SaveChangesAsync();
        var retention = new AlertRetentionService(f.Db);
        var before = await retention.CleanupAsync(new DateTime(2026, 11, 26, 18, 59, 0, DateTimeKind.Utc));
        Assert.Equal(0, before.DeletedAlerts);
        var after = await retention.CleanupAsync(new DateTime(2026, 11, 26, 19, 0, 0, DateTimeKind.Utc));
        Assert.Equal(1, after.DeletedAlerts);
        Assert.Equal(2, (await f.Db.Alerts.AsNoTracking().SingleAsync()).Id);
    }

    private sealed class PageHealthService : IHealthService
    {
        public Task<HealthSummary> GetHealthSummaryAsync() => Task.FromResult(new HealthSummary { Total = 1, Unknown = 1 });
        public Task<HealthRecord?> GetCurrentHealthAsync(int cameraId) => Task.FromResult<HealthRecord?>(null);
        public Task<HealthRecord> CheckCameraAsync(int cameraId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Render tests must not contact cameras.");
        public Task CheckAllDueAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException("Render tests must not run checks.");
    }

    [Fact]
    public async Task HistoryFolders_AggregateAllVisibleClosedCasesAndIssues_InSql()
    {
        await using var f = await Fixture.CreateAsync();
        var start = new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc);
        f.Db.Alerts.AddRange(Enumerable.Range(100, 45).Select(id => new Alert
        {
            Id = id,
            CameraId = 1,
            AssignedToUserId = 10,
            AutoResolved = true,
            CreatedAt = start,
            ResolvedAt = start.AddHours(5),
            RecoveryDetectedAt = start.AddHours(2),
            Issues = [new AlertIssue { Category = ErrorCategory.Technical, StartedAt = start },
                new AlertIssue { Category = ErrorCategory.Technical, StartedAt = start },
                new AlertIssue { Category = ErrorCategory.Physical, StartedAt = start }]
        }));
        f.Db.Alerts.AddRange(
            new Alert
            {
                Id = 200,
                CameraId = 1,
                AutoResolved = true,
                AssignedToUserId = 10,
                Category = ErrorCategory.Physical,
                CreatedAt = start,
                ResolvedAt = start.AddHours(4)
            },
            new Alert { Id = 201, AutoResolved = true, CreatedAt = start },
            new Alert { Id = 202, AutoResolved = true, CreatedAt = start, ResolvedAt = start.AddHours(-1) });
        await f.Db.SaveChangesAsync();
        var admin = await f.Db.AppUsers.FindAsync(12);
        var query = AlertVisibilityPolicy.ApplyToQuery(f.Db.Alerts.AsNoTracking(), admin!, await f.Board.GetColumnsAsync());
        var folders = CameraHistoryFolder.From(query);
        Assert.Equal(2, await folders.CountAsync());
        var camera = await folders.SingleAsync(item => item.CameraId == 1);
        Assert.Equal(46, camera.Cases);
        Assert.Equal(90, camera.Technical);
        Assert.Equal(46, camera.Physical);
        Assert.Equal((45 * 7200d + 14400) / 46, camera.AverageRepairSeconds!.Value, 1);
        Assert.Null((await folders.SingleAsync(item => item.CameraId == null)).AverageRepairSeconds);
        var technician = await f.Db.AppUsers.FindAsync(11);
        Assert.Empty(await CameraHistoryFolder.From(AlertVisibilityPolicy.ApplyToQuery(f.Db.Alerts.AsNoTracking(), technician!, await f.Board.GetColumnsAsync())).ToListAsync());
    }

    [Theory]
    [InlineData(null, "Нет данных")]
    [InlineData(-1d, "Нет данных")]
    [InlineData(0d, "< 1 мин")]
    [InlineData(3600d, "1 ч 0 мин")]
    [InlineData(90000d, "1 д 1 ч")]
    public void HistoryFolders_FormatRepairDuration(double? seconds, string expected) =>
        Assert.Equal(expected, CameraHistoryFolder.FormatDuration(seconds));

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _provider;
        public AppDbContext Db { get; }
        public AlertBoardRepository Board { get; }
        public AlertService Service { get; }
        public AlertHandoffService Handoffs { get; }
        private readonly EventBroadcaster _events;

        private Fixture(SqliteConnection connection, AppDbContext db)
        {
            _connection = connection;
            Db = db;
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSignalR();
            _provider = services.BuildServiceProvider();
            _events = new(_provider.GetRequiredService<IHubContext<EventsHub>>(), NullLogger<EventBroadcaster>.Instance);
            Board = new(db);
            Service = new(new AlertRepository(db), NullLogger<AlertService>.Instance, Board);
            Handoffs = new(db, NullLogger<AlertHandoffService>.Instance);
        }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            db.AppUsers.AddRange(User(10, "Operator"), User(11, "Technician"), User(12, "Admin"), User(13, "Technician"));
            db.Cameras.Add(new Camera { Id = 1, Name = "Camera 1", IpAddress = "192.0.2.1", ServiceUrl = "http://192.0.2.1/onvif/device_service" });
            db.AlertHandoffReasons.Add(new() { Id = 1, Key = "parts", Name = "Parts", IsActive = true });
            db.Alerts.AddRange(Enumerable.Range(1, 2).Select(id => new Alert
            {
                Id = id,
                CameraId = 1,
                Title = "Camera offline",
                AlertType = AlertType.CameraOffline,
                AssignedToUserId = 10,
                AssignedToUsername = "user10",
                WorkflowStage = "working",
                CreatedAt = DateTime.UtcNow.AddDays(-2),
                UpdatedAt = DateTime.UtcNow
            }));
            await db.SaveChangesAsync();
            return new(connection, db);
        }

        public async Task HideWorkingFromTechnician()
        {
            var columns = await Board.GetColumnsAsync();
            columns.Single(item => item.Key == "working").VisibleToRoles = ["Admin", "Operator"];
            await Board.SaveColumnsAsync(columns);
        }

        public AlertsController Controller(int id) => new(Service, Handoffs, Db, Board, _events, NullLogger<AlertsController>.Instance)
        { ControllerContext = Context(id) };
        public NotificationsController Notifications(int id) => new(Db, Board) { ControllerContext = Context(id) };
        public WorkspaceNotesController Notes(int id) => new(Db, _events) { ControllerContext = Context(id) };
        private ControllerContext Context(int id)
        {
            var user = Db.AppUsers.Find(id)!;
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, id.ToString(CultureInfo.InvariantCulture)),
                new(ClaimTypes.Name, user.Username), new(ClaimTypes.Role, user.Role),
                new(AccessPolicies.PermissionClaim, AccessPolicies.ViewAlerts), new(AccessPolicies.PermissionClaim, AccessPolicies.RunChecks)
            };
            if (user.CanViewAlertHistory) claims.Add(new(AccessPolicies.PermissionClaim, AccessPolicies.ViewAlertHistory));
            if (user.CanManageUsers) claims.Add(new(AccessPolicies.PermissionClaim, AccessPolicies.ManageUsers));
            return new() { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) } };
        }

        private static AppUser User(int id, string role) => new()
        {
            Id = id,
            Username = "user" + id,
            DisplayName = "User " + id,
            Role = role,
            CanRunChecks = true,
            CanViewAlerts = true,
            CanViewAlertHistory = true,
            CanManageUsers = role == "Admin",
            CanManageCameras = role == "Admin"
        };

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
            await _provider.DisposeAsync();
        }
    }
}
