using AccessRequestHub.Application.Common.Models;

namespace AccessRequestHub.Api.Common;

/// <summary>
/// One place that decides what an error looks like. Every client-visible failure is an RFC 7807
/// ProblemDetails carrying a stable <c>code</c>, so the UI can branch on the code instead of
/// parsing English ("version_conflict" vs "not_assigned_approver" are different UX).
/// This is also the only place that knows about HTTP status codes: the Application layer states an
/// outcome, we translate it.
/// </summary>
public static class ApiProblems
{
    public static IResult ToResult<T>(HttpContext ctx, Result<T> result) => result.Kind switch
    {
        ResultKind.Success => Results.Ok(result.Value),
        ResultKind.Validation => Validation(ctx, result.Errors ?? new Dictionary<string, string[]>()),
        ResultKind.Unauthorized => Unauthorized(ctx, result.Code, result.Detail),
        ResultKind.Forbidden => Forbidden(ctx, result.Code, result.Detail),
        ResultKind.NotFound => NotFound(ctx, result.Code, result.Detail),
        ResultKind.Conflict => Conflict(ctx, result.Code, result.Detail),
        _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
    };

    public static IResult Validation(HttpContext ctx, IReadOnlyDictionary<string, string[]> errors)
    {
        // Field errors live under "errors" (the ProblemDetails convention) so the UI can highlight
        // inputs, while "code" stays a machine readable reason for the whole response.
        var fields = errors.ToDictionary(e => e.Key, e => (object?)e.Value);

        return TypedResults.Problem(
            title: "Validation failed",
            statusCode: StatusCodes.Status400BadRequest,
            detail: "One or more fields are invalid.",
            extensions: Extensions(ctx, "validation_failed", new Dictionary<string, object?> { ["errors"] = fields }));
    }

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

    private static Dictionary<string, object?> Extensions(
        HttpContext ctx,
        string code,
        Dictionary<string, object?>? more = null)
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
