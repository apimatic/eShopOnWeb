using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface ISubscriptionService
{
    /// <summary>Lists the subscription plans offered by the configured Maxio product family.</summary>
    Task<IReadOnlyList<MaxioProduct>> ListPlansAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Ensures a Maxio customer exists for the eShopOnWeb user (idempotent) and subscribes them to
    /// the given plan. If the user already has an ongoing subscription to that plan the existing
    /// subscription is returned instead of creating a duplicate.
    /// </summary>
    Task<SubscriptionEnrollmentResult> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken);

    /// <summary>Lists the Maxio subscriptions of the given user (empty when they have no customer yet).</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListMySubscriptionsAsync(string userName, CancellationToken cancellationToken);
}

/// <summary>Outcome of an idempotent subscribe attempt.</summary>
public sealed class SubscriptionEnrollmentResult
{
    public SubscriptionEnrollmentResult(MaxioSubscription subscription, bool wasAlreadySubscribed)
    {
        Subscription = subscription;
        WasAlreadySubscribed = wasAlreadySubscribed;
    }

    public MaxioSubscription Subscription { get; }

    /// <summary>True when the user was already subscribed to the plan and no new subscription was created.</summary>
    public bool WasAlreadySubscribed { get; }
}
