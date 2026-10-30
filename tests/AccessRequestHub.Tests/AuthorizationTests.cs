using System.Net.Http.Json;
using System.Net;

namespace AccessRequestHub.Tests;

/// <summary>
/// Authorization is a server side rule, so every test here calls the API directly — no UI involved.
/// </summary>
public class AuthorizationTests
{
    [Fact]
    public async Task AdminIsNotAnApprover()
    {
        await using var app = AppHarness.Start();
        var alice = app.Client(Demo.Alice);
        var erin = app.Client(Demo.Erin);

        var request = await alice.CreateOk("auth-1", Demo.Crm, "Production", "Read");

        var response = await erin.Approve(request.Id, request.Version);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("not_assigned_approver", await response.ReadCode());
    }

    [Fact]
    public async Task RequesterCannotApproveOwnRequest_EvenAsSystemOwner()
    {
        await using var app = AppHarness.Start();
        var carol = app.Client(Demo.Carol);
        var bob = app.Client(Demo.Bob);

        // Carol owns CRM and requests CRM access herself. Bob handles the manager step, then Carol
        // would be the assigned system owner of her own request.
        var ownRequest = new { clientRequestId = "self-1", applicationId = Demo.Crm, environment = "Production", accessLevel = "Read", justification = "I need production access for the CRM audit" };
        var created = await (await carol.PostAsJsonAsync("/api/requests", ownRequest)).ReadRequest();

        var afterManager = await (await bob.Approve(created.Id, created.Version)).ReadRequest();
        Assert.Equal("PendingSystemOwnerApproval", afterManager.Status);

        var response = await carol.Approve(created.Id, afterManager.Version);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("self_approval_forbidden", await response.ReadCode());
    }

    [Fact]
    public async Task SystemOwnerOfAnotherApplication_CannotApprove()
    {
        await using var app = AppHarness.Start();
        var alice = app.Client(Demo.Alice);
        var bob = app.Client(Demo.Bob);
        var dana = app.Client(Demo.Dana);

        var request = await alice.CreateOk("auth-2", Demo.Crm, "Production", "Read");
        var afterManager = await (await bob.Approve(request.Id, request.Version)).ReadRequest();

        var response = await dana.Approve(request.Id, afterManager.Version); // Dana owns Finance Portal only

        // Dana is not a participant of this request at all, so she learns nothing beyond "forbidden".
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("not_a_participant", await response.ReadCode());
    }

    [Fact]
    public async Task RequestDetail_IsOnlyVisibleToParticipants()
    {
        await using var app = AppHarness.Start();
        var alice = app.Client(Demo.Alice);
        var bob = app.Client(Demo.Bob);
        var carol = app.Client(Demo.Carol);
        var dana = app.Client(Demo.Dana);
        var erin = app.Client(Demo.Erin);

        var request = await alice.CreateOk("view-1", Demo.Crm, "NonProduction", "Read");

        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync($"/api/requests/{request.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync($"/api/requests/{request.Id}")).StatusCode);   // manager
        Assert.Equal(HttpStatusCode.OK, (await carol.GetAsync($"/api/requests/{request.Id}")).StatusCode); // system owner
        Assert.Equal(HttpStatusCode.OK, (await erin.GetAsync($"/api/requests/{request.Id}")).StatusCode);  // admin/auditor

        var outsider = await dana.GetAsync($"/api/requests/{request.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, outsider.StatusCode);
        Assert.Equal("not_a_participant", await outsider.ReadCode());
    }

    [Fact]
    public async Task OnlyAdminCanListEveryRequest()
    {
        await using var app = AppHarness.Start();
        var alice = app.Client(Demo.Alice);
        var erin = app.Client(Demo.Erin);

        await alice.CreateOk("list-1");

        var forbidden = await alice.GetAsync("/api/requests/all");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal("admin_only", await forbidden.ReadCode());

        var allowed = await erin.GetAsync("/api/requests/all");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Single(await allowed.ReadRequestList());
    }

    [Fact]
    public async Task MyRequests_OnlyReturnsOwnRequests()
    {
        await using var app = AppHarness.Start();
        var alice = app.Client(Demo.Alice);
        var carol = app.Client(Demo.Carol);

        var carolRequest = new { clientRequestId = "mine-1", applicationId = Demo.Crm, environment = "NonProduction", accessLevel = "Read", justification = "Carol needs CRM read for support rota" };
        await carol.PostAsJsonAsync("/api/requests", carolRequest);
        await alice.CreateOk("mine-2");

        var aliceRequests = await (await alice.GetAsync("/api/requests")).ReadRequestList();

        Assert.Single(aliceRequests);
        Assert.Equal(Demo.Alice, aliceRequests[0].Requester.Email);
    }

    [Fact]
    public async Task ManagerLosesTheApprovalRightAfterTheirStepIsDone()
    {
        await using var app = AppHarness.Start();
        var alice = app.Client(Demo.Alice);
        var bob = app.Client(Demo.Bob);

        var request = await alice.CreateOk("hand-1", Demo.Crm, "Production", "Read");
        var afterManager = await (await bob.Approve(request.Id, request.Version)).ReadRequest();

        var again = await bob.Approve(request.Id, afterManager.Version);

        Assert.Equal(HttpStatusCode.Forbidden, again.StatusCode);
        Assert.Equal("not_assigned_approver", await again.ReadCode());
    }

    [Fact]
    public async Task RequesterWithoutManager_CannotSubmitAnUnapprovableRequest()
    {
        await using var app = AppHarness.Start();
        var bob = app.Client(Demo.Bob);

        var response = await bob.Create("bob-1");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("manager_not_configured", await response.ReadCode());
    }

    public async Task MissingOrUnknownIdentity_IsUnauthorized()
    {
        await using var app = AppHarness.Start();

        var anonymous = app.Factory.CreateClient();
        var noHeader = await anonymous.GetAsync("/api/requests");
        Assert.Equal(HttpStatusCode.Unauthorized, noHeader.StatusCode);
        Assert.Equal("missing_identity", await noHeader.ReadCode());

        var imposter = app.Client("nobody@example.local");
        var unknown = await imposter.GetAsync("/api/requests");
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal("unknown_demo_user", await unknown.ReadCode());
    }

    [Fact]
    public async Task RequesterComesFromTheSessionNotFromThePayload()
    {
        await using var app = AppHarness.Start();
        var alice = app.Client(Demo.Alice);

        // A hostile client tries to file the request as Bob and pre-approve it by sending extra fields.
        var body = new
        {
            clientRequestId = "spoof-1",
            applicationId = Demo.Crm,
            environment = "NonProduction",
            accessLevel = "Read",
            justification = "Trying to spoof the requester identity in the body",
            requesterId = 2,
            requesterEmail = Demo.Bob,
            status = "Approved",
            version = 99
        };

        var response = await alice.PostAsJsonAsync("/api/requests", body);
        var created = await response.ReadRequest();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(Demo.Alice, created.Requester.Email);
        Assert.Equal("PendingManagerApproval", created.Status);
        Assert.Equal(1, created.Version);
    }
}
