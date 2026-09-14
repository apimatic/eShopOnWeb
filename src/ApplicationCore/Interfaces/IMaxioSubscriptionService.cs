using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Fronts Maxio Advanced Billing - the system of record for recurring-subscription billing.
/// Implementations must make <see cref="SubscribeAsync"/> safe to call twice for the same
/// customer/plan (e.g. a double-click) without creating duplicate Maxio customers or subscriptions.
/// </summary>
public interface IMaxioSubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken ct = default);

    Task<CustomerSubscription> SubscribeAsync(SubscribingCustomer customer, string planHandle, CancellationToken ct = default);

    Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsForUserAsync(string userId, CancellationToken ct = default);
}
