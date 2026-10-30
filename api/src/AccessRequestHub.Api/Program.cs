using System.Diagnostics;
using AccessRequestHub.Api.Middleware;
using AccessRequestHub.Application.Features.Applications.GetApplications;
using AccessRequestHub.Application.Features.Identity.GetCurrentUser;
using AccessRequestHub.Application.Features.Requests.CreateRequest;
using AccessRequestHub.Application.Features.Requests.GetRequest;
using AccessRequestHub.Application.Features.Requests.ListRequests;
using AccessRequestHub.Application.Features.Requests.Transitions;
using AccessRequestHub.Domain.Entities;
using AccessRequestHub.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Resolve the SQLite file against the content root so `dotnet run` from anywhere lands in the same
// place; tests override this with an absolute temp path.
var sqlite = new SqliteConnectionStringBuilder(
    builder.Configuration.GetConnectionString("Default") ?? "Data Source=App_Data/access-request-hub.db");

if (!Path.IsPathRooted(sqlite.DataSource))
{
    sqlite.DataSource = Path.Combine(builder.Environment.ContentRootPath, sqlite.DataSource);
}

var directory = Path.GetDirectoryName(sqlite.DataSource);
if (!string.IsNullOrEmpty(directory))
{
    Directory.CreateDirectory(directory);
}

builder.Services.AddProblemDetails();
builder.Services.AddControllers();

builder.Services.AddDbContext<AppDbContext>(options => options
    .UseSqlite(sqlite.ToString())
    .AddInterceptors(new SqlitePragmaInterceptor()));

// One service per use case, all scoped: they share the request scoped DbContext.
builder.Services.AddScoped<GetCurrentUserService>();
builder.Services.AddScoped<GetApplicationsService>();
builder.Services.AddScoped<CreateRequestService>();
builder.Services.AddScoped<TransitionService>();
builder.Services.AddScoped<GetRequestService>();
builder.Services.AddScoped<ListRequestsService>();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Database");
    await DatabaseInitializer.InitializeAsync(db, logger);
}

app.UseExceptionHandler();

// Request log first, so the entry carries the resolved user and the final status code.
app.Use(async (context, next) =>
{
    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Http");
    var stopwatch = Stopwatch.StartNew();

    try
    {
        await next();
    }
    finally
    {
        var user = (context.Items[CurrentUserMiddleware.ItemKey] as User)?.Email ?? "anonymous";
        logger.LogInformation(
            "{Method} {Path} -> {StatusCode} in {ElapsedMs}ms user={User} traceId={TraceId}",
            context.Request.Method,
            context.Request.Path.Value,
            context.Response.StatusCode,
            stopwatch.ElapsedMilliseconds,
            user,
            context.TraceIdentifier);
    }
});

app.UseMiddleware<CurrentUserMiddleware>();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapControllers();

app.Run();

// Needed by WebApplicationFactory in the integration tests.
public partial class Program;
