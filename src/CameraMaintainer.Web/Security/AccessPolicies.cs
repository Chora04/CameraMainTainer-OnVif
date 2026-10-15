namespace CameraMaintainer.Web.Security;

public static class AccessPolicies
{
    public const string PermissionClaim = "camera_maintainer_permission";

    public const string ViewDashboard = "dashboard.view";
    public const string ViewCameras = "cameras.view";
    public const string ViewAlerts = "alerts.view";
    public const string ViewAlertHistory = "alerts.history.view";
    public const string ViewMaintenance = "maintenance.view";
    public const string RunChecks = "cameras.check";
    public const string ManageCameras = "cameras.manage";
    public const string ManageUsers = "users.manage";

    // Composite policies. These names are used only by authorization rules;
    // they are not persisted as independent account permissions.
    public const string CheckCameras = "cameras.check.with-view";
    public const string WorkAlerts = "alerts.work";
    public const string ManageMaintenance = "maintenance.manage";
}
