using AccessRequestHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AccessRequestHub.Infrastructure;

/// <summary>
/// Rebuilds the database from migrations + seed. Idempotent: safe to run on every startup,
/// and it is the only way the schema is created (no manual SQL steps in the README).
/// </summary>
public static class DatabaseInitializer
{
    public static async Task InitializeAsync(AppDbContext db, ILogger logger, CancellationToken ct = default)
    {
        await db.Database.MigrateAsync(ct);

        if (await db.Users.AnyAsync(ct))
        {
            logger.LogInformation("Database already seeded, skipping seed");
            return;
        }

        logger.LogInformation("Seeding demo users and master data");

        // Fixed ids keep the demo deterministic and let the README/UI refer to the same people every run.
        // Everyone but Bob reports to Bob: the self-approval rule needs a requester who is also a
        // system owner and still has a manager (Carol on CRM).
        db.Users.AddRange(
            new User { Id = 1, Email = "alice@example.local", DisplayName = "Alice (Requester)", ManagerId = 2 },
            new User { Id = 2, Email = "bob@example.local", DisplayName = "Bob (Manager)" },
            new User { Id = 3, Email = "carol@example.local", DisplayName = "Carol (System Owner - CRM)", ManagerId = 2 },
            new User { Id = 4, Email = "dana@example.local", DisplayName = "Dana (System Owner - Finance Portal)", ManagerId = 2 },
            new User { Id = 5, Email = "erin@example.local", DisplayName = "Erin (Admin/Auditor)", IsAdmin = true, ManagerId = 2 });

        db.Applications.AddRange(
            new Application { Id = 1, Name = "CRM", SystemOwnerId = 3 },
            new Application { Id = 2, Name = "Finance Portal", SystemOwnerId = 4 });

        await db.SaveChangesAsync(ct);
    }
}
