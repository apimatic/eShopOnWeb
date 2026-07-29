using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription capability backed by Maxio Advanced Billing as the system of
/// record. Implementations are responsible for talking to Maxio and for keeping the
/// operations idempotent (a Maxio customer is created at most once per eShopOnWeb user,
/// and a shopper is enrolled at most once in a given live plan).
/// </summary>
public interface IMaxioSubscriptionService
{
    /// <summary>
    /// Lists the plans a shopper may subscribe to (the products in the configured Maxio
    /// product family).
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls the shopper in the plan identified by <paramref name="planHandle"/>. Ensures a
    /// Maxio customer exists for the shopper (idempotent), and does not create a second
    /// subscription if the shopper already has a live subscription to that plan.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(
        SubscriberIdentity subscriber,
        string planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the shopper's subscriptions as reported by Maxio. Empty when the shopper has
    /// no Maxio customer yet.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(
        SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default);
}
