using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// Client abstraction over the Maxio Advanced Billing API, scoped to the operations needed
/// for the eShopOnWeb subscribe flow (plans, customers, subscriptions).
/// </summary>
public interface IMaxioClient
{
    /// <summary>Lists the subscribable plans (products) in the configured product family.</summary>
    Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>Looks up an existing customer by its eShopOnWeb-supplied reference. Null if none exists.</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Idempotently ensures a Maxio customer exists for the given reference (Maxio enforces
    /// reference uniqueness server-side, so concurrent calls converge on a single customer).
    /// </summary>
    Task<MaxioCustomer> EnsureCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken = default);

    /// <summary>Lists all subscriptions belonging to a Maxio customer.</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a subscription for the given customer to the given plan. Uses a uniqueness
    /// token derived from (customerId, planHandle) so a retried/duplicated request cannot
    /// create two subscriptions; on a duplicate-submission conflict the existing subscription
    /// is looked up and returned instead of failing.
    /// </summary>
    Task<MaxioSubscription> SubscribeAsync(long customerId, string planHandle, CancellationToken cancellationToken = default);
}
