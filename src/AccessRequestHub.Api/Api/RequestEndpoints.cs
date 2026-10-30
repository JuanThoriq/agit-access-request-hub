using AccessRequestHub.Api.Contracts;
using AccessRequestHub.Api.Data;
using AccessRequestHub.Api.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AccessRequestHub.Api.Api;

public static class RequestEndpoints
{
    private const int JustificationMinLength = 10;
    private const int JustificationMaxLength = 2000;
    private const int ReasonMaxLength = 2000;

    public static void MapRequestEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/me", GetMe);
        api.MapGet("/applications", GetApplications);

        api.MapGet("/requests", GetMyRequests);
        api.MapGet("/requests/all", GetAllRequests);
        api.MapGet("/requests/{id:int}", GetRequest);
        api.MapGet("/inbox", GetInbox);

        api.MapPost("/requests", CreateRequest);
        api.MapPost("/requests/{id:int}/approve", (HttpContext ctx, AppDbContext db, int id, TransitionRequest body, ILogger<AccessRequest> log, CancellationToken ct)
            => TransitionAsync(ctx, db, id, body, isApproval: true, log, ct));
        api.MapPost("/requests/{id:int}/reject", (HttpContext ctx, AppDbContext db, int id, TransitionRequest body, ILogger<AccessRequest> log, CancellationToken ct)
            => TransitionAsync(ctx, db, id, body, isApproval: false, log, ct));
    }

    // ---------------------------------------------------------------- reads

    private static async Task<IResult> GetMe(HttpContext ctx, AppDbContext db, CancellationToken ct)
    {
        var user = ctx.CurrentUser();

        var owned = await db.Applications.AsNoTracking()
            .Include(a => a.SystemOwner)
            .Where(a => a.SystemOwnerId == user.Id)
            .OrderBy(a => a.Name)
            .ToListAsync(ct);

        var reports = await db.Users.AsNoTracking()
            .Where(u => u.ManagerId == user.Id)
            .OrderBy(u => u.DisplayName)
            .ToListAsync(ct);

        return Results.Ok(new CurrentUserDto(
            RequestMapper.ToRef(user),
            user.IsAdmin,
            owned.Select(RequestMapper.ToRef).ToList(),
            reports.Select(RequestMapper.ToRef).ToList()));
    }

    private static async Task<IResult> GetApplications(AppDbContext db, CancellationToken ct)
    {
        var applications = await db.Applications.AsNoTracking()
            .Include(a => a.SystemOwner)
            .OrderBy(a => a.Name)
            .ToListAsync(ct);

        return Results.Ok(applications.Select(RequestMapper.ToRef).ToList());
    }

    private static async Task<IResult> GetMyRequests(HttpContext ctx, AppDbContext db, CancellationToken ct)
    {
        var user = ctx.CurrentUser();

        var requests = await WithNavigations(db).AsNoTracking()
            .Where(r => r.RequesterId == user.Id)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync(ct);

        return Results.Ok(requests.Select(r => RequestMapper.ToDto(r, user)).ToList());
    }

    private static async Task<IResult> GetAllRequests(HttpContext ctx, AppDbContext db, CancellationToken ct)
    {
        var user = ctx.CurrentUser();

        // Admin/Auditor can read everything, but is never an approver because of it.
        if (!user.IsAdmin)
        {
            return ApiProblems.Forbidden(ctx, "admin_only", "Only Admin/Auditor can list every request.");
        }

        var requests = await WithNavigations(db).AsNoTracking()
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync(ct);

        return Results.Ok(requests.Select(r => RequestMapper.ToDto(r, user)).ToList());
    }

    private static async Task<IResult> GetRequest(HttpContext ctx, AppDbContext db, int id, CancellationToken ct)
    {
        var user = ctx.CurrentUser();

        var request = await WithNavigations(db).AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (request is null)
        {
            return ApiProblems.NotFound(ctx, "request_not_found", $"Request {id} does not exist.");
        }

        if (!AccessWorkflow.CanView(user, request))
        {
            return ApiProblems.Forbidden(ctx, "not_a_participant",
                "You are not the requester, their manager, the System Owner, or an Admin/Auditor.");
        }

        var audit = await LoadAuditAsync(db, id, ct);
        return Results.Ok(RequestMapper.ToDto(request, user, audit));
    }

    private static async Task<IResult> GetInbox(HttpContext ctx, AppDbContext db, CancellationToken ct)
    {
        var user = ctx.CurrentUser();

        // Same rule as AccessWorkflow.CheckApprover, expressed as SQL so the inbox stays cheap:
        // a request is mine to act on only while it waits on my own step, and never my own request.
        var requests = await WithNavigations(db).AsNoTracking()
            .Where(r => r.RequesterId != user.Id
                        && ((r.Status == RequestStatus.PendingManagerApproval && r.Requester.ManagerId == user.Id)
                            || (r.Status == RequestStatus.PendingSystemOwnerApproval && r.Application.SystemOwnerId == user.Id)))
            .OrderBy(r => r.CreatedAtUtc)
            .ToListAsync(ct);

        return Results.Ok(requests.Select(r => RequestMapper.ToDto(r, user)).ToList());
    }

    // ---------------------------------------------------------------- create

    private static async Task<IResult> CreateRequest(
        HttpContext ctx,
        AppDbContext db,
        CreateAccessRequestRequest body,
        ILogger<AccessRequest> log,
        CancellationToken ct)
    {
        var user = ctx.CurrentUser();

        var errors = new Dictionary<string, List<string>>();

        var clientRequestId = (body.ClientRequestId ?? string.Empty).Trim();
        if (clientRequestId.Length == 0)
        {
            Add(errors, "clientRequestId", "clientRequestId is required and must be stable across retries.");
        }
        else if (clientRequestId.Length > 100)
        {
            Add(errors, "clientRequestId", "clientRequestId must be at most 100 characters.");
        }

        EnvironmentType? environment = TryParseEnum<EnvironmentType>(body.Environment, out var parsedEnvironment) ? parsedEnvironment : null;
        if (environment is null)
        {
            Add(errors, "environment", "environment must be 'NonProduction' or 'Production'.");
        }

        AccessLevel? accessLevel = TryParseEnum<AccessLevel>(body.AccessLevel, out var parsedAccessLevel) ? parsedAccessLevel : null;
        if (accessLevel is null)
        {
            Add(errors, "accessLevel", "accessLevel must be 'Read' or 'Admin'.");
        }

        var justification = (body.Justification ?? string.Empty).Trim();
        if (justification.Length < JustificationMinLength)
        {
            Add(errors, "justification", $"justification is required (min {JustificationMinLength} characters).");
        }
        else if (justification.Length > JustificationMaxLength)
        {
            Add(errors, "justification", $"justification must be at most {JustificationMaxLength} characters.");
        }

        Application? application = null;
        if (body.ApplicationId is null)
        {
            Add(errors, "applicationId", "applicationId is required.");
        }
        else
        {
            application = await db.Applications.AsNoTracking()
                .Include(a => a.SystemOwner)
                .FirstOrDefaultAsync(a => a.Id == body.ApplicationId, ct);

            if (application is null)
            {
                Add(errors, "applicationId", $"Application {body.ApplicationId} does not exist.");
            }
        }

        if (errors.Count > 0)
        {
            return ApiProblems.Validation(ctx, errors);
        }

        // Every request needs a manager approval first, so a requester without a manager would create
        // a request nobody can ever approve. Refuse loudly instead of storing a dead request.
        if (user.ManagerId is null)
        {
            return ApiProblems.Conflict(ctx, "manager_not_configured",
                "Your account has no manager assigned, so the mandatory manager approval step cannot run.");
        }

        // environment/accessLevel/application are non-null here because of the checks above.
        var env = environment!.Value;
        var level = accessLevel!.Value;
        var app = application!;

        // Fast path for a retry after a timeout: return what was already stored.
        var existing = await WithNavigations(db).AsNoTracking()
            .FirstOrDefaultAsync(r => r.RequesterId == user.Id && r.ClientRequestId == clientRequestId, ct);

        if (existing is not null)
        {
            return await Replay(ctx, db, existing, user, app, env, level, justification, log, ct);
        }

        var now = DateTime.UtcNow;
        var request = new AccessRequest
        {
            ClientRequestId = clientRequestId,
            RequesterId = user.Id,
            ApplicationId = app.Id,
            Environment = env,
            AccessLevel = level,
            Justification = justification,
            Status = RequestStatus.PendingManagerApproval,
            PolicyVersion = AccessWorkflow.Phase1PolicyVersion,
            Version = 1,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        db.AccessRequests.Add(request);

        // Audit row is added to the same context, so the state change and its audit event are
        // committed in one transaction. There is no code path that writes one without the other.
        db.AuditEvents.Add(new AuditEvent
        {
            AccessRequest = request,
            ActorUserId = user.Id,
            EventType = "RequestCreated",
            FromStatus = null,
            ToStatus = RequestStatus.PendingManagerApproval,
            CreatedAtUtc = now
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // A concurrent retry passed the read above and won the insert. The unique index is what
            // makes this correct: the loser recovers by returning the winner's row, never a 500.
            db.ChangeTracker.Clear(); // the failed insert (request + audit event) must not stay tracked

            var winner = await WithNavigations(db).AsNoTracking()
                .FirstOrDefaultAsync(r => r.RequesterId == user.Id && r.ClientRequestId == clientRequestId, ct);

            if (winner is null)
            {
                throw; // a different constraint broke; do not hide it as an idempotent replay
            }

            log.LogInformation(
                "Idempotent create replay after unique violation requestId={RequestId} clientRequestId={ClientRequestId}",
                winner.Id, clientRequestId);

            return await Replay(ctx, db, winner, user, app, env, level, justification, log, ct);
        }

        log.LogInformation(
            "Request created requestId={RequestId} requesterId={RequesterId} application={Application} environment={Environment} accessLevel={AccessLevel} highRisk={HighRisk}",
            request.Id, user.Id, app.Name, env, level, AccessWorkflow.IsHighRisk(env, level));

        // Re-read with navigations so the response carries the requester and application snapshots;
        // the in-memory entity only holds their ids.
        var created = await WithNavigations(db).AsNoTracking().FirstAsync(r => r.Id == request.Id, ct);
        var audit = await LoadAuditAsync(db, created.Id, ct);
        return Results.Created($"/api/requests/{created.Id}", RequestMapper.ToDto(created, user, audit));
    }

    private static async Task<IResult> Replay(
        HttpContext ctx,
        AppDbContext db,
        AccessRequest existing,
        User user,
        Application application,
        EnvironmentType environment,
        AccessLevel accessLevel,
        string justification,
        ILogger log,
        CancellationToken ct)
    {
        // Reusing a key for a different payload is a client bug, not a retry: say so instead of
        // silently returning a request that does not describe what the caller asked for.
        if (existing.ApplicationId != application.Id
            || existing.Environment != environment
            || existing.AccessLevel != accessLevel
            || existing.Justification != justification)
        {
            return ApiProblems.Conflict(ctx, "idempotency_key_reuse",
                "This clientRequestId was already used with a different payload.");
        }

        var audit = await LoadAuditAsync(db, existing.Id, ct);
        return Results.Ok(RequestMapper.ToDto(existing, user, audit));
    }

    // ---------------------------------------------------------------- transitions

    private static async Task<IResult> TransitionAsync(
        HttpContext ctx,
        AppDbContext db,
        int id,
        TransitionRequest body,
        bool isApproval,
        ILogger log,
        CancellationToken ct)
    {
        var user = ctx.CurrentUser();

        if (body.Version is null)
        {
            return ApiProblems.Validation(ctx, new Dictionary<string, List<string>>
            {
                ["version"] = ["version is required: send the version you last read."]
            });
        }

        var request = await WithNavigations(db).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (request is null)
        {
            return ApiProblems.NotFound(ctx, "request_not_found", $"Request {id} does not exist.");
        }

        // Check order is deliberate:
        // 1. invisible requests stay invisible (403), so a stranger learns nothing about state or version
        // 2. terminal state (409) and stale version (409) come before the approver check, because a
        //    participant acting on a version that already moved deserves "conflict", not "forbidden"
        // 3. only then, self approval and the assigned approver for the current step (403)
        if (!AccessWorkflow.CanView(user, request))
        {
            return ApiProblems.Forbidden(ctx, "not_a_participant",
                "You are not the requester, their manager, the System Owner, or an Admin/Auditor.");
        }

        var denial = AccessWorkflow.CheckApprover(user, request);
        if (denial == Denial.RequestIsTerminal)
        {
            return ApiProblems.ForDenial(ctx, denial, request, user);
        }

        if (!isApproval && string.IsNullOrWhiteSpace(body.Reason))
        {
            return ApiProblems.Validation(ctx, new Dictionary<string, List<string>>
            {
                ["reason"] = ["reason is required when rejecting."]
            });
        }

        var reason = isApproval ? null : body.Reason!.Trim();
        if (reason is not null && reason.Length > ReasonMaxLength)
        {
            return ApiProblems.Validation(ctx, new Dictionary<string, List<string>>
            {
                ["reason"] = [$"reason must be at most {ReasonMaxLength} characters."]
            });
        }

        if (body.Version.Value != request.Version)
        {
            return VersionConflict(ctx, request);
        }

        if (denial != Denial.None)
        {
            return ApiProblems.ForDenial(ctx, denial, request, user);
        }

        var from = request.Status;
        var to = isApproval
            ? AccessWorkflow.StatusAfterApproval(from, AccessWorkflow.IsHighRisk(request.Environment, request.AccessLevel))!.Value
            : RequestStatus.Rejected;

        var now = DateTime.UtcNow;
        request.Status = to;
        request.Version += 1;
        request.UpdatedAtUtc = now;

        db.AuditEvents.Add(new AuditEvent
        {
            AccessRequestId = request.Id,
            ActorUserId = user.Id,
            EventType = isApproval ? "Approved" : "Rejected",
            FromStatus = from,
            ToStatus = to,
            Reason = reason,
            CreatedAtUtc = now
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Two requests passed the version check at the same time; the UPDATE ... WHERE Version = @v
            // let exactly one through. This is the real guard, the check above is only a fast path.
            return VersionConflict(ctx, request);
        }

        log.LogInformation(
            "Transition requestId={RequestId} from={From} to={To} actorId={ActorId} version={Version}",
            request.Id, from, to, user.Id, request.Version);

        var audit = await LoadAuditAsync(db, request.Id, ct);
        return Results.Ok(RequestMapper.ToDto(request, user, audit));
    }

    private static IResult VersionConflict(HttpContext ctx, AccessRequest request)
        => ApiProblems.Conflict(ctx, "version_conflict",
            $"Request was changed by someone else (current version {request.Version}, status {request.Status}). Reload and retry.");

    // ---------------------------------------------------------------- helpers

    private static IQueryable<AccessRequest> WithNavigations(AppDbContext db) => db.AccessRequests
        .Include(r => r.Requester).ThenInclude(u => u!.Manager)
        .Include(r => r.Application).ThenInclude(a => a.SystemOwner);

    private static Task<List<AuditEvent>> LoadAuditAsync(AppDbContext db, int requestId, CancellationToken ct)
        => db.AuditEvents.AsNoTracking()
            .Include(a => a.ActorUser)
            .Where(a => a.AccessRequestId == requestId)
            .OrderBy(a => a.Id)
            .ToListAsync(ct);

    private static bool TryParseEnum<TEnum>(string? value, out TEnum parsed) where TEnum : struct, Enum
    {
        parsed = default;
        return !string.IsNullOrWhiteSpace(value)
               && Enum.TryParse(value.Trim(), ignoreCase: true, out parsed)
               && Enum.IsDefined(parsed);
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
        => ex.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 }; // SQLITE_CONSTRAINT_UNIQUE

    private static void Add(Dictionary<string, List<string>> errors, string field, string message)
    {
        if (!errors.TryGetValue(field, out var list))
        {
            errors[field] = list = new List<string>();
        }

        list.Add(message);
    }
}
