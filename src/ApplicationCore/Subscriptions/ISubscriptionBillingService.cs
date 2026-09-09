using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Recurring-subscription billing capability, backed by an external billing system of record.
/// This abstraction is provider-agnostic; the concrete implementation talks to Maxio Advanced Billing.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the plans a shopper can subscribe to.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls <paramref name="user"/> in the plan identified by <paramref name="planHandle"/>.
    /// Ensures a billing customer exists for the user and creates the subscription. The operation is
    /// idempotent: repeated calls (e.g. a double-click) will not create duplicate customers or duplicate
    /// live subscriptions for the same plan.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(BillingUser user, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists the subscriptions currently held by <paramref name="user"/> (empty if none).</summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsForUserAsync(BillingUser user, CancellationToken cancellationToken = default);
}
