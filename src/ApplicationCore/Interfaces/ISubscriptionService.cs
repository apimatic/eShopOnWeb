using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscription;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing capability, backed by Maxio Advanced Billing
/// as the billing system of record.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Lists the subscription plans available on the configured Maxio product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes a buyer to a plan. Idempotent: repeated calls with the same buyer and
    /// plan never create duplicate Maxio customers or subscriptions.
    /// </summary>
    /// <param name="buyerId">The buyer identity from the JWT (username).</param>
    /// <param name="planHandle">The Maxio product handle to subscribe to.</param>
    Task<SubscribeResult> SubscribeAsync(string buyerId, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all Maxio subscriptions that belong to the buyer, synced from Maxio.
    /// </summary>
    Task<IReadOnlyList<SubscriptionDetails>> GetSubscriptionsForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a Subscribe call. <see cref="WasCreated"/> is true when a new Maxio
/// subscription was created and false when an existing one was returned idempotently.
/// </summary>
public class SubscribeResult
{
    public SubscribeResult(SubscriptionDetails subscription, bool wasCreated)
    {
        Subscription = subscription;
        WasCreated = wasCreated;
    }

    public SubscriptionDetails Subscription { get; }
    public bool WasCreated { get; }
}
