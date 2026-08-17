using System;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// A stable id for the lifetime of this process, mixed into PayPal-Request-Id values. It keeps request
/// ids idempotent within a run (so genuine retries de-duplicate) while ensuring a restart — which, with
/// the in-memory provider, resets order ids to 1, 2, 3… — never replays a previous run's cached PayPal
/// authorizations, captures, or refunds.
/// </summary>
public sealed class ProcessInstance
{
    public string Id { get; } = Guid.NewGuid().ToString("N").Substring(0, 12);
}
