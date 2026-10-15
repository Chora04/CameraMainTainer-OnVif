using CameraMaintainer.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace CameraMaintainer.Web.Security;

public static class AlertVisibilityPolicy
{
    public static IQueryable<Alert> ApplyToQuery(
        IQueryable<Alert> query, AppUser actor, IEnumerable<AlertBoardColumn> columns)
    {
        if (!actor.IsActive || !actor.CanViewAlerts)
            return query.Where(alert => false);
        if (!actor.CanViewAlertHistory)
            query = query.Where(alert => !alert.AutoResolved);
        if (IsAdministrator(actor.Role, actor.CanManageUsers))
            return query;

        var username = actor.Username.ToUpperInvariant();
        var visibleStages = columns.Where(column => AlertBoardRoleVisibility.IsVisibleTo(
            column, actor.Role, false, actor.AlertBoardColumnKeys)).Select(column => column.Key).ToArray();
        // Apply ownership before pagination; closed intake cards are not shared history.
        return query.Where(alert =>
            alert.AssignedToUserId == actor.Id ||
            (!alert.AssignedToUserId.HasValue && username != "" && alert.AssignedToUsername != null &&
                EF.Functions.Collate(alert.AssignedToUsername, "NOCASE") == actor.Username) ||
            alert.Handoffs.Any(item => item.FromUserId == actor.Id || item.ToUserId == actor.Id) ||
            (!alert.AutoResolved && (alert.WorkflowStage == "new" ||
                ((alert.WorkflowStage == null || alert.WorkflowStage == "") && !alert.AssignedToUserId.HasValue))))
            .Where(alert => alert.AutoResolved || visibleStages.Contains(
                alert.WorkflowStage == null || alert.WorkflowStage == ""
                    ? (alert.AssignedToUserId.HasValue ? "assigned" : "new") : alert.WorkflowStage));
    }

    public static bool IsOperatorScoped(bool canRunChecks, bool canManageUsers, string? role = null) =>
        role == null
            ? canRunChecks && !canManageUsers
            : !IsAdministrator(role, canManageUsers);

    public static bool IsAdministrator(string? role, bool canManageUsers) =>
        canManageUsers && string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase);

    public static List<Alert> FilterForOperator(IEnumerable<Alert> alerts, int userId, string? username = null)
    {
        return alerts
            .Where(alert => IsVisibleToOperator(alert, userId, username))
            .ToList();
    }

    public static List<Alert> FilterForBoardAccess(
        IEnumerable<Alert> alerts,
        IEnumerable<AlertBoardColumn> columns,
        string? role,
        bool isAdministrator,
        string? accountColumnKeys)
    {
        if (isAdministrator)
            return alerts.ToList();

        var visibleStages = columns
            .Where(column => AlertBoardRoleVisibility.IsVisibleTo(
                column,
                role,
                false,
                accountColumnKeys))
            .Select(column => column.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return alerts
            .Where(alert => alert.AutoResolved || visibleStages.Contains(EffectiveStage(alert)))
            .ToList();
    }

    public static bool IsVisibleToOperator(Alert alert, int userId, string? username = null)
    {
        var assignedToCurrentUser = alert.AssignedToUserId == userId ||
            (!alert.AssignedToUserId.HasValue && !string.IsNullOrWhiteSpace(username) &&
             string.Equals(alert.AssignedToUsername, username, StringComparison.OrdinalIgnoreCase));
        if (assignedToCurrentUser)
            return true;

        if (IsHandoffParticipant(alert, userId))
        {
            return true;
        }

        // The New column is the shared intake queue. Operators must see all
        // new work so that unassigned problems cannot be missed.
        return IsNew(alert);
    }

    public static bool IsHandoffParticipant(Alert alert, int userId) =>
        alert.Handoffs.Any(handoff => handoff.FromUserId == userId || handoff.ToUserId == userId);

    private static bool IsNew(Alert alert) => !alert.AutoResolved &&
        (string.Equals(alert.WorkflowStage, "new", StringComparison.OrdinalIgnoreCase) ||
        (string.IsNullOrWhiteSpace(alert.WorkflowStage) && !alert.AssignedToUserId.HasValue));

    public static string EffectiveStage(Alert alert) =>
        string.IsNullOrWhiteSpace(alert.WorkflowStage)
            ? (alert.AssignedToUserId.HasValue ? "assigned" : "new")
            : alert.WorkflowStage;
}
