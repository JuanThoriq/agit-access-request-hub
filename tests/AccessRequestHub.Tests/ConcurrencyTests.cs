using System.Net;
using Microsoft.EntityFrameworkCore;

namespace AccessRequestHub.Tests;

/// <summary>
/// Stale and concurrent actions. The assertions are about how many transitions actually happened,
/// which is the only thing that matters: a request may not move twice on the same version.
/// </summary>
public class ConcurrencyTests
{
    [Fact]
    public async Task StaleVersion_IsConflict()
    {
        await using var app = AppHarness.Start();
        var alice = app.Client(Demo.Alice);
        var bob = app.Client(Demo.Bob);

        var request = await alice.CreateOk("stale-1", Demo.Crm, "Production", "Read");

        // Bob read version 1 in the morning and sends it hours later.
        var response = await bob.Approve(request.Id, version: request.Version + 7);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("version_conflict", await response.ReadCode());
        Assert.Contains("current version 1", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task VersionFromAnotherRequest_IsConflict()
    {
        await using var app = AppHarness.Start();
        var alice = app.Client(Demo.Alice);
        var bob = app.Client(Demo.Bob);

        var first = await alice.CreateOk("xver-1", Demo.Crm, "Production", "Read");
        var second = await alice.CreateOk("xver-2", Demo.Crm, "Production", "Read");
        var firstAfterApproval = await (await bob.Approve(first.Id, first.Version)).ReadRequest();

        var response = await bob.Approve(second.Id, version: firstAfterApproval.Version); // version 2 is stale for `second` (v1)

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("version_conflict", await response.ReadCode());
    }

    [Fact]
    public async Task TwoActionsOnTheSameVersion_ExactlyOneSucceeds()
    {
        await using var app = AppHarness.Start();
        var alice = app.Client(Demo.Alice);
        var bob = app.Client(Demo.Bob);

        var request = await alice.CreateOk("race-1", Demo.Crm, "Production", "Read");

        // Approve and reject fired at the same moment on the same version.
        var responses = await Task.WhenAll(
            bob.Approve(request.Id, request.Version),
            bob.Reject(request.Id, request.Version, "changed my mind"));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));

        var loser = responses.Single(r => r.StatusCode != HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.Conflict, loser.StatusCode);
        Assert.Contains(await loser.ReadCode(), new[] { "version_conflict", "invalid_transition" });

        // Exactly one transition reached the database, and it brought exactly one audit event.
        var state = await app.WithDbAsync(db => db.AccessRequests.FirstAsync(r => r.Id == request.Id));
        Assert.Equal(2, state.Version);

        var auditEvents = await app.WithDbAsync(db => db.AuditEvents.CountAsync(a => a.AccessRequestId == request.Id));
        Assert.Equal(2, auditEvents); // RequestCreated + the single accepted transition
    }

    [Fact]
    public async Task ParallelDuplicateCreates_ProduceOneRow()
    {
        await using var app = AppHarness.Start();
        const int parallelism = 4;

        var clients = Enumerable.Range(0, parallelism).Select(_ => app.Client(Demo.Alice)).ToArray();
        var responses = await Task.WhenAll(clients.Select(c => c.Create("par-1")));

        var ids = new List<int>();
        foreach (var response in responses)
        {
            Assert.True(
                response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK,
                $"unexpected status {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            ids.Add((await response.ReadRequest()).Id);
        }

        Assert.Single(ids.Distinct());
        Assert.Equal(1, await app.WithDbAsync(db => db.AccessRequests.CountAsync(r => r.ClientRequestId == "par-1")));
    }
}
