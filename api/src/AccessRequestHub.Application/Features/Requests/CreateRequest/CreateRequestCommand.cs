namespace AccessRequestHub.Application.Features.Requests.CreateRequest;

/// <summary>
/// Create payload. Enums arrive as strings so an unknown value produces a field-level validation
/// error instead of a raw JSON deserialization failure.
/// The requester is deliberately absent: identity always comes from the server side session.
/// </summary>
public sealed record CreateRequestCommand(
    string? ClientRequestId,
    int? ApplicationId,
    string? Environment,
    string? AccessLevel,
    string? Justification);
