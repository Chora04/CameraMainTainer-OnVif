using CameraMaintainer.Web.Hubs;
using CameraMaintainer.Web.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using System.Text.Json;

namespace CameraMaintainer.Tests;

public sealed class WorkspaceChangeTests
{
    [Theory]
    [InlineData("Alerts", "alerts")]
    [InlineData("AlertBoard", "alerts")]
    [InlineData("Accounts", "access")]
    [InlineData("Cameras", "registry")]
    [InlineData("Notifications", "notifications")]
    [InlineData("CameraMonitoringSettings", "settings")]
    [InlineData("Health", "queue")]
    public async Task SuccessfulWrites_OnlyPublishInvalidationTopics(string controller, string topic)
    {
        var hub = new TestHub();
        await Execute(hub, controller, "PUT", new NoContentResult());
        var message = Assert.Single(hub.Messages);
        Assert.Equal("workspace.changed", message.Method);
        var payload = JsonSerializer.SerializeToElement(Assert.Single(message.Arguments));
        Assert.Single(payload.EnumerateObject());
        Assert.Contains(topic, payload.GetProperty("topics").EnumerateArray().Select(item => item.GetString()));
    }

    [Fact]
    public async Task ReadsErrorsAndDeniedMutations_DoNotPublish()
    {
        var hub = new TestHub();
        await Execute(hub, "Alerts", "GET", new OkResult());
        await Execute(hub, "Alerts", "POST", new BadRequestResult());
        await Execute(hub, "Alerts", "POST", new UnauthorizedResult());
        await Execute(hub, "Alerts", "POST", new ForbidResult());
        await Execute(hub, "Alerts", "POST", new ChallengeResult());
        await Execute(hub, "Alerts", "POST", new NotFoundResult());
        await Execute(hub, "Alerts", "POST", new NoContentResult(), "RecordViewed");
        Assert.Empty(hub.Messages);
    }

    [Fact]
    public async Task BrokenClientTransport_DoesNotFailASavedMutation()
    {
        await Execute(new TestHub { Fail = true }, "Alerts", "POST", new NoContentResult());
    }

    [Fact]
    public async Task HealthUpdates_AreCoalescedIntoOneCameraBatch()
    {
        var hub = new TestHub();
        var broadcaster = new EventBroadcaster(hub, NullLogger<EventBroadcaster>.Instance);

        await broadcaster.HealthUpdatedAsync(12);
        await broadcaster.HealthUpdatedAsync(7);
        await broadcaster.HealthUpdatedAsync(12);

        await hub.MessagePublished.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var message = Assert.Single(hub.Messages);
        Assert.Equal("health.updated", message.Method);
        var payload = JsonSerializer.SerializeToElement(Assert.Single(message.Arguments));
        Assert.Equal([7, 12], payload.GetProperty("cameraIds").EnumerateArray().Select(item => item.GetInt32()));
    }

    [Fact]
    public void AccessVersion_IsOrderIndependentAndChangesWhenPermissionIsRevoked()
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, "12"), new Claim(AccessPolicies.PermissionClaim, AccessPolicies.ViewAlerts) };
        var current = AccessVersion.For(new ClaimsPrincipal(new ClaimsIdentity(claims)));
        Assert.Equal(current, AccessVersion.For(new ClaimsPrincipal(new ClaimsIdentity(claims.Reverse()))));
        Assert.NotEqual(current, AccessVersion.For(new ClaimsPrincipal(new ClaimsIdentity(claims.Take(1)))));
    }

    private static Task Execute(TestHub hub, string controller, string method, IActionResult result, string action = "Update")
    {
        var http = new DefaultHttpContext();
        http.Request.Method = method;
        var context = new ActionContext(http, new RouteData(), new ControllerActionDescriptor { ControllerName = controller, ActionName = action });
        var filters = new List<IFilterMetadata>();
        var executing = new ActionExecutingContext(context, filters, new Dictionary<string, object?>(), new object());
        return new WorkspaceChangeFilter(new EventBroadcaster(hub, NullLogger<EventBroadcaster>.Instance))
            .OnActionExecutionAsync(executing, () => Task.FromResult(new ActionExecutedContext(context, filters, new object()) { Result = result }));
    }

    private sealed class TestHub : IHubContext<EventsHub>, IHubClients, IClientProxy
    {
        public bool Fail { get; init; }
        public List<(string Method, object?[] Arguments)> Messages { get; } = [];
        public TaskCompletionSource MessagePublished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IHubClients Clients => this;
        public IGroupManager Groups => throw new NotSupportedException();
        public IClientProxy All => this;
        public IClientProxy AllExcept(IReadOnlyList<string> ids) => this;
        public IClientProxy Client(string id) => this;
        IClientProxy IHubClients<IClientProxy>.Clients(IReadOnlyList<string> ids) => this;
        public IClientProxy Group(string name) => this;
        public IClientProxy GroupExcept(string name, IReadOnlyList<string> ids) => this;
        IClientProxy IHubClients<IClientProxy>.Groups(IReadOnlyList<string> names) => this;
        public IClientProxy User(string id) => this;
        public IClientProxy Users(IReadOnlyList<string> ids) => this;
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            if (Fail) throw new IOException("Transport closed");
            Messages.Add((method, args));
            MessagePublished.TrySetResult();
            return Task.CompletedTask;
        }
    }
}
