using CameraMaintainer.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CameraMaintainer.Web.Pages;

[Authorize]
public sealed class DashboardModel : PageModel
{
    public IActionResult OnGet()
    {
        var destination = User.HasClaim(AccessPolicies.PermissionClaim, AccessPolicies.ViewCameras) ? "/Cameras"
            : User.HasClaim(AccessPolicies.PermissionClaim, AccessPolicies.ViewAlerts) ? "/Alerts?view=active"
            : User.HasClaim(AccessPolicies.PermissionClaim, AccessPolicies.ManageUsers) ? "/Users"
            : "/Settings";
        return LocalRedirect(destination);
    }
}
