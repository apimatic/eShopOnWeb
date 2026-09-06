using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, as the rest of eShopOnWeb sees it. Implementations own all
/// contact with the billing provider and translate every failure into
/// <see cref="Exceptions.SubscriptionBillingException"/>.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>
    /// Lists the plans a shopper may subscribe to, from the configured product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes a shopper to a plan, creating the provider customer first if one does not exist.
    /// </summary>
    /// <remarks>
    /// Idempotent per (shopper, plan): a repeated call while a live subscription to the same plan
    /// exists returns that subscription with
    /// <see cref="SubscribeResult.AlreadySubscribed"/> set, and creates nothing.
    /// </remarks>
    /// <param name="identity">The shopper, resolved from the authenticated caller.</param>
    /// <param name="planHandle">
    /// Plan to subscribe to. When null or empty the configured default plan is used, and the call
    /// is rejected if no default is configured.
    /// </param>
    Task<SubscribeResult> SubscribeAsync(BillingCustomerIdentity identity,
        string? planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the shopper's subscriptions. Returns an empty list when the shopper has no provider
    /// customer record yet — that is "not subscribed", not an error.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(BillingCustomerIdentity identity,
        CancellationToken cancellationToken = default);
}
