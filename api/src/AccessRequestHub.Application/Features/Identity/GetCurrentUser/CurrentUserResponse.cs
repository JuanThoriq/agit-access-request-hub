using AccessRequestHub.Application.Common.Models;

namespace AccessRequestHub.Application.Features.Identity.GetCurrentUser;

/// <summary>Who am I, and what does the UI need to render my controls (owned apps, direct reports).</summary>
public sealed record CurrentUserResponse(
    UserRef User,
    bool IsAdmin,
    IReadOnlyList<ApplicationRef> OwnedApplications,
    IReadOnlyList<UserRef> DirectReports);
