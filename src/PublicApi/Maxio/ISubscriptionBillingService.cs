using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class SubscribeResult
{
    public required MaxioSubscription Subscription { get; set; }

    /// <summary>True when an existing live subscription was returned instead of creating a new one.</summary>
    public bool AlreadySubscribed { get; set; }
}

/// <summary>
/// Orchestrates the subscription billing flows on top of the Maxio API:
/// plan discovery, idempotent customer provisioning, and idempotent enrollment.
/// </summary>
public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<MaxioProduct>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the given eShopOnWeb user and subscribes them
    /// to the plan. Safe against duplicate submissions: an existing live subscription to
    /// the same plan is returned instead of creating a second one.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(string userId, string username, string email, string productHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsForUserAsync(string userId, CancellationToken cancellationToken = default);
}
