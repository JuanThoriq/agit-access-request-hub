using AccessRequestHub.Api.Domain;

namespace AccessRequestHub.Api.Api;

/// <summary>
/// One place that decides what an error looks like. Every client-visible failure is an RFC 7807
/// ProblemDetails carrying a stable <c>code</c>, so the UI can branch on the code instead of
/// parsing English ("version_conflict" vs "not_assigned_approver" are different UX).
/// </summary>
public static class ApiProblems
{
    public static IResult Validation(HttpContext ctx, Dictionary<string, List<string>> errors)
        => TypedResults.Problem(
            title: "Validation failed",
            statusCode: StatusCodes.Status400BadRequest,
            detail: "One or more fields are invalid.",
            extensions: Extensions(ctx, "validation_failed", errors.ToDictionary(e => e.Key, e => (object?)e.Value.ToArray())));

    public static IResult Unauthorized(HttpContext ctx, string code, string detail)
        => TypedResults.Problem(title: "Unauthorized", statusCode: StatusCodes.Status401Unauthorized, detail: detail,
            extensions: Extensions(ctx, code));

    public static IResult Forbidden(HttpContext ctx, string code, string detail)
        => TypedResults.Problem(title: "Forbidden", statusCode: StatusCodes.Status403Forbidden, detail: detail,
            extensions: Extensions(ctx, code));

    public static IResult NotFound(HttpContext ctx, string code, string detail)
        => TypedResults.Problem(title: "Not found", statusCode: StatusCodes.Status404NotFound, detail: detail,
            extensions: Extensions(ctx, code));

    public static IResult Conflict(HttpContext ctx, string code, string detail)
        => TypedResults.Problem(title: "Conflict", statusCode: StatusCodes.Status409Conflict, detail: detail,
            extensions: Extensions(ctx, code));

    public static IResult ForDenial(HttpContext ctx, Denial denial, AccessRequest request, User actor) => denial switch
    {
        Denial.RequestIsTerminal => Conflict(ctx, "invalid_transition",
            $"Request is already {request.Status}; Approved and Rejected are terminal."),

        Denial.SelfApproval => Forbidden(ctx, "self_approval_forbidden",
            "You cannot approve or reject your own request, even when you are the assigned approver."),

        Denial.NotAssignedManager => Forbidden(ctx, "not_assigned_approver",
            "You are not the manager of this request's requester. This step belongs to their manager."),

        Denial.NotAssignedSystemOwner => Forbidden(ctx, "not_assigned_approver",
            $"You are not the System Owner of {request.Application.Name}. This step belongs to its System Owner."),

        _ => Forbidden(ctx, "forbidden", "Not allowed.")
    };

    private static Dictionary<string, object?> Extensions(HttpContext ctx, string code, Dictionary<string, object?>? more = null)
    {
        var extensions = new Dictionary<string, object?>
        {
            ["code"] = code,
            ["traceId"] = ctx.TraceIdentifier
        };

        if (more is not null)
        {
            foreach (var (key, value) in more)
            {
                extensions[key] = value;
            }
        }

        return extensions;
    }
}
