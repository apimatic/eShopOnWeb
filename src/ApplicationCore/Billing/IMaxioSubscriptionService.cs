using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// Application-facing contract for subscription billing, backed by Maxio Advanced Billing
/// (the system of record). Implementations must make <see cref="SubscribeAsync"/> safe to
/// call more than once for the same user/plan without creating duplicate customers or
/// subscriptions in Maxio.
/// </summary>
public interface IMaxioSubscriptionService
{
    /// <summary>
    /// Lists the plans a shopper can subscribe to, from the configured Maxio product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for <paramref name="username"/> and enrolls them in the
    /// given plan. If the user already has a live subscription to that plan, that subscription
    /// is returned instead of creating a new one.
    /// </summary>
    /// <param name="username">The eShopOnWeb user's identity (their username, which doubles as their email).</param>
    /// <param name="planHandle">The Maxio product handle of the plan to subscribe to.</param>
    Task<SubscribeResult> SubscribeAsync(string username, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the given user's subscriptions. Returns an empty list if the user has never subscribed
    /// (i.e. no Maxio customer exists for them yet).
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsForUserAsync(string username, CancellationToken cancellationToken = default);
}
