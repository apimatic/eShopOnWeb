using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Client for the subset of the Maxio Advanced Billing API used by this application.
/// Implementations talk to Maxio directly (it is the billing system of record).
/// </summary>
public interface IMaxioAdvancedBillingClient
{
    /// <summary>
    /// Lists the plans (products) of the product family identified by <paramref name="familyHandle"/>.
    /// </summary>
    Task<IReadOnlyList<MaxioPlan>> GetPlansAsync(string familyHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the customer whose app-provided <paramref name="reference"/> matches, or null when none exists.
    /// </summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a customer with the given unique <paramref name="reference"/>. When Maxio rejects the
    /// create because the reference is already taken (a concurrent/duplicate request), the existing
    /// customer is returned instead - the operation is idempotent.
    /// </summary>
    Task<MaxioCustomer> EnsureCustomerAsync(string reference, string firstName, string lastName, string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the subscription whose app-provided <paramref name="reference"/> matches, or null when none exists.
    /// </summary>
    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls <paramref name="customerId"/> in the plan identified by <paramref name="planHandle"/>,
    /// tagging the new subscription with the unique <paramref name="reference"/>.
    /// When Maxio rejects the create because the reference is already taken, the existing subscription
    /// is returned instead - the operation is idempotent.
    /// </summary>
    Task<MaxioSubscription?> SubscribeAsync(long customerId, string planHandle, string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all subscriptions of a customer.
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> GetSubscriptionsForCustomerAsync(long customerId, CancellationToken cancellationToken = default);
}
