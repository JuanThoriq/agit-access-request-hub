namespace AccessRequestHub.Application.Common.Models;

/// <summary>
/// The only user shape the API ever exposes. Never the entity: it also holds the manager link and
/// the admin flag, which a client has no business receiving for other people.
/// </summary>
public sealed record UserRef(int Id, string Email, string DisplayName);

public sealed record ApplicationRef(int Id, string Name, UserRef SystemOwner);
