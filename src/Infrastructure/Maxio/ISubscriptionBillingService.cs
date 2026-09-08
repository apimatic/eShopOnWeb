using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// The eShopOnWeb subscription-billing capability. Wraps Maxio Advanced Billing as the system
/// of record and keeps the local eShopOnWeb ↔ Maxio customer mapping up to date so that every
/// operation is idempotent (repeated calls for the same user + plan never create duplicate
/// customers or subscriptions).
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the currently subscribable plans from the configured Maxio product family.</summary>
    Task<IReadOnlyList<MaxioProduct>> ListAvailablePlansAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Subscribes the given eShopOnWeb user to the given plan. Ensures (idempotently) that a
    /// Maxio customer exists for the user, reuses an existing live subscription for the same
    /// plan, and otherwise creates a new one.
    /// </summary>
    Task<SubscriptionEnrollmentResult> SubscribeAsync(
        string applicationUserId,
        string applicationUserEmail,
        string productHandle,
        string? firstName,
        string? lastName,
        CancellationToken cancellationToken);

    /// <summary>Lists the caller's subscriptions, or an empty list when they have none.</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListMySubscriptionsAsync(
        string applicationUserId,
        string applicationUserEmail,
        CancellationToken cancellationToken);
}

/// <summary>The outcome of an idempotent subscribe operation.</summary>
public sealed class SubscriptionEnrollmentResult
{
    public SubscriptionEnrollmentResult(MaxioSubscription subscription, bool wasCreated)
    {
        Subscription = subscription;
        WasCreated = wasCreated;
    }

    /// <summary>The subscription, either pre-existing or newly created, in Maxio.</summary>
    public MaxioSubscription Subscription { get; }

    /// <summary>True when the subscription was created by this call; false when it already existed.</summary>
    public bool WasCreated { get; }
}
