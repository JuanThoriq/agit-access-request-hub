using AccessRequestHub.Application.Common.Mapping;
using AccessRequestHub.Application.Common.Models;
using AccessRequestHub.Application.Features.Requests.Common;
using AccessRequestHub.Domain;
using AccessRequestHub.Domain.Entities;
using AccessRequestHub.Domain.Workflow;
using AccessRequestHub.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AccessRequestHub.Application.Features.Requests.Transitions;

/// <summary>
/// Approve and reject are one rule with two outcomes: same authorization, same version guard,
/// same audit write. They live together so the two paths cannot drift apart.
/// </summary>
public sealed class TransitionService(AppDbContext db, ILogger<TransitionService> log)
{
    private const int ReasonMaxLength = 2000;

    public Task<Result<AccessRequestResponse>> ApproveAsync(
        User actor, int requestId, TransitionCommand command, CancellationToken ct)
        => HandleAsync(actor, requestId, command, isApproval: true, ct);

    public Task<Result<AccessRequestResponse>> RejectAsync(
        User actor, int requestId, TransitionCommand command, CancellationToken ct)
        => HandleAsync(actor, requestId, command, isApproval: false, ct);

    private async Task<Result<AccessRequestResponse>> HandleAsync(
        User actor,
        int requestId,
        TransitionCommand command,
        bool isApproval,
        CancellationToken ct)
    {
        if (command.Version is null)
        {
            return Result<AccessRequestResponse>.Validation(new Dictionary<string, string[]>
            {
                ["version"] = ["version is required: send the version you last read."]
            });
        }

        var request = await RequestQueries.WithNavigations(db).FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null)
        {
            return Result<AccessRequestResponse>.NotFound("request_not_found", $"Request {requestId} does not exist.");
        }

        // Check order is deliberate:
        // 1. invisible requests stay invisible (403), so a stranger learns nothing about state or version
        // 2. terminal state (409) and stale version (409) come before the approver check, because a
        //    participant acting on a version that already moved deserves "conflict", not "forbidden"
        // 3. only then, self approval and the assigned approver for the current step (403)
        if (!AccessWorkflow.CanView(actor, request))
        {
            return Result<AccessRequestResponse>.Forbidden("not_a_participant",
                "You are not the requester, their manager, the System Owner, or an Admin/Auditor.");
        }

        var denial = AccessWorkflow.CheckApprover(actor, request);
        if (denial == Denial.RequestIsTerminal)
        {
            return Mappers.ToResult<AccessRequestResponse>(denial, request);
        }

        if (command.Version.Value != request.Version)
        {
            return VersionConflict(request);
        }

        if (denial != Denial.None)
        {
            return Mappers.ToResult<AccessRequestResponse>(denial, request);
        }

        if (!isApproval && string.IsNullOrWhiteSpace(command.Reason))
        {
            return Result<AccessRequestResponse>.Validation(new Dictionary<string, string[]>
            {
                ["reason"] = ["reason is required when rejecting."]
            });
        }

        var reason = isApproval ? null : command.Reason!.Trim();
        if (reason is not null && reason.Length > ReasonMaxLength)
        {
            return Result<AccessRequestResponse>.Validation(new Dictionary<string, string[]>
            {
                ["reason"] = [$"reason must be at most {ReasonMaxLength} characters."]
            });
        }

        var from = request.Status;
        var to = isApproval
            ? AccessWorkflow.StatusAfterApproval(from, AccessWorkflow.IsHighRisk(request.Environment, request.AccessLevel))!.Value
            : RequestStatus.Rejected;

        var now = DateTime.UtcNow;
        request.Status = to;
        request.Version += 1;
        request.UpdatedAtUtc = now;

        db.AuditEvents.Add(new AuditEvent
        {
            AccessRequestId = request.Id,
            ActorUserId = actor.Id,
            EventType = isApproval ? "Approved" : "Rejected",
            FromStatus = from,
            ToStatus = to,
            Reason = reason,
            CreatedAtUtc = now
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Two requests passed the version check at the same time; the UPDATE ... WHERE Version = @v
            // let exactly one through. This is the real guard, the check above is only a fast path.
            return VersionConflict(request);
        }

        log.LogInformation(
            "Transition requestId={RequestId} from={From} to={To} actorId={ActorId} version={Version}",
            request.Id, from, to, actor.Id, request.Version);

        var audit = await RequestQueries.LoadAuditAsync(db, request.Id, ct);
        return Result<AccessRequestResponse>.Success(RequestResponseMapper.ToResponse(request, actor, audit));
    }

    private static Result<AccessRequestResponse> VersionConflict(AccessRequest request)
        => Result<AccessRequestResponse>.Conflict("version_conflict",
            $"Request was changed by someone else (current version {request.Version}, status {request.Status}). Reload and retry.");
}
