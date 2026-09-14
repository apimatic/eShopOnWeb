using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// Application service that fronts the recurring-subscription capability backed by the
/// billing provider. The billing provider is the system of record for customers and
/// subscriptions; the local eShop user is tied to a provider customer through a stable,
/// deterministic provider-side reference.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Lists the plans available in the configured product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures the eShop user has a billing customer and subscribes them to <see cref="SubscriptionSignup.PlanHandle"/>.
    /// The operation is idempotent: subscribing to a plan the user already holds returns the existing subscription.
    /// </summary>
    Task<SubscriptionResult> SubscribeAsync(SubscriptionSignup signup, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions the given eShop user holds on plans in the configured product family.
    /// </summary>
    Task<IReadOnlyList<Subscription>> ListSubscriptionsAsync(string userId, CancellationToken cancellationToken = default);
}
