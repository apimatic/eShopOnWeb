using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// A minimal, testable transport over the Maxio Advanced Billing REST API. Implementations
/// own HTTP, authentication, retries and mapping to domain models; orchestration and
/// idempotency live in <see cref="MaxioSubscriptionService"/>.
/// </summary>
public interface IMaxioClient
{
    /// <summary>Lists the (non-archived) products of a product family as subscription plans.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(string productFamilyHandle, CancellationToken cancellationToken = default);

    /// <summary>Returns the billing customer with the given external reference, or null if none exists.</summary>
    Task<MaxioCustomerReference?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>Creates a billing customer keyed by the given external reference.</summary>
    Task<MaxioCustomerReference> CreateCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken = default);

    /// <summary>Lists all subscriptions belonging to a billing customer.</summary>
    Task<IReadOnlyList<CustomerSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a subscription for the customer on the given plan.
    /// <paramref name="uniquenessToken"/> lets Maxio reject an accidental duplicate submission.
    /// </summary>
    Task<CustomerSubscription> CreateSubscriptionAsync(long customerId, string planHandle, string uniquenessToken, CancellationToken cancellationToken = default);
}

/// <summary>A billing customer identity as known to Maxio.</summary>
public sealed record MaxioCustomerReference(long Id, string? Reference, string? Email);
