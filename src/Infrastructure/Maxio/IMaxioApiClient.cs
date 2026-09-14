using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin, spec-faithful transport over the Maxio Advanced Billing REST API. Each method maps
/// to exactly one operation in the Maxio OpenAPI specification. Orchestration and idempotency
/// live in <see cref="MaxioBillingService"/>, not here.
/// </summary>
public interface IMaxioApiClient
{
    /// <summary>GET /product_families/handle:{familyHandle}/products.json (listProductsForProductFamily).</summary>
    Task<IReadOnlyList<ProductDto>> ListProductsForFamilyAsync(string familyHandle, CancellationToken cancellationToken = default);

    /// <summary>GET /customers/lookup.json?reference=... (readCustomerByReference). Returns null on 404.</summary>
    Task<CustomerDto?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>POST /customers.json (createCustomer).</summary>
    Task<CustomerDto> CreateCustomerAsync(CreateCustomerDto customer, CancellationToken cancellationToken = default);

    /// <summary>GET /customers/{customer_id}/subscriptions.json (listCustomerSubscriptions).</summary>
    Task<IReadOnlyList<SubscriptionDto>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default);

    /// <summary>POST /subscriptions.json (createSubscription).</summary>
    Task<SubscriptionDto> CreateSubscriptionAsync(CreateSubscriptionDto subscription, CancellationToken cancellationToken = default);
}
