using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace CameraMaintainer.Web.Hubs;

// Publish invalidations after successful writes, never account or card contents.
public sealed class WorkspaceChangeFilter(EventBroadcaster events) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var result = await next();
        if (result.Exception != null || result.Canceled || result.Result is ForbidResult or ChallengeResult ||
            context.HttpContext.Request.Method is not ("POST" or "PUT" or "PATCH" or "DELETE")) return;

        var status = (result.Result as IStatusCodeActionResult)?.StatusCode ?? context.HttpContext.Response.StatusCode;
        if (status is < 200 or >= 300 || context.ActionDescriptor is not ControllerActionDescriptor action) return;

        string[] topics = action.ControllerName switch
        {
            // View audit is published only when a new record is actually saved.
            "Alerts" when action.ActionName == "RecordViewed" => [],
            "Alerts" when action.ActionName is "CreateHandoffReason" or "UpdateHandoffReason" => ["alerts", "settings"],
            "Alerts" => ["alerts", "notifications", "navigation"],
            "AlertBoard" => ["alerts", "notifications", "navigation", "users"],
            "Cameras" when action.ActionName == "ResolveLocation" => [],
            "Cameras" => ["cameras", "registry", "alerts", "navigation", "queue"],
            "Accounts" => ["access", "users", "alerts", "notifications", "navigation"],
            "Notifications" => ["notifications", "settings"],
            "CameraMonitoringSettings" => ["settings", "queue"],
            "AlertRetention" => ["settings", "alerts", "notifications", "navigation"],
            "Health" when context.HttpContext.Request.Method == "PUT" => ["queue", "cameras"],
            "Maintenance" => ["maintenance", "cameras", "queue"],
            "Config" or "Ptz" => ["cameras"],
            _ => []
        };
        if (topics.Length > 0) await events.WorkspaceChangedAsync(topics);
    }
}
