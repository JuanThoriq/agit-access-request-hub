using AccessRequestHub.Api.Contracts;
using AccessRequestHub.Api.Domain;

namespace AccessRequestHub.Api.Api;

public static class RequestMapper
{
    public static UserRef ToRef(User user) => new(user.Id, user.Email, user.DisplayName);

    public static ApplicationRef ToRef(Application application) => new(application.Id, application.Name, ToRef(application.SystemOwner));

    public static AuditEventDto ToDto(AuditEvent e) => new(
        e.Id,
        e.EventType,
        ToRef(e.ActorUser),
        e.FromStatus?.ToString(),
        e.ToStatus.ToString(),
        e.Reason,
        e.CreatedAtUtc);

    /// <summary>
    /// <paramref name="allowedActions"/> is a UI hint only: hiding a button is not authorization,
    /// and every transition re-checks the same rule server side.
    /// </summary>
    public static AccessRequestDto ToDto(AccessRequest request, User actor, IReadOnlyList<AuditEvent>? audit = null)
    {
        var canAct = AccessWorkflow.CheckApprover(actor, request) == Denial.None;

        return new AccessRequestDto(
            request.Id,
            request.ClientRequestId,
            ToRef(request.Requester),
            ToRef(request.Application),
            request.Environment.ToString(),
            request.AccessLevel.ToString(),
            request.Justification,
            request.Status.ToString(),
            AccessWorkflow.IsHighRisk(request.Environment, request.AccessLevel),
            request.PolicyVersion,
            request.Version,
            request.CreatedAtUtc,
            request.UpdatedAtUtc,
            new AllowedActions(canAct, canAct),
            audit?.Select(ToDto).ToList());
    }
}
