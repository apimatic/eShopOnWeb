using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by Maxio Advanced Billing. Maxio is the system of
/// record for plans and subscriptions; this service never persists billing state locally.
/// </summary>
public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);

    Task<SubscriptionPlan?> GetPlanAsync(string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for <paramref name="customerReference"/> and enrolls them in
    /// <paramref name="planHandle"/>. Idempotent: a repeat call for the same customer/plan while an
    /// active enrollment already exists returns that enrollment instead of creating a duplicate.
    /// </summary>
    Task<(CustomerSubscription Subscription, bool Created)> SubscribeAsync(string customerReference, string customerEmail, string planHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsForCustomerAsync(string customerReference, CancellationToken cancellationToken = default);
}
