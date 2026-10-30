using AccessRequestHub.Application.Common.Models;
using AccessRequestHub.Application.Features.Requests.Common;
using AccessRequestHub.Domain;
using AccessRequestHub.Domain.Entities;
using AccessRequestHub.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AccessRequestHub.Application.Features.Requests.ListRequests;

/// <summary>The three read views of the same data. Each is one query, so they stay in one place.</summary>
public sealed class ListRequestsService(AppDbContext db)
{
    /// <summary>Everything I asked for.</summary>
    public async Task<Result<IReadOnlyList<AccessRequestResponse>>> MineAsync(User actor, CancellationToken ct)
    {
        var requests = await RequestQueries.WithNavigations(db).AsNoTracking()
            .Where(r => r.RequesterId == actor.Id)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync(ct);

        return Result<IReadOnlyList<AccessRequestResponse>>.Success(ToResponses(requests, actor));
    }

    /// <summary>Everything, for the Admin/Auditor. Reading all is not the same as being able to decide.</summary>
    public async Task<Result<IReadOnlyList<AccessRequestResponse>>> AllAsync(User actor, CancellationToken ct)
    {
        if (!actor.IsAdmin)
        {
            return Result<IReadOnlyList<AccessRequestResponse>>.Forbidden("admin_only",
                "Only Admin/Auditor can list every request.");
        }

        var requests = await RequestQueries.WithNavigations(db).AsNoTracking()
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync(ct);

        return Result<IReadOnlyList<AccessRequestResponse>>.Success(ToResponses(requests, actor));
    }

    /// <summary>
    /// The queue waiting on me. Same rule as <see cref="AccessWorkflow.CheckApprover"/>, expressed as
    /// SQL so the inbox stays cheap: mine only while it waits on my own step, and never my own request.
    /// </summary>
    public async Task<Result<IReadOnlyList<AccessRequestResponse>>> InboxAsync(User actor, CancellationToken ct)
    {
        var requests = await RequestQueries.WithNavigations(db).AsNoTracking()
            .Where(r => r.RequesterId != actor.Id
                        && ((r.Status == RequestStatus.PendingManagerApproval && r.Requester.ManagerId == actor.Id)
                            || (r.Status == RequestStatus.PendingSystemOwnerApproval && r.Application.SystemOwnerId == actor.Id)))
            .OrderBy(r => r.CreatedAtUtc)
            .ToListAsync(ct);

        return Result<IReadOnlyList<AccessRequestResponse>>.Success(ToResponses(requests, actor));
    }

    private static IReadOnlyList<AccessRequestResponse> ToResponses(IEnumerable<AccessRequest> requests, User actor)
        => requests.Select(r => RequestResponseMapper.ToResponse(r, actor)).ToList();
}
