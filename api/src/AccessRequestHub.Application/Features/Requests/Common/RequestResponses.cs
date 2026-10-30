using AccessRequestHub.Application.Common.Mapping;
using AccessRequestHub.Application.Common.Models;
using AccessRequestHub.Domain.Entities;
using AccessRequestHub.Domain.Workflow;

namespace AccessRequestHub.Application.Features.Requests.Common;

public sealed record AllowedActions(bool CanApprove, bool CanReject);

public sealed record AuditEventResponse(
    long Id,
    string EventType,
    UserRef Actor,
    string? FromStatus,
    string ToStatus,
    string? Reason,
    DateTime AtUtc);

public sealed record AccessRequestResponse(
    int Id,
    string ClientRequestId,
    UserRef Requester,
    ApplicationRef Application,
    string Environment,
    string AccessLevel,
    string Justification,
    string Status,
    bool IsHighRisk,
    string PolicyVersion,
    int Version,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    AllowedActions AllowedActions,
    IReadOnlyList<AuditEventResponse>? Audit);

public static class RequestResponseMapper
{
    private static AuditEventResponse ToResponse(AuditEvent e) => new(
        e.Id,
        e.EventType,
        Mappers.ToRef(e.ActorUser),
        e.FromStatus?.ToString(),
        e.ToStatus.ToString(),
        e.Reason,
        Mappers.AsUtc(e.CreatedAtUtc));

    /// <summary>
    /// <c>allowedActions</c> is a UI hint only: hiding a button is not authorization, and every
    /// transition re-checks the same rule server side.
    /// </summary>
    public static AccessRequestResponse ToResponse(AccessRequest request, User actor, IReadOnlyList<AuditEvent>? audit = null)
    {
        var canAct = AccessWorkflow.CheckApprover(actor, request) == Denial.None;

        return new AccessRequestResponse(
            request.Id,
            request.ClientRequestId,
            Mappers.ToRef(request.Requester),
            Mappers.ToRef(request.Application),
            request.Environment.ToString(),
            request.AccessLevel.ToString(),
            request.Justification,
            request.Status.ToString(),
            AccessWorkflow.IsHighRisk(request.Environment, request.AccessLevel),
            request.PolicyVersion,
            request.Version,
            Mappers.AsUtc(request.CreatedAtUtc),
            Mappers.AsUtc(request.UpdatedAtUtc),
            new AllowedActions(canAct, canAct),
            audit?.Select(ToResponse).ToList());
    }
}
