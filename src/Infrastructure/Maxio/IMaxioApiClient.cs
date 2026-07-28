using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Low-level, typed client for the subset of the Maxio Advanced Billing API used by this
/// integration. Each method maps to exactly one operation in the OpenAPI spec. Errors surface
/// as <see cref="MaxioApiException"/>.
/// </summary>
internal interface IMaxioApiClient
{
    /// <summary>GET /products.json — all products belonging to the site.</summary>
    Task<IReadOnlyList<MaxioProductDto>> ListProductsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// GET /customers/lookup.json?reference=... — the customer for a given app reference, or
    /// <c>null</c> when none exists.
    /// </summary>
    Task<MaxioCustomerDto?> LookupCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>POST /customers.json — create a new customer.</summary>
    Task<MaxioCustomerDto> CreateCustomerAsync(CreateCustomerDto customer, CancellationToken cancellationToken = default);

    /// <summary>POST /subscriptions.json — enroll a customer in a product.</summary>
    Task<MaxioSubscriptionDto> CreateSubscriptionAsync(CreateSubscriptionDto subscription, CancellationToken cancellationToken = default);

    /// <summary>GET /customers/{customer_id}/subscriptions.json — a customer's subscriptions.</summary>
    Task<IReadOnlyList<MaxioSubscriptionDto>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>GET /site.json — the site's configured (default) currency code.</summary>
    Task<string?> GetSiteCurrencyAsync(CancellationToken cancellationToken = default);
}
