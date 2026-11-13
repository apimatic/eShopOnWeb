using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// Recurring-subscription billing. The billing provider is the system of record for plans and subscriptions.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>The plans the shop offers, from the configured product family.</summary>
    Task<PlanCatalog> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes the user to the plan with the given handle, creating their billing customer first if needed.
    /// Idempotent per (user, plan): repeating the call never creates a second customer or subscription.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(BillingCustomerProfile customer, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>The user's subscriptions; empty when the user has no billing customer yet.</summary>
    Task<IReadOnlyList<SubscriptionDetails>> GetSubscriptionsAsync(string userName, CancellationToken cancellationToken = default);
}
