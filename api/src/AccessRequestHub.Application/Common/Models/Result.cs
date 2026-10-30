namespace AccessRequestHub.Application.Common.Models;

public enum ResultKind
{
    Success,
    Validation,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict
}

/// <summary>
/// What a use case returns. The Application layer never touches HTTP: it states the outcome and a
/// stable <see cref="Code"/>, and the API layer is the only place that turns that into a status code.
/// </summary>
public sealed class Result<T>
{
    private Result() { }

    public ResultKind Kind { get; private init; } = ResultKind.Success;
    public T? Value { get; private init; }
    public string Code { get; private init; } = string.Empty;
    public string Detail { get; private init; } = string.Empty;

    /// <summary>Per-field messages for a 400, keyed by the request property name the UI highlights.</summary>
    public IReadOnlyDictionary<string, string[]>? Errors { get; private init; }

    public bool IsSuccess => Kind == ResultKind.Success;

    public static Result<T> Success(T value) => new() { Value = value };

    public static Result<T> Validation(IReadOnlyDictionary<string, string[]> errors) => new()
    {
        Kind = ResultKind.Validation,
        Code = "validation_failed",
        Detail = "One or more fields are invalid.",
        Errors = errors
    };

    public static Result<T> Unauthorized(string code, string detail)
        => new() { Kind = ResultKind.Unauthorized, Code = code, Detail = detail };

    public static Result<T> Forbidden(string code, string detail)
        => new() { Kind = ResultKind.Forbidden, Code = code, Detail = detail };

    public static Result<T> NotFound(string code, string detail)
        => new() { Kind = ResultKind.NotFound, Code = code, Detail = detail };

    public static Result<T> Conflict(string code, string detail)
        => new() { Kind = ResultKind.Conflict, Code = code, Detail = detail };
}
