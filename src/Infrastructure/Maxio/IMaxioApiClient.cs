using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin, typed client for the subset of the Maxio Advanced Billing (Billing API)
/// endpoints used by eShopOnWeb. All HTTP mechanics (auth, base address, error
/// mapping) are handled here; orchestration lives in <see cref="MaxioSubscriptionService"/>.
/// </summary>
public interface IMaxioApiClient
{
    /// <summary>
    /// Lists the products (plans) in a product family. The family may be referenced
    /// by its handle (e.g. "eshop-subscribe"). Archived products are included so the
    /// caller can filter them out; use <see cref="MaxioProduct.ArchivedAt"/>.
    /// </summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsInFamilyAsync(string familyHandleOrId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a customer by the reference value assigned by eShopOnWeb, or null when
    /// no customer with that reference exists.
    /// </summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a customer. Throws <see cref="MaxioApiException"/> on failure; a 422 with
    /// a "reference" error means a customer with that reference already exists.
    /// </summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions that belong to a customer.
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a subscription for an existing customer and a product referenced by its
    /// handle. <paramref name="uniquenessToken"/> enables Billing API duplicate
    /// prevention: retries of the same logical request within 60 minutes are rejected
    /// with 409 instead of creating duplicates.
    /// </summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle, string uniquenessToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a single subscription by its Billing API id.
    /// </summary>
    Task<MaxioSubscription> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Attributes for creating a Billing API customer.
/// </summary>
public class MaxioCustomerInput
{
    public string Reference { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string? Organization { get; set; }
}