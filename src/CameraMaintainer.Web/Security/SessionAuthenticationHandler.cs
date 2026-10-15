using System.Security.Claims;
using System.Globalization;
using System.Text.Encodings.Web;
using CameraMaintainer.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CameraMaintainer.Web.Security;

public sealed class SessionAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "SessionBearer";

    private readonly AccountSessionStore _sessions;
    private readonly AppDbContext _db;

    public SessionAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        AccountSessionStore sessions,
        AppDbContext db)
        : base(options, logger, encoder)
    {
        _sessions = sessions;
        _db = db;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = SessionTokenReader.Read(Request);
        if (string.IsNullOrEmpty(token))
            return AuthenticateResult.NoResult();

        if (!_sessions.TryGetUserId(token, out var userId))
            return AuthenticateResult.Fail("Session is invalid or expired");

        var user = await _db.AppUsers.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == userId && x.IsActive, Context.RequestAborted);
        if (user == null)
        {
            _sessions.Remove(token);
            return AuthenticateResult.Fail("Session is invalid or expired");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString(CultureInfo.InvariantCulture)),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.GivenName, string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName),
            new(ClaimTypes.Role, user.Role)
        };

        AddPermission(claims, user.CanViewDashboard, AccessPolicies.ViewDashboard);
        AddPermission(claims, user.CanViewCameras, AccessPolicies.ViewCameras);
        AddPermission(claims, user.CanViewAlerts, AccessPolicies.ViewAlerts);
        AddPermission(claims, user.CanViewAlerts && user.CanViewAlertHistory, AccessPolicies.ViewAlertHistory);
        AddPermission(claims, user.CanViewCameras && user.CanViewMaintenance, AccessPolicies.ViewMaintenance);
        AddPermission(
            claims,
            user.CanRunChecks && (user.CanViewCameras || user.CanViewAlerts),
            AccessPolicies.RunChecks);
        AddPermission(
            claims,
            user.CanViewCameras && user.CanManageCameras,
            AccessPolicies.ManageCameras);
        // Account management is an administrator capability. Do not let an
        // inconsistent permission flag turn an Operator into an administrator.
        var canManageUsers = user.CanManageUsers &&
            string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase);
        AddPermission(claims, canManageUsers, AccessPolicies.ManageUsers);

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) ||
            Request.Path.StartsWithSegments("/hubs", StringComparison.OrdinalIgnoreCase))
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        var returnUrl = Request.PathBase + Request.Path + Request.QueryString;
        Response.Redirect("/Login?returnUrl=" + Uri.EscapeDataString(returnUrl));
        return Task.CompletedTask;
    }

    private static void AddPermission(List<Claim> claims, bool enabled, string permission)
    {
        if (enabled)
            claims.Add(new Claim(AccessPolicies.PermissionClaim, permission));
    }
}
