using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Orchestrates the subscription capability against Maxio Advanced Billing,
/// which is the system of record for customers and subscriptions.
///
/// Idempotency model:
///  * The eShopOnWeb user is mapped to a Maxio customer through the unique
///    customer "reference" value. Customer creation is guarded by an in-process
///    per-reference semaphore and by Maxio's own unique-reference constraint,
///    so a double click never creates two customers.
///  * Subscription creation is guarded by the same per-reference semaphore plus
///    an existing-active-subscription check, so a double click never creates two
///    subscriptions to the same plan.
/// </summary>
public interface IMaxioBillingService
{
    /// <summary>Resolves the configured product family and returns its non-archived plans (products).</summary>
    Task<IReadOnlyList<MaxioProduct>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for <paramref name="customerReference"/> and subscribes it to
    /// <paramref name="planHandle"/>. Returns the (possibly pre-existing) resulting subscription.
    /// </summary>
    Task<MaxioSubscription> SubscribeAsync(
        string customerReference,
        MaxioCustomerProfile profile,
        string planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the subscriptions the given customer reference holds in the configured product family.</summary>
    Task<IReadOnlyList<MaxioSubscription>> GetSubscriptionsForCustomerAsync(
        string customerReference,
        CancellationToken cancellationToken = default);
}
