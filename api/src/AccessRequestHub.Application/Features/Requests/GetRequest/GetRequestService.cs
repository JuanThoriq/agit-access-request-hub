using AccessRequestHub.Application.Common.Models;
using AccessRequestHub.Application.Features.Requests.Common;
using AccessRequestHub.Domain.Entities;
using AccessRequestHub.Domain.Workflow;
using AccessRequestHub.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AccessRequestHub.Application.Features.Requests.GetRequest;

public sealed class GetRequestService(AppDbContext db)
{
    public async Task<Result<AccessRequestResponse>> HandleAsync(User actor, int requestId, CancellationToken ct)
    {
        var request = await RequestQueries.WithNavigations(db).AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == requestId, ct);

        if (request is null)
        {
            return Result<AccessRequestResponse>.NotFound("request_not_found", $"Request {requestId} does not exist.");
        }

        if (!AccessWorkflow.CanView(actor, request))
        {
            return Result<AccessRequestResponse>.Forbidden("not_a_participant",
                "You are not the requester, their manager, the System Owner, or an Admin/Auditor.");
        }

        var audit = await RequestQueries.LoadAuditAsync(db, requestId, ct);
        return Result<AccessRequestResponse>.Success(RequestResponseMapper.ToResponse(request, actor, audit));
    }
}
