using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin, low-level client over the Maxio Billing API. Each method maps to a single Maxio endpoint and
/// speaks Maxio wire models. Higher-level orchestration and idempotency live in the billing service.
/// </summary>
internal interface IMaxioApiClient
{
    /// <summary>GET /customers/lookup.json?reference=... — returns null when no customer matches.</summary>
    Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// POST /customers.json. Throws <see cref="MaxioDuplicateReferenceException"/> when the reference is
    /// already taken (a concurrent create won the race).
    /// </summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken = default);

    /// <summary>GET /product_families/handle:{familyHandle}/products.json — plans in the product family.</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string familyHandle, CancellationToken cancellationToken = default);

    /// <summary>GET /customers/{customerId}/subscriptions.json.</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// POST /subscriptions.json. Throws <see cref="MaxioDuplicateSubmissionException"/> when the
    /// uniqueness token collides with a very recent request (a concurrent create won the race).
    /// </summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken = default);
}
