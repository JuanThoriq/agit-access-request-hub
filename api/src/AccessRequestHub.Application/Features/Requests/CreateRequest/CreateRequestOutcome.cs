using AccessRequestHub.Application.Features.Requests.Common;

namespace AccessRequestHub.Application.Features.Requests.CreateRequest;

/// <summary>
/// <c>Created</c> is false for an idempotent replay, so the API can answer 200 instead of 201:
/// the client is told the same row came back, not that a second one was written.
/// </summary>
public sealed record CreateRequestOutcome(bool Created, AccessRequestResponse Request);
