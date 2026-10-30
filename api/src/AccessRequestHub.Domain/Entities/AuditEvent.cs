namespace AccessRequestHub.Domain.Entities;

/// <summary>
/// Append-only history. Rows are written in the same transaction as the state change they describe,
/// and the database refuses UPDATE/DELETE on this table (see the trigger in the migration).
/// </summary>
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
