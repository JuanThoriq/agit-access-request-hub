namespace AccessRequestHub.Domain.Entities;

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
