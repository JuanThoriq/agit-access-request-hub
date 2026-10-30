using AccessRequestHub.Application.Common.Mapping;
using AccessRequestHub.Application.Common.Models;
using AccessRequestHub.Application.Features.Identity.GetCurrentUser;
using AccessRequestHub.Domain.Entities;
using AccessRequestHub.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AccessRequestHub.Application.Features.Identity.GetCurrentUser;

public sealed class GetCurrentUserService(AppDbContext db)
{
    public async Task<Result<CurrentUserResponse>> HandleAsync(User actor, CancellationToken ct)
    {
        var owned = await db.Applications.AsNoTracking()
            .Include(a => a.SystemOwner)
            .Where(a => a.SystemOwnerId == actor.Id)
            .OrderBy(a => a.Name)
            .ToListAsync(ct);

        var reports = await db.Users.AsNoTracking()
            .Where(u => u.ManagerId == actor.Id)
            .OrderBy(u => u.DisplayName)
            .ToListAsync(ct);

        return Result<CurrentUserResponse>.Success(new CurrentUserResponse(
            Mappers.ToRef(actor),
            actor.IsAdmin,
            owned.Select(Mappers.ToRef).ToList(),
            reports.Select(Mappers.ToRef).ToList()));
    }
}
