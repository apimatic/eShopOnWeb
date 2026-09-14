using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Gateway to Maxio Advanced Billing for the subscription-billing capability.
/// The Maxio customer for an eShopOnWeb user is keyed by the user's e-mail address
/// (used as the Maxio customer <c>reference</c>), which keeps every operation
/// idempotent without a local mapping table.
/// </summary>
public interface IMaxioSubscriptionService
{
    /// <summary>Lists the subscribable plans (products) in the configured Maxio product family.</summary>
    Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Ensures a Maxio customer exists for the given reference/e-mail and returns it.
    /// Safe to call concurrently - a double call never creates two customers.
    /// </summary>
    Task<MaxioCustomer> GetOrCreateCustomerAsync(string reference, string email, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the subscriptions owned by the customer identified by <paramref name="reference"/>.
    /// Returns an empty list when no Maxio customer exists for the reference yet.
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsAsync(string reference, CancellationToken cancellationToken);

    /// <summary>
    /// Subscribes the customer identified by <paramref name="reference"/> to the plan identified by
    /// <paramref name="planHandle"/>. Idempotent: if the customer already has a current subscription to the
    /// same plan it is returned with <see cref="SubscriptionEnrollmentResult.Created"/> set to false,
    /// otherwise a new subscription is created.
    /// </summary>
    Task<SubscriptionEnrollmentResult> SubscribeAsync(string reference, string email, string planHandle, CancellationToken cancellationToken);
}
