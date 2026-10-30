using AccessRequestHub.Application.Common.Models;
using AccessRequestHub.Domain.Entities;
using ApplicationEntity = AccessRequestHub.Domain.Entities.Application;
using AccessRequestHub.Domain.Workflow;

namespace AccessRequestHub.Application.Common.Mapping;

public static class Mappers
{
    public static UserRef ToRef(User user) => new(user.Id, user.Email, user.DisplayName);

    public static ApplicationRef ToRef(ApplicationEntity application)
        => new(application.Id, application.Name, ToRef(application.SystemOwner));

    /// <summary>
    /// SQLite has no DateTime type: values come back as Unspecified, so System.Text.Json would drop
    /// the "Z" and every browser would re-interpret UTC as local time. Re-stamp the kind on the way out.
    /// </summary>
    public static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    /// <summary>
    /// Maps a denial (a domain verdict) to the outcome the API must return. Keeping the wording here
    /// means every endpoint explains the same rule the same way.
    /// </summary>
    public static Result<T> ToResult<T>(Denial denial, AccessRequest request) => denial switch
    {
        Denial.SelfApproval => Result<T>.Forbidden("self_approval_forbidden",
            "You cannot approve or reject your own request, even when you are the assigned approver."),

        Denial.NotAssignedManager => Result<T>.Forbidden("not_assigned_approver",
            "You are not the manager of this request's requester. This step belongs to their manager."),

        Denial.NotAssignedSystemOwner => Result<T>.Forbidden("not_assigned_approver",
            $"You are not the System Owner of {request.Application.Name}. This step belongs to its System Owner."),

        Denial.RequestIsTerminal => Result<T>.Conflict("invalid_transition",
            $"Request is already {request.Status}; Approved and Rejected are terminal."),

        _ => Result<T>.Forbidden("forbidden", "Not allowed.")
    };
}
