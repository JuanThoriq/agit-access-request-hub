namespace AccessRequestHub.Api.Domain;

public enum EnvironmentType
{
    NonProduction,
    Production
}

public enum AccessLevel
{
    Read,
    Admin
}

/// <summary>
/// The only states a request can be in. Approved and Rejected are terminal.
/// </summary>
public enum RequestStatus
{
    PendingManagerApproval,
    PendingSystemOwnerApproval,
    Approved,
    Rejected
}

public sealed class User
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int? ManagerId { get; set; }
    public User? Manager { get; set; }
    public bool IsAdmin { get; set; }
}

public sealed class Application
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SystemOwnerId { get; set; }
    public User SystemOwner { get; set; } = null!;
}

public sealed class AccessRequest
{
    public int Id { get; set; }

    /// <summary>Client supplied idempotency key. Unique per requester (enforced by the database).</summary>
    public string ClientRequestId { get; set; } = string.Empty;

    public int RequesterId { get; set; }
    public User Requester { get; set; } = null!;

    public int ApplicationId { get; set; }
    public Application Application { get; set; } = null!;

    public EnvironmentType Environment { get; set; }
    public AccessLevel AccessLevel { get; set; }
    public string Justification { get; set; } = string.Empty;
    public RequestStatus Status { get; set; }

    /// <summary>Policy version captured at creation time, so old requests stay explainable when policy changes.</summary>
    public string PolicyVersion { get; set; } = string.Empty;

    /// <summary>Optimistic concurrency token. Incremented on every accepted transition.</summary>
    public int Version { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public List<AuditEvent> AuditEvents { get; set; } = new();
}

public sealed class AuditEvent
{
    public long Id { get; set; }
    public int AccessRequestId { get; set; }
    public AccessRequest AccessRequest { get; set; } = null!;
    public string EventType { get; set; } = string.Empty;
    public int ActorUserId { get; set; }
    public User ActorUser { get; set; } = null!;
    public RequestStatus? FromStatus { get; set; }
    public RequestStatus ToStatus { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
