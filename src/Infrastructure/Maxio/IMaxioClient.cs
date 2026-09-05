using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Low-level Maxio Advanced Billing REST API operations needed by <see cref="MaxioSubscriptionService"/>.
/// Extracted purely so the orchestration/idempotency logic can be unit tested against a fake.
/// </summary>
internal interface IMaxioClient
{
    Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);

    Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken cancellationToken);

    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string familyHandle, CancellationToken cancellationToken);

    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken);

    /// <summary>
    /// Attempts to create a subscription. Returns null (instead of throwing) when Maxio reports
    /// the <paramref name="uniquenessToken"/> as a duplicate submission (HTTP 409) - the caller is
    /// expected to fall back to re-reading the customer's subscriptions in that case.
    /// </summary>
    Task<MaxioSubscription?> CreateSubscriptionAsync(int customerId, string productHandle, string uniquenessToken, CancellationToken cancellationToken);
}
