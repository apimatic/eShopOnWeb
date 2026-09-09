using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by Maxio Advanced Billing as the system of record.
/// This is additive to the existing one-time commerce flow and does not replace it.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>
    /// Lists the plans a shopper can subscribe to (the active products of the configured
    /// Maxio product family).
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls the given eShopOnWeb user in the plan identified by <paramref name="planHandle"/>.
    /// Idempotent: ensures a single Maxio customer exists for the user (keyed on
    /// <see cref="BillingCustomer.Reference"/>) and, if the user already has a live subscription
    /// to that plan, returns it instead of creating a duplicate.
    /// </summary>
    Task<CustomerSubscription> SubscribeAsync(BillingCustomer customer, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions belonging to the given user reference. Returns an empty list
    /// when no Maxio customer exists yet for the user.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsForUserAsync(string customerReference, CancellationToken cancellationToken = default);
}
