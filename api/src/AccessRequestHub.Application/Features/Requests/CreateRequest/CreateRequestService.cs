using AccessRequestHub.Application.Common.Models;
using AccessRequestHub.Application.Features.Requests.Common;
using AccessRequestHub.Domain;
using AccessRequestHub.Domain.Entities;
using AccessRequestHub.Domain.Workflow;
using ApplicationEntity = AccessRequestHub.Domain.Entities.Application;
using AccessRequestHub.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AccessRequestHub.Application.Features.Requests.CreateRequest;

public sealed class CreateRequestService(AppDbContext db, ILogger<CreateRequestService> log)
{
    private const int JustificationMinLength = 10;
    private const int JustificationMaxLength = 2000;

    public async Task<Result<CreateRequestOutcome>> HandleAsync(
        User actor,
        CreateRequestCommand command,
        CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();

        var clientRequestId = (command.ClientRequestId ?? string.Empty).Trim();
        if (clientRequestId.Length == 0)
        {
            errors["clientRequestId"] = ["clientRequestId is required and must be stable across retries."];
        }
        else if (clientRequestId.Length > 100)
        {
            errors["clientRequestId"] = ["clientRequestId must be at most 100 characters."];
        }

        var environment = TryParseEnum<EnvironmentType>(command.Environment, out var parsedEnvironment)
            ? parsedEnvironment
            : (EnvironmentType?)null;
        if (environment is null)
        {
            errors["environment"] = ["environment must be 'NonProduction' or 'Production'."];
        }

        var accessLevel = TryParseEnum<AccessLevel>(command.AccessLevel, out var parsedAccessLevel)
            ? parsedAccessLevel
            : (AccessLevel?)null;
        if (accessLevel is null)
        {
            errors["accessLevel"] = ["accessLevel must be 'Read' or 'Admin'."];
        }

        var justification = (command.Justification ?? string.Empty).Trim();
        if (justification.Length < JustificationMinLength)
        {
            errors["justification"] = [$"justification is required (min {JustificationMinLength} characters)."];
        }
        else if (justification.Length > JustificationMaxLength)
        {
            errors["justification"] = [$"justification must be at most {JustificationMaxLength} characters."];
        }

        ApplicationEntity? application = null;
        if (command.ApplicationId is null)
        {
            errors["applicationId"] = ["applicationId is required."];
        }
        else
        {
            application = await db.Applications.AsNoTracking()
                .Include(a => a.SystemOwner)
                .FirstOrDefaultAsync(a => a.Id == command.ApplicationId, ct);

            if (application is null)
            {
                errors["applicationId"] = [$"Application {command.ApplicationId} does not exist."];
            }
        }

        if (errors.Count > 0)
        {
            return Result<CreateRequestOutcome>.Validation(errors);
        }

        // Every request needs a manager approval first, so a requester without a manager would create
        // a request nobody can ever approve. Refuse loudly instead of storing a dead request.
        if (actor.ManagerId is null)
        {
            return Result<CreateRequestOutcome>.Conflict("manager_not_configured",
                "Your account has no manager assigned, so the mandatory manager approval step cannot run.");
        }

        var env = environment!.Value;
        var level = accessLevel!.Value;
        var app = application!;

        // Fast path for a retry after a timeout: return what was already stored.
        var existing = await RequestQueries.WithNavigations(db).AsNoTracking()
            .FirstOrDefaultAsync(r => r.RequesterId == actor.Id && r.ClientRequestId == clientRequestId, ct);

        if (existing is not null)
        {
            return await ReplayAsync(existing, actor, app, env, level, justification, ct);
        }

        var now = DateTime.UtcNow;
        var request = new AccessRequest
        {
            ClientRequestId = clientRequestId,
            RequesterId = actor.Id,
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

        // The audit row is added to the same context, so the state change and its audit event are
        // committed in one transaction. There is no code path that writes one without the other.
        db.AuditEvents.Add(new AuditEvent
        {
            AccessRequest = request,
            ActorUserId = actor.Id,
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

            var winner = await RequestQueries.WithNavigations(db).AsNoTracking()
                .FirstOrDefaultAsync(r => r.RequesterId == actor.Id && r.ClientRequestId == clientRequestId, ct);

            if (winner is null)
            {
                throw; // a different constraint broke; do not hide it as an idempotent replay
            }

            log.LogInformation(
                "Idempotent create replay after unique violation requestId={RequestId} clientRequestId={ClientRequestId}",
                winner.Id, clientRequestId);

            return await ReplayAsync(winner, actor, app, env, level, justification, ct);
        }

        log.LogInformation(
            "Request created requestId={RequestId} requesterId={RequesterId} application={Application} environment={Environment} accessLevel={AccessLevel} highRisk={HighRisk}",
            request.Id, actor.Id, app.Name, env, level, AccessWorkflow.IsHighRisk(env, level));

        // Re-read with navigations so the response carries the requester and application snapshots;
        // the in-memory entity only holds their ids.
        var created = await RequestQueries.WithNavigations(db).AsNoTracking().FirstAsync(r => r.Id == request.Id, ct);
        var audit = await RequestQueries.LoadAuditAsync(db, created.Id, ct);
        return Result<CreateRequestOutcome>.Success(new CreateRequestOutcome(true, RequestResponseMapper.ToResponse(created, actor, audit)));
    }

    private async Task<Result<CreateRequestOutcome>> ReplayAsync(
        AccessRequest existing,
        User actor,
        ApplicationEntity application,
        EnvironmentType environment,
        AccessLevel accessLevel,
        string justification,
        CancellationToken ct)
    {
        // Reusing a key for a different payload is a client bug, not a retry: say so instead of
        // silently returning a request that does not describe what the caller asked for.
        if (existing.ApplicationId != application.Id
            || existing.Environment != environment
            || existing.AccessLevel != accessLevel
            || existing.Justification != justification)
        {
            return Result<CreateRequestOutcome>.Conflict("idempotency_key_reuse",
                "This clientRequestId was already used with a different payload.");
        }

        var audit = await RequestQueries.LoadAuditAsync(db, existing.Id, ct);
        return Result<CreateRequestOutcome>.Success(new CreateRequestOutcome(false, RequestResponseMapper.ToResponse(existing, actor, audit)));
    }

    private static bool TryParseEnum<TEnum>(string? value, out TEnum parsed) where TEnum : struct, Enum
    {
        parsed = default;
        return !string.IsNullOrWhiteSpace(value)
               && Enum.TryParse(value.Trim(), ignoreCase: true, out parsed)
               && Enum.IsDefined(parsed);
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
        => ex.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 }; // SQLITE_CONSTRAINT_UNIQUE
}
