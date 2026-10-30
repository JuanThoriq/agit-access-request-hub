using AccessRequestHub.Api.Common;
using AccessRequestHub.Domain.Entities;
using AccessRequestHub.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AccessRequestHub.Api.Middleware;

/// <summary>
/// Simulated authentication: the demo user is identified by the <c>X-Demo-User</c> header and
/// resolved server side to a seeded row. Nothing downstream trusts a requester id from the body
/// or the UI, so swapping the header only ever changes *who you are*, never *what you may do* —
/// that is still decided per request by the domain workflow.
/// </summary>
public sealed class CurrentUserMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Demo-User";
    public const string ItemKey = "current-user";

    public async Task InvokeAsync(HttpContext ctx, AppDbContext db)
    {
        if (!ctx.Request.Path.StartsWithSegments("/api"))
        {
            await next(ctx);
            return;
        }

        var email = ctx.Request.Headers[HeaderName].ToString().Trim().ToLowerInvariant();

        if (email.Length == 0)
        {
            await ApiProblems.Unauthorized(ctx, "missing_identity",
                $"Send the {HeaderName} header with one of the seeded demo users.").ExecuteAsync(ctx);
            return;
        }

        var user = await db.Users.AsNoTracking()
            .Include(u => u.Manager)
            .FirstOrDefaultAsync(u => u.Email == email, ctx.RequestAborted);

        if (user is null)
        {
            await ApiProblems.Unauthorized(ctx, "unknown_demo_user",
                $"'{email}' is not a seeded demo user.").ExecuteAsync(ctx);
            return;
        }

        ctx.Items[ItemKey] = user;
        await next(ctx);
    }
}

public static class HttpContextExtensions
{
    public static User CurrentUser(this HttpContext ctx) => (User)ctx.Items[CurrentUserMiddleware.ItemKey]!;
}
