using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// Application-facing abstraction over Maxio Advanced Billing for recurring-subscription
/// billing. Maxio is the system of record for customers, plans and subscription state.
/// </summary>
public interface IMaxioSubscriptionService
{
    /// <summary>
    /// Lists the active, subscribable plans in the store's configured product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for <paramref name="customerReference"/> (idempotent),
    /// then enrolls it in <paramref name="planHandle"/>. If the customer already has a live
    /// subscription to that plan, returns it instead of creating a duplicate.
    /// </summary>
    Task<CustomerSubscription> SubscribeAsync(
        string customerReference,
        string email,
        string firstName,
        string lastName,
        string planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions belonging to the Maxio customer mapped to <paramref name="customerReference"/>.
    /// Returns an empty list if no Maxio customer exists yet for this user.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default);
}
