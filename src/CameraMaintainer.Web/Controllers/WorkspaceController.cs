using CameraMaintainer.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CameraMaintainer.Web.Controllers;

[ApiController]
[Authorize]
[Route("api/workspace")]
public sealed class WorkspaceController : ControllerBase
{
    [HttpGet("access")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public ActionResult Access() => Ok(new { version = AccessVersion.For(User) });
}
