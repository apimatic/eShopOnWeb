using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Outbound abstraction over the recurring-billing system of record (Maxio Advanced Billing).
/// The implementation lives in Infrastructure and translates provider failures into
/// <see cref="Microsoft.eShopWeb.ApplicationCore.Exceptions.BillingException"/>s, so callers see a
/// single, transport-agnostic failure type.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the subscription plans available in the configured product family.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls the shopper in the given plan. Ensures a Maxio customer exists for the shopper
    /// idempotently and will not create a second subscription when the shopper already has a live
    /// subscription to that plan, so a double-click never double-enrolls.
    /// </summary>
    Task<CustomerSubscription> SubscribeAsync(SubscriberIdentity subscriber, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists the shopper's subscriptions; empty when the shopper is not yet a Maxio customer.</summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default);
}
