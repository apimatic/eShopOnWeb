using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Facade over Maxio Advanced Billing for the recurring-subscription capability. Maxio types
/// never leak past this interface.
/// </summary>
public interface IMaxioSubscriptionService
{
    /// <summary>Lists the subscribe-able plans in the configured product family.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Ensures a Maxio customer exists for <paramref name="customer"/> and subscribes them to
    /// <paramref name="planHandle"/>. Idempotent per (customer, plan): a repeat call returns the
    /// existing subscription instead of creating another.
    /// </summary>
    Task<SubscriptionEnrollment> SubscribeAsync(MaxioCustomer customer, string planHandle, CancellationToken cancellationToken);

    /// <summary>Lists the subscriptions of the given customer (empty when they have no customer yet).</summary>
    Task<IReadOnlyList<SubscriptionInfo>> ListSubscriptionsAsync(MaxioCustomer customer, CancellationToken cancellationToken);
}
