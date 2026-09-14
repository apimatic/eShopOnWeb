using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription capability, backed by an external billing system of record.
/// Implementations are responsible for keeping enrollment idempotent per shopper and plan.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>
    /// Lists the plans a shopper can subscribe to.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls the shopper onto a plan, creating the billing customer if needed.
    /// Repeated calls for the same shopper and plan return the existing subscription.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every subscription held by the shopper, newest first. Returns an empty list when the
    /// shopper has never been enrolled.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(string userName, CancellationToken cancellationToken = default);
}
