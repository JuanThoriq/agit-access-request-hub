namespace AccessRequestHub.Api.Contracts;

/// <summary>
/// Create payload. Enums arrive as strings so an unknown value produces a field-level
/// validation error instead of a raw JSON deserialization failure.
/// Requester is deliberately absent: identity always comes from the server side session.
/// </summary>
public sealed record CreateAccessRequestRequest(
    string? ClientRequestId,
    int? ApplicationId,
    string? Environment,
    string? AccessLevel,
    string? Justification);

/// <summary>Approve/reject payload. <paramref name="Version"/> is the version the client saw.</summary>
public sealed record TransitionRequest(int? Version, string? Reason);

public sealed record UserRef(int Id, string Email, string DisplayName);

public sealed record ApplicationRef(int Id, string Name, UserRef SystemOwner);

public sealed record AuditEventDto(
    long Id,
    string EventType,
    UserRef Actor,
    string? FromStatus,
    string ToStatus,
    string? Reason,
    DateTime AtUtc);

public sealed record AllowedActions(bool CanApprove, bool CanReject);

public sealed record AccessRequestDto(
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
    IReadOnlyList<AuditEventDto>? Audit);

public sealed record CurrentUserDto(
    UserRef User,
    bool IsAdmin,
    IReadOnlyList<ApplicationRef> OwnedApplications,
    IReadOnlyList<UserRef> DirectReports);

public sealed record TransitionResultDto(AccessRequestDto Request, string? IdempotentReplayOf = null);
