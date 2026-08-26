using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Gateway to Maxio Advanced Billing, the billing system of record for subscriptions.
/// </summary>
public interface IMaxioBillingService
{
    /// <summary>
    /// Lists the subscribable plans in the configured product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlanDto>> ListSubscriptionPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all subscriptions belonging to the given eShopOnWeb user.
    /// Returns an empty list when the user has no Maxio customer record yet.
    /// </summary>
    Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsForUserAsync(string userName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls the given eShopOnWeb user in a plan, ensuring a Maxio customer exists first.
    /// Idempotent: repeated calls for the same user and plan return the existing subscription
    /// instead of creating duplicates.
    /// </summary>
    /// <exception cref="SubscriptionPlanNotFoundException">The plan handle is not offered.</exception>
    Task<SubscribeResult> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a subscribe attempt. <paramref name="Created"/> is false when an existing
/// subscription was returned because the user was already subscribed to the plan.
/// </summary>
public record SubscribeResult(SubscriptionDto Subscription, bool Created);
