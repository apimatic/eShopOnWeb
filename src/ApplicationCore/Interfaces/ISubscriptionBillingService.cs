using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by an external billing system of record.
/// </summary>
/// <remarks>
/// Every method raises <see cref="Exceptions.BillingException"/> — and only that — when the billing system
/// cannot be reached, rejects the request, or answers unreadably.
/// </remarks>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the plans shoppers may subscribe to.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls a shopper in a plan. Idempotent: repeated calls for the same shopper and plan return the
    /// existing subscription with <see cref="SubscribeResult.AlreadySubscribed"/> set, and never create a
    /// second billing customer or a second subscription.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(BillingCustomerIdentity identity, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists a shopper's subscriptions. Returns an empty list for a shopper who has never subscribed.</summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(BillingCustomerIdentity identity, bool includeInactive = false, CancellationToken cancellationToken = default);
}
