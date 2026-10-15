using CameraMaintainer.Core.Enums;
using CameraMaintainer.Core.Models;
using CameraMaintainer.Infrastructure.Data;
using CameraMaintainer.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CameraMaintainer.Tests;

public sealed class AlertHandoffWorkflowTests
{
    [Fact]
    public async Task Create_KeepsBoardStageAndCreatesPersistentNotification()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var handoff = await fixture.Service.CreateAsync(
            fixture.AlertId, 11, fixture.ReasonId, "Нужна замена блока питания", 12, 10);

        var alert = await fixture.Db.Alerts.SingleAsync(item => item.Id == fixture.AlertId);
        var notification = await fixture.Db.UserNotifications.SingleAsync();
        Assert.Equal("working", alert.WorkflowStage);
        Assert.Equal(AlertHandoffStates.Pending, handoff.State);
        Assert.Equal(11, handoff.ToUserId);
        Assert.Equal(handoff.Id, notification.HandoffId);
        Assert.Equal(11, notification.UserId);
        Assert.Null(notification.ReadAt);
        Assert.Null(notification.ResolvedAt);
        Assert.Single(fixture.Db.AlertAuditEvents, item => item.EventType == "handoff_created");
    }

    [Fact]
    public async Task Accept_IsAllowedOnlyForRecipientAndKeepsNotificationUnresolved()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var handoff = await fixture.Service.CreateAsync(
            fixture.AlertId, 11, fixture.ReasonId, "Проверьте питание", 24, 10);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => fixture.Service.AcceptAsync(handoff.Id, null, 12));

        var accepted = await fixture.Service.AcceptAsync(handoff.Id, "Принял в работу", 11);
        var notification = await fixture.Db.UserNotifications.SingleAsync();
        Assert.Equal(AlertHandoffStates.Accepted, accepted.State);
        Assert.NotNull(accepted.ViewedAt);
        Assert.NotNull(accepted.AcceptedAt);
        Assert.NotNull(notification.ReadAt);
        Assert.Null(notification.ResolvedAt);
    }

    [Fact]
    public async Task Forward_CreatesLinkedChainWithoutMovingBoardCard()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var first = await fixture.Service.CreateAsync(
            fixture.AlertId, 11, fixture.ReasonId, "Нужна проверка техника", 24, 10);
        await fixture.Service.AcceptAsync(first.Id, null, 11);

        var next = await fixture.Service.ForwardAsync(
            first.Id, 12, fixture.ReasonId, "Нужно согласование администратора", 8, 11);

        var storedFirst = await fixture.Db.AlertHandoffs.SingleAsync(item => item.Id == first.Id);
        var alert = await fixture.Db.Alerts.SingleAsync(item => item.Id == fixture.AlertId);
        Assert.Equal(AlertHandoffStates.Forwarded, storedFirst.State);
        Assert.Equal(first.Id, next.ParentHandoffId);
        Assert.Equal(12, next.ToUserId);
        Assert.Equal("working", alert.WorkflowStage);
        Assert.Single(fixture.Db.UserNotifications, item =>
            item.UserId == 12 && !item.ResolvedAt.HasValue);
    }

    [Fact]
    public async Task Complete_ResolvesRecipientNotificationAndNotifiesSender()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var handoff = await fixture.Service.CreateAsync(
            fixture.AlertId, 11, fixture.ReasonId, "Заменить блок питания", 24, 10);
        await fixture.Service.AcceptAsync(handoff.Id, null, 11);

        var completed = await fixture.Service.CompleteAsync(
            handoff.Id, "Блок заменён, камера работает", 11);

        var notifications = await fixture.Db.UserNotifications.OrderBy(item => item.Id).ToListAsync();
        Assert.Equal(AlertHandoffStates.Completed, completed.State);
        Assert.NotNull(notifications[0].ResolvedAt);
        Assert.Equal(10, notifications[1].UserId);
        Assert.Equal("handoff_completed", notifications[1].Type);
        Assert.Null(notifications[1].ResolvedAt);
    }

    [Fact]
    public async Task ReasonCannotBeDisabledWhileUsedByOpenHandoff()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.Service.CreateAsync(
            fixture.AlertId, 11, fixture.ReasonId, "Ожидается комплектующая", 24, 10);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.SaveReasonAsync(
                fixture.ReasonId, "Замена комплектующих", false, 12));

        Assert.Contains("незавершённой передаче", exception.Message);
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private TestFixture(
            SqliteConnection connection,
            AppDbContext db,
            AlertHandoffService service,
            int alertId,
            int reasonId)
        {
            _connection = connection;
            Db = db;
            Service = service;
            AlertId = alertId;
            ReasonId = reasonId;
        }

        public AppDbContext Db { get; }
        public AlertHandoffService Service { get; }
        public int AlertId { get; }
        public int ReasonId { get; }

        public static async Task<TestFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new AppDbContext(options);
            await db.Database.EnsureCreatedAsync();

            var camera = new Camera
            {
                Id = 1,
                Name = "Камера 1",
                IpAddress = "192.168.4.31",
                ServiceUrl = "http://192.168.4.31/onvif/device_service"
            };
            var owner = CreateUser(10, "operator", "Оператор", "Operator");
            var technician = CreateUser(11, "technician", "Техник", "Technician");
            var admin = CreateUser(12, "admin", "Администратор", "Admin", true);
            var reason = new AlertHandoffReason
            {
                Key = "parts-replacement",
                Name = "Замена комплектующих",
                SortOrder = 10
            };
            var alert = new Alert
            {
                Camera = camera,
                CameraId = camera.Id,
                AlertType = AlertType.CameraOffline,
                Severity = AlertSeverity.Warning,
                Title = "Камера недоступна",
                AssignedToUserId = owner.Id,
                AssignedToUsername = owner.Username,
                AssignedToDisplayName = owner.DisplayName,
                WorkflowStage = "working"
            };
            db.AddRange(camera, owner, technician, admin, reason, alert);
            await db.SaveChangesAsync();

            var service = new AlertHandoffService(db, NullLogger<AlertHandoffService>.Instance);
            return new TestFixture(connection, db, service, alert.Id, reason.Id);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }

        private static AppUser CreateUser(
            int id,
            string username,
            string displayName,
            string role,
            bool canManageUsers = false) => new()
            {
                Id = id,
                Username = username,
                DisplayName = displayName,
                Role = role,
                CanViewAlerts = true,
                CanViewAlertHistory = true,
                CanRunChecks = true,
                CanManageCameras = true,
                CanManageUsers = canManageUsers,
                IsActive = true
            };
    }
}
