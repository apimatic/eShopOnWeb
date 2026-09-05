using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by an external billing system of record (Maxio Advanced Billing).
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>
    /// Lists the plans available for subscription.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a billing customer exists for <paramref name="customerReference"/> and enrolls it in the plan
    /// identified by <paramref name="planHandle"/>. Safe to call more than once for the same customer/plan pair:
    /// an existing enrollment is returned rather than duplicated.
    /// </summary>
    Task<CustomerSubscription> SubscribeAsync(string customerReference, string customerEmail, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions belonging to the customer identified by <paramref name="customerReference"/>.
    /// Returns an empty list if no billing customer exists yet for that reference.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default);
}
