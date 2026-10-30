using System.Net;
using AccessRequestHub.Application.Features.Requests.Common;

namespace AccessRequestHub.Tests;

/// <summary>
/// The Phase 1 workflow and its demo scenarios. Each test starts from an empty database and drives
/// the API exactly like the UI does.
/// </summary>
public class WorkflowTests
{
    [Fact]
    public async Task StandardRequest_IsApprovedAfterManagerApproval()
    {
        await using var app = AppHarness.Start();
        var alice = app.Client(Demo.Alice);
        var bob = app.Client(Demo.Bob);

        var request = await alice.CreateOk("std-1", Demo.Crm, "NonProduction", "Read");
        Assert.Equal("PendingManagerApproval", request.Status);
        Assert.False(request.IsHighRisk);
        Assert.Equal("v1", request.PolicyVersion);

        var approved = await bob.Approve(request.Id, request.Version);

        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        var updated = await approved.ReadRequest();
        Assert.Equal("Approved", updated.Status);
        Assert.Equal(2, updated.Version);
    }

    [Fact]
    public async Task ProductionRequest_NeedsSystemOwnerAfterManager()
    {
        await using var app = AppHarness.Start();
        var alice = app.Client(Demo.Alice);
        var bob = app.Client(Demo.Bob);
        var carol = app.Client(Demo.Carol);

        var request = await alice.CreateOk("prod-1", Demo.Crm, "Production", "Read");
        Assert.True(request.IsHighRisk);

        var afterManager = await (await bob.Approve(request.Id, request.Version)).ReadRequest();
        Assert.Equal("PendingSystemOwnerApproval", afterManager.Status);

        var afterOwner = await carol.Approve(request.Id, afterManager.Version);
        Assert.Equal(HttpStatusCode.OK, afterOwner.StatusCode);
        Assert.Equal("Approved", (await afterOwner.ReadRequest()).Status);
    }

    [Fact]
    public async Task AdminAccessRequest_NeedsSystemOwnerAfterManager()
    {
        await using var app = AppHarness.Start();
        var alice = app.Client(Demo.Alice);
        var bob = app.Client(Demo.Bob);
        var dana = app.Client(Demo.Dana);

        var request = await alice.CreateOk("admin-1", Demo.FinancePortal, "NonProduction", "Admin");
        Assert.True(request.IsHighRisk);

        var afterManager = await (await bob.Approve(request.Id, request.Version)).ReadRequest();
        Assert.Equal("PendingSystemOwnerApproval", afterManager.Status);

        var afterOwner = await dana.Approve(request.Id, afterManager.Version);
        Assert.Equal("Approved", (await afterOwner.ReadRequest()).Status);
    }

    [Fact]
    public async Task ApprovingAtTheWrongStep_IsForbidden()
    {
        await using var app = AppHarness.Start();
        var alice = app.Client(Demo.Alice);
        var carol = app.Client(Demo.Carol);

        var request = await alice.CreateOk("step-1", Demo.Crm, "Production", "Read");

        // Carol owns CRM, but the manager step is not hers yet.
        var response = await carol.Approve(request.Id, request.Version);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("not_assigned_approver", await response.ReadCode());
    }

    [Fact]
    public async Task RejectWithoutReason_IsValidationError()
    {
        await using var app = AppHarness.Start();
        var alice = app.Client(Demo.Alice);
        var bob = app.Client(Demo.Bob);

        var request = await alice.CreateOk("rej-1", Demo.Crm, "NonProduction", "Read");

        var response = await bob.Reject(request.Id, request.Version, reason: "   ");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = await response.ReadJson();
        Assert.Equal("validation_failed", problem.RootElement.GetProperty("code").GetString());
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("reason", out _));
    }

    [Fact]
    public async Task RejectedRequest_KeepsReasonAndIsTerminal()
    {
        await using var app = AppHarness.Start();
        var alice = app.Client(Demo.Alice);
        var bob = app.Client(Demo.Bob);

        var request = await alice.CreateOk("rej-2", Demo.Crm, "Production", "Read");

        var rejected = await bob.Reject(request.Id, request.Version, "Use the shared service account instead");
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        var rejectedDto = await rejected.ReadRequest();
        Assert.Equal("Rejected", rejectedDto.Status);

        var detail = await (await alice.GetAsync($"/api/requests/{request.Id}")).ReadRequest();
        var lastAudit = detail.Audit!.Last();
        Assert.Equal("Rejected", lastAudit.EventType);
        Assert.Equal("Use the shared service account instead", lastAudit.Reason);
        Assert.Equal(Demo.Bob, lastAudit.Actor.Email);

        // Nothing may move a Rejected request, not even the approver who just rejected it.
        var again = await bob.Approve(request.Id, rejectedDto.Version);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("invalid_transition", await again.ReadCode());
    }

    [Fact]
    public async Task ApprovedRequest_IsTerminal()
    {
        await using var app = AppHarness.Start();
        var alice = app.Client(Demo.Alice);
        var bob = app.Client(Demo.Bob);
        var carol = app.Client(Demo.Carol);

        var request = await alice.CreateOk("term-1", Demo.Crm, "NonProduction", "Read");
        var approved = await (await bob.Approve(request.Id, request.Version)).ReadRequest();

        var response = await carol.Reject(request.Id, approved.Version, "too late");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("invalid_transition", await response.ReadCode());
    }

    [Theory]
    [InlineData("", "environment")]
    [InlineData("Staging", "environment")]
    public async Task InvalidEnvironment_IsValidationError(string environment, string expectedField)
    {
        await using var app = AppHarness.Start();
        var alice = app.Client(Demo.Alice);

        var response = await alice.Create("env-1", Demo.Crm, environment);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = await response.ReadJson();
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty(expectedField, out _));
    }

    [Fact]
    public async Task ShortJustification_AndUnknownApplication_AreValidationErrors()
    {
        await using var app = AppHarness.Start();
        var alice = app.Client(Demo.Alice);

        var shortReason = await alice.Create("val-1", justification: "pls");
        Assert.Equal(HttpStatusCode.BadRequest, shortReason.StatusCode);

        var unknownApp = await alice.Create("val-2", applicationId: 999);
        Assert.Equal(HttpStatusCode.BadRequest, unknownApp.StatusCode);
        using var problem = await unknownApp.ReadJson();
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("applicationId", out _));
    }

    [Fact]
    public async Task DuplicateClientRequestId_ReturnsTheSameRequest()
    {
        await using var app = AppHarness.Start();
        var alice = app.Client(Demo.Alice);

        var first = await alice.Create("dup-1");
        var replay = await alice.Create("dup-1");

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal((await first.ReadRequest()).Id, (await replay.ReadRequest()).Id);
    }

    [Fact]
    public async Task DuplicateClientRequestId_WithDifferentPayload_IsConflict()
    {
        await using var app = AppHarness.Start();
        var alice = app.Client(Demo.Alice);

        await alice.CreateOk("dup-2", Demo.Crm, "NonProduction", "Read");
        var conflicting = await alice.Create("dup-2", Demo.Crm, "Production", "Admin");

        Assert.Equal(HttpStatusCode.Conflict, conflicting.StatusCode);
        Assert.Equal("idempotency_key_reuse", await conflicting.ReadCode());
    }

    [Fact]
    public async Task Inbox_ContainsOnlyRequestsWaitingOnTheActor()
    {
        await using var app = AppHarness.Start();
        var alice = app.Client(Demo.Alice);
        var bob = app.Client(Demo.Bob);
        var carol = app.Client(Demo.Carol);

        var production = await alice.CreateOk("inbox-1", Demo.Crm, "Production", "Read");
        var standard = await alice.CreateOk("inbox-2", Demo.Crm, "NonProduction", "Read");

        // Both wait on Bob; Alice's own requests never appear in her own inbox.
        Assert.Equal(2, (await (await bob.GetAsync("/api/inbox")).ReadRequestList()).Count);

        await bob.Approve(production.Id, production.Version); // high-risk: moves on to Carol
        await bob.Approve(standard.Id, standard.Version);     // not high-risk: done

        var carolInbox = await (await carol.GetAsync("/api/inbox")).ReadRequestList();
        Assert.Single(carolInbox);
        Assert.Equal(production.Id, carolInbox[0].Id);

        Assert.Empty(await (await alice.GetAsync("/api/inbox")).ReadRequestList());
        Assert.Empty(await (await bob.GetAsync("/api/inbox")).ReadRequestList());
    }
}
