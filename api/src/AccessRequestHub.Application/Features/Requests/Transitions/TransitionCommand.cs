namespace AccessRequestHub.Application.Features.Requests.Transitions;

/// <summary>
/// Approve/reject payload. <see cref="Version"/> is the version the client last read: the accepted
/// transition is the one whose version still matches.
/// </summary>
public sealed record TransitionCommand(int? Version, string? Reason);
