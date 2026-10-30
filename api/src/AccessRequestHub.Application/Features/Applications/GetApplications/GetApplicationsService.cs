using AccessRequestHub.Application.Common.Mapping;
using AccessRequestHub.Application.Common.Models;
using AccessRequestHub.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AccessRequestHub.Application.Features.Applications.GetApplications;

public sealed class GetApplicationsService(AppDbContext db)
{
    /// <summary>Master data for the create form: which applications exist and who owns each one.</summary>
    public async Task<Result<IReadOnlyList<ApplicationRef>>> HandleAsync(CancellationToken ct)
    {
        var applications = await db.Applications.AsNoTracking()
            .Include(a => a.SystemOwner)
            .OrderBy(a => a.Name)
            .ToListAsync(ct);

        return Result<IReadOnlyList<ApplicationRef>>.Success(applications.Select(Mappers.ToRef).ToList());
    }
}
