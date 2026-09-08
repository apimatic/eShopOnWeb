using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Application service for the eShopOnWeb subscription capability backed by Maxio Advanced Billing.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the subscribable plans of the configured Maxio product family.</summary>
    Task<IReadOnlyList<MaxioProduct>> ListPlansAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Ensures the given eShopOnWeb user has an active Maxio subscription to <paramref name="planHandle"/>.
    /// Idempotent: if the user already has a non-terminal subscription to that plan the existing one is returned.
    /// </summary>
    Task<SubscribeResult> EnsureSubscriptionAsync(string userEmail, string planHandle, CancellationToken cancellationToken);

    /// <summary>Lists the Maxio subscriptions of the given eShopOnWeb user.</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsAsync(string userEmail, CancellationToken cancellationToken);
}

/// <summary>Outcome of <see cref="ISubscriptionBillingService.EnsureSubscriptionAsync"/>.</summary>
public sealed class SubscribeResult
{
    public SubscribeResult(MaxioSubscription subscription, bool created)
    {
        Subscription = subscription;
        Created = created;
    }

    /// <summary>The subscription that is now in effect for the user and plan.</summary>
    public MaxioSubscription Subscription { get; }

    /// <summary><c>true</c> when a new Maxio subscription was created; <c>false</c> when an existing one was reused.</summary>
    public bool Created { get; }
}
