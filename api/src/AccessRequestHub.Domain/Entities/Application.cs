namespace AccessRequestHub.Domain.Entities;

/// <summary>An application whose access can be requested. The owner is the second approver for high-risk access.</summary>
public sealed class Application
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SystemOwnerId { get; set; }
    public User SystemOwner { get; set; } = null!;
}
