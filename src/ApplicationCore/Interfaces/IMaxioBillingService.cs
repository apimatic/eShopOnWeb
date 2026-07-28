using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Application-facing abstraction over the Maxio Advanced Billing subscription capability.
/// Implementations translate between eShopOnWeb identities and Maxio customers/subscriptions.
/// </summary>
public interface IMaxioBillingService
{
    /// <summary>
    /// Lists the plans a shopper may subscribe to (the products in the configured product family).
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes the given eShopOnWeb user to a plan. Ensures a Maxio customer exists for the
    /// user (idempotent), and does not create a duplicate subscription if the user already has a
    /// live subscription to the same plan.
    /// </summary>
    /// <param name="userName">The eShopOnWeb user name (from the JWT), used to resolve the user.</param>
    /// <param name="planHandle">The stable plan handle to subscribe to.</param>
    /// <param name="pricePointHandle">Optional price point handle for the plan.</param>
    Task<SubscribeResult> SubscribeAsync(string userName, string planHandle, string? pricePointHandle = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the subscriptions belonging to the given eShopOnWeb user. Returns an empty list
    /// when the user has no Maxio customer record yet.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsForUserAsync(string userName,
        CancellationToken cancellationToken = default);
}
