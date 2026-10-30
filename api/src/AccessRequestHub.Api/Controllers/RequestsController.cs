using Microsoft.AspNetCore.Mvc;
using AccessRequestHub.Api.Common;
using AccessRequestHub.Api.Middleware;
using AccessRequestHub.Application.Features.Requests.CreateRequest;
using AccessRequestHub.Application.Features.Requests.GetRequest;
using AccessRequestHub.Application.Features.Requests.ListRequests;
using AccessRequestHub.Application.Features.Requests.Transitions;

namespace AccessRequestHub.Api.Controllers;

/// <summary>
/// Thin by design: resolve who is asking, call one use case, translate the outcome.
/// No rule about access decisions lives here (see Domain/Workflow/AccessWorkflow).
/// </summary>
[ApiController]
[Route("api")]
public sealed class RequestsController(
    CreateRequestService createRequest,
    TransitionService transitions,
    GetRequestService getRequest,
    ListRequestsService listRequests) : ControllerBase
{
    [HttpGet("requests")]
    public async Task<IResult> GetMine(CancellationToken ct)
        => ApiProblems.ToResult(HttpContext, await listRequests.MineAsync(HttpContext.CurrentUser(), ct));

    [HttpGet("requests/all")]
    public async Task<IResult> GetAll(CancellationToken ct)
        => ApiProblems.ToResult(HttpContext, await listRequests.AllAsync(HttpContext.CurrentUser(), ct));

    [HttpGet("requests/{id:int}")]
    public async Task<IResult> GetById(int id, CancellationToken ct)
        => ApiProblems.ToResult(HttpContext, await getRequest.HandleAsync(HttpContext.CurrentUser(), id, ct));

    [HttpGet("inbox")]
    public async Task<IResult> GetInbox(CancellationToken ct)
        => ApiProblems.ToResult(HttpContext, await listRequests.InboxAsync(HttpContext.CurrentUser(), ct));

    [HttpPost("requests")]
    public async Task<IResult> Create([FromBody] CreateRequestCommand body, CancellationToken ct)
    {
        var result = await createRequest.HandleAsync(HttpContext.CurrentUser(), body, ct);

        if (!result.IsSuccess)
        {
            return ApiProblems.ToResult(HttpContext, result);
        }

        var outcome = result.Value!;
        // 201 only for a genuinely new row: an idempotent replay answers 200 with the same body,
        // so the client can tell "I created it" from "it already existed".
        return outcome.Created
            ? Results.Created($"/api/requests/{outcome.Request.Id}", outcome.Request)
            : Results.Ok(outcome.Request);
    }

    [HttpPost("requests/{id:int}/approve")]
    public async Task<IResult> Approve(int id, [FromBody] TransitionCommand body, CancellationToken ct)
        => ApiProblems.ToResult(HttpContext, await transitions.ApproveAsync(HttpContext.CurrentUser(), id, body, ct));

    [HttpPost("requests/{id:int}/reject")]
    public async Task<IResult> Reject(int id, [FromBody] TransitionCommand body, CancellationToken ct)
        => ApiProblems.ToResult(HttpContext, await transitions.RejectAsync(HttpContext.CurrentUser(), id, body, ct));
}
