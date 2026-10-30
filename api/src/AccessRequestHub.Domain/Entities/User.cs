namespace AccessRequestHub.Domain.Entities;

public sealed class User
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Every request needs a manager approval first; a user without one cannot create requests.</summary>
    public int? ManagerId { get; set; }
    public User? Manager { get; set; }

    /// <summary>Admin/Auditor: can read everything, never an approver because of it.</summary>
    public bool IsAdmin { get; set; }
}
