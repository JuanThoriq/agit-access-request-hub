using Microsoft.AspNetCore.Mvc;
using AccessRequestHub.Api.Common;
using AccessRequestHub.Api.Middleware;
using AccessRequestHub.Application.Features.Applications.GetApplications;
using AccessRequestHub.Application.Features.Identity.GetCurrentUser;

namespace AccessRequestHub.Api.Controllers;

/// <summary>Everything the UI needs to know about itself before showing a single request.</summary>
[ApiController]
[Route("api")]
public sealed class LookupsController(
    GetCurrentUserService currentUser,
    GetApplicationsService applications) : ControllerBase
{
    [HttpGet("me")]
    public async Task<IResult> GetMe(CancellationToken ct)
        => ApiProblems.ToResult(HttpContext, await currentUser.HandleAsync(HttpContext.CurrentUser(), ct));

    [HttpGet("applications")]
    public async Task<IResult> GetApplications(CancellationToken ct)
        => ApiProblems.ToResult(HttpContext, await applications.HandleAsync(ct));
}
