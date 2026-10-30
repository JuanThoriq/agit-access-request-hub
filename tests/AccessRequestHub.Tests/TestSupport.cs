using System.Net.Http.Json;
using System.Text.Json;
using AccessRequestHub.Api.Contracts;
using AccessRequestHub.Api.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AccessRequestHub.Tests;

public static class Demo
{
    public const string Alice = "alice@example.local";   // requester, reports to Bob
    public const string Bob = "bob@example.local";       // manager
    public const string Carol = "carol@example.local";   // system owner: CRM
    public const string Dana = "dana@example.local";     // system owner: Finance Portal
    public const string Erin = "erin@example.local";     // admin/auditor

    public const int Crm = 1;
    public const int FinancePortal = 2;
}

/// <summary>
/// One isolated application instance per test: its own SQLite file, its own migrations and seed.
/// Tests exercise the real HTTP surface (including the X-Demo-User middleware), not the handlers
/// directly, because authorization and idempotency are only meaningful end to end.
/// </summary>
public sealed class AppHarness : IAsyncDisposable
{
    private readonly string _root;

    private AppHarness(string root, WebApplicationFactory<Program> factory)
    {
        _root = root;
        Factory = factory;
    }

    public WebApplicationFactory<Program> Factory { get; }

    public static AppHarness Start()
    {
        var root = Path.Combine(Path.GetTempPath(), "arh-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", $"Data Source={Path.Combine(root, "test.db")}");
            builder.UseSetting("Logging:LogLevel:Default", "Warning");
        });

        _ = factory.Services; // start the host so migrations and seed run before the first request
        return new AppHarness(root, factory);
    }

    public HttpClient Client(string email = Demo.Alice)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Demo-User", email);
        return client;
    }

    /// <summary>Runs <paramref name="action"/> against a fresh DbContext, for assertions that bypass the API.</summary>
    public async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // a lingering file handle on Windows must not fail an otherwise green test
        }
    }
}

public static class Api
{
    public static Task<HttpResponseMessage> Create(
        this HttpClient client,
        string clientRequestId,
        int applicationId = Demo.Crm,
        string environment = "NonProduction",
        string accessLevel = "Read",
        string justification = "Need access to do the quarterly reporting work") =>
        client.PostAsJsonAsync("/api/requests", new
        {
            clientRequestId,
            applicationId,
            environment,
            accessLevel,
            justification
        });

    public static Task<HttpResponseMessage> Approve(this HttpClient client, int requestId, int version) =>
        client.PostAsJsonAsync($"/api/requests/{requestId}/approve", new { version });

    public static Task<HttpResponseMessage> Reject(this HttpClient client, int requestId, int version, string? reason) =>
        client.PostAsJsonAsync($"/api/requests/{requestId}/reject", new { version, reason });

    public static async Task<AccessRequestDto> ReadRequest(this HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<AccessRequestDto>())!;

    public static async Task<List<AccessRequestDto>> ReadRequestList(this HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<List<AccessRequestDto>>())!;

    public static async Task<string> ReadCode(this HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() ?? "" : "";
    }

    public static async Task<JsonDocument> ReadJson(this HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    /// <summary>Creates a request and asserts it was accepted, so tests can focus on the step they care about.</summary>
    public static async Task<AccessRequestDto> CreateOk(this HttpClient client, string clientRequestId, int applicationId = Demo.Crm,
        string environment = "NonProduction", string accessLevel = "Read", string justification = "Need access to do the quarterly reporting work")
    {
        var response = await client.Create(clientRequestId, applicationId, environment, accessLevel, justification);
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        return await response.ReadRequest();
    }
}
