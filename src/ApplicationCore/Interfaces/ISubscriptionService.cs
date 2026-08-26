using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by the billing system of record (Maxio).
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Lists the subscription plans available in the configured product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlanDto>> ListSubscriptionPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes a user to a plan, ensuring the billing customer exists first.
    /// Idempotent: repeating the call for the same user and plan returns the
    /// existing subscription instead of creating a duplicate.
    /// </summary>
    /// <param name="userName">The authenticated user's identity (used as the billing customer reference).</param>
    /// <param name="productHandle">The handle of the plan to subscribe to.</param>
    Task<CustomerSubscriptionDto> SubscribeAsync(string userName, string productHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the user's subscriptions. Returns an empty list when the user has
    /// no billing customer record yet.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscriptionDto>> ListSubscriptionsForUserAsync(string userName, CancellationToken cancellationToken = default);
}
