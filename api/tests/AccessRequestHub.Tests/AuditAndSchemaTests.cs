using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AccessRequestHub.Tests;

/// <summary>
/// Audit trail correctness and the database level invariants that must survive an application bug.
/// These tests deliberately talk to SQLite directly for the tampering cases.
/// </summary>
public class AuditAndSchemaTests
{
    [Fact]
    public async Task EveryTransition_WritesOneAuditEvent_WithActorFromToAndReason()
    {
        await using var app = AppHarness.Start();
        var alice = app.Client(Demo.Alice);
        var bob = app.Client(Demo.Bob);
        var carol = app.Client(Demo.Carol);

        var request = await alice.CreateOk("audit-1", Demo.Crm, "Production", "Admin");
        var afterManager = await (await bob.Approve(request.Id, request.Version)).ReadRequest();
        var detailResponse = await carol.Approve(request.Id, afterManager.Version);
        var detail = await detailResponse.ReadRequest();

        var audit = detail.Audit!;
        Assert.Equal(3, audit.Count);

        Assert.Equal("RequestCreated", audit[0].EventType);
        Assert.Null(audit[0].FromStatus);
        Assert.Equal("PendingManagerApproval", audit[0].ToStatus);
        Assert.Equal(Demo.Alice, audit[0].Actor.Email);

        Assert.Equal("Approved", audit[1].EventType);
        Assert.Equal("PendingManagerApproval", audit[1].FromStatus);
        Assert.Equal("PendingSystemOwnerApproval", audit[1].ToStatus);
        Assert.Equal(Demo.Bob, audit[1].Actor.Email);

        Assert.Equal("Approved", audit[2].EventType);
        Assert.Equal("PendingSystemOwnerApproval", audit[2].FromStatus);
        Assert.Equal("Approved", audit[2].ToStatus);
        Assert.Equal(Demo.Carol, audit[2].Actor.Email);

        // The timeline is ordered and append-only in id order.
        Assert.Equal(audit.Select(a => a.Id).OrderBy(id => id), audit.Select(a => a.Id));
    }

    [Fact]
    public async Task AuditEvents_CannotBeUpdatedOrDeletedByApplicationCode()
    {
        await using var app = AppHarness.Start();
        var alice = app.Client(Demo.Alice);
        var request = await alice.CreateOk("tamper-1");

        var update = await Assert.ThrowsAsync<SqliteException>(() => app.WithDbAsync(db =>
            db.Database.ExecuteSqlRawAsync($"UPDATE AuditEvents SET Reason = 'rewritten' WHERE AccessRequestId = {request.Id}")));
        Assert.Contains("append-only", update.Message);

        var delete = await Assert.ThrowsAsync<SqliteException>(() => app.WithDbAsync(db =>
            db.Database.ExecuteSqlRawAsync($"DELETE FROM AuditEvents WHERE AccessRequestId = {request.Id}")));
        Assert.Contains("append-only", delete.Message);

        Assert.Equal(1, await app.WithDbAsync(db => db.AuditEvents.CountAsync(a => a.AccessRequestId == request.Id)));
    }

    [Fact]
    public async Task DatabaseRejectsAnUnknownStatus_EvenIfTheApplicationLayerLetsItThrough()
    {
        await using var app = AppHarness.Start();
        var alice = app.Client(Demo.Alice);
        var request = await alice.CreateOk("check-1");

        var exception = await Assert.ThrowsAsync<SqliteException>(() => app.WithDbAsync(db =>
            db.Database.ExecuteSqlRawAsync($"UPDATE AccessRequests SET Status = 'Hacked' WHERE Id = {request.Id}")));

        Assert.Contains("CK_AccessRequests_Status", exception.Message);
    }

    [Fact]
    public async Task DatabaseRejectsADuplicateClientRequestId_IndependentlyOfApplicationCode()
    {
        await using var app = AppHarness.Start();
        var alice = app.Client(Demo.Alice);
        await alice.CreateOk("uq-1");

        const string insert = """
            INSERT INTO AccessRequests
                (ClientRequestId, RequesterId, ApplicationId, Environment, AccessLevel, Justification,
                 Status, PolicyVersion, Version, CreatedAtUtc, UpdatedAtUtc)
            VALUES
                ('uq-1', 1, 1, 'NonProduction', 'Read', 'bypassing the API entirely',
                 'PendingManagerApproval', 'v1', 1, '2026-01-01 00:00:00', '2026-01-01 00:00:00');
            """;

        var exception = await Assert.ThrowsAsync<SqliteException>(() => app.WithDbAsync(db =>
            db.Database.ExecuteSqlRawAsync(insert)));

        Assert.Contains("UNIQUE", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await app.WithDbAsync(db => db.AccessRequests.CountAsync(r => r.ClientRequestId == "uq-1")));
    }

    [Fact]
    public async Task ForeignKeys_AreEnforced()
    {
        await using var app = AppHarness.Start();

        var exception = await Assert.ThrowsAsync<SqliteException>(() => app.WithDbAsync(db =>
            db.Database.ExecuteSqlRawAsync("""
                INSERT INTO AccessRequests
                    (ClientRequestId, RequesterId, ApplicationId, Environment, AccessLevel, Justification,
                     Status, PolicyVersion, Version, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('fk-1', 999, 1, 'NonProduction', 'Read', 'requester does not exist',
                        'PendingManagerApproval', 'v1', 1, '2026-01-01 00:00:00', '2026-01-01 00:00:00');
                """)));

        Assert.Contains("FOREIGN KEY", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
