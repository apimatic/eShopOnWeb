using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing capability backed by Maxio Advanced Billing,
/// the billing system of record.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the plans available for subscription.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls a shopper in a plan. Idempotent: the Maxio customer is looked up
    /// (or created) by <paramref name="customerReference"/>, and if an active
    /// subscription to the same plan already exists it is returned instead of
    /// creating a duplicate.
    /// </summary>
    Task<SubscriptionDetails> SubscribeAsync(string customerReference, string email, string planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the subscriptions belonging to a shopper.</summary>
    Task<IReadOnlyList<SubscriptionDetails>> ListSubscriptionsAsync(string customerReference,
        CancellationToken cancellationToken = default);
}
