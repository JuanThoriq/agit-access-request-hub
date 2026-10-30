using AccessRequestHub.Domain.Entities;
using AccessRequestHub.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AccessRequestHub.Application.Features.Requests.Common;

/// <summary>
/// Shared queries so every feature loads a request with the same graph: the authorization rules
/// read <c>Requester.ManagerId</c> and <c>Application.SystemOwnerId</c>, and a missing Include would
/// silently turn a real approver into "not assigned".
/// </summary>
internal static class RequestQueries
{
    public static IQueryable<AccessRequest> WithNavigations(AppDbContext db) => db.AccessRequests
        .Include(r => r.Requester).ThenInclude(u => u!.Manager)
        .Include(r => r.Application).ThenInclude(a => a.SystemOwner);

    public static Task<List<AuditEvent>> LoadAuditAsync(AppDbContext db, int requestId, CancellationToken ct)
        => db.AuditEvents.AsNoTracking()
            .Include(a => a.ActorUser)
            .Where(a => a.AccessRequestId == requestId)
            .OrderBy(a => a.Id)
            .ToListAsync(ct);
}
