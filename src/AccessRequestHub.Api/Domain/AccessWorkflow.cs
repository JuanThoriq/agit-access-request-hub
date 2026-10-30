namespace AccessRequestHub.Api.Domain;

/// <summary>
/// Why a user may not act on a request. Each value maps to exactly one HTTP response,
/// so the API never guesses between "forbidden" and "conflict".
/// </summary>
public enum Denial
{
    None = 0,
    RequestIsTerminal,
    SelfApproval,
    NotAssignedManager,
    NotAssignedSystemOwner
}

/// <summary>
/// Pure business rules: no EF, no HTTP, no clock. Everything here is decided from
/// data already loaded, which keeps the rules testable in isolation and impossible to
/// bypass by calling a different endpoint.
/// </summary>
public static class AccessWorkflow
{
    public const string Phase1PolicyVersion = "v1";

    /// <summary>Production OR Admin is high-risk and needs a second approval by the system owner.</summary>
    public static bool IsHighRisk(EnvironmentType environment, AccessLevel accessLevel)
        => environment == EnvironmentType.Production || accessLevel == AccessLevel.Admin;

    public static bool IsTerminal(RequestStatus status)
        => status is RequestStatus.Approved or RequestStatus.Rejected;

    /// <summary>Status after an approval at the current step, or null when the step cannot be approved.</summary>
    public static RequestStatus? StatusAfterApproval(RequestStatus current, bool isHighRisk) => current switch
    {
        RequestStatus.PendingManagerApproval => isHighRisk
            ? RequestStatus.PendingSystemOwnerApproval
            : RequestStatus.Approved,
        RequestStatus.PendingSystemOwnerApproval => RequestStatus.Approved,
        _ => null
    };

    /// <summary>
    /// Single authorization gate for approve/reject. Order matters: terminal first (the request
    /// can no longer move), then self-approval, then the assigned approver for the current step.
    /// </summary>
    public static Denial CheckApprover(User actor, AccessRequest request)
    {
        if (IsTerminal(request.Status))
        {
            return Denial.RequestIsTerminal;
        }

        if (request.RequesterId == actor.Id)
        {
            return Denial.SelfApproval;
        }

        return request.Status switch
        {
            RequestStatus.PendingManagerApproval when request.Requester.ManagerId == actor.Id => Denial.None,
            RequestStatus.PendingManagerApproval => Denial.NotAssignedManager,

            RequestStatus.PendingSystemOwnerApproval when request.Application.SystemOwnerId == actor.Id => Denial.None,
            RequestStatus.PendingSystemOwnerApproval => Denial.NotAssignedSystemOwner,

            _ => Denial.RequestIsTerminal
        };
    }

    /// <summary>Who may read a request: the requester, their manager, the owning system owner, or an admin/auditor.</summary>
    public static bool CanView(User actor, AccessRequest request)
        => actor.IsAdmin
           || request.RequesterId == actor.Id
           || request.Requester.ManagerId == actor.Id
           || request.Application.SystemOwnerId == actor.Id;
}
