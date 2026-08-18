using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Typed HTTP client for the Maxio Advanced Billing operations used by eShopOnWeb.
/// Paths, query parameters, request/response shapes, and Basic auth match the OpenAPI spec.
/// </summary>
public interface IMaxioApiClient
{
    Task<IReadOnlyList<Product>> ListProductsForProductFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default);

    Task<Customer?> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Customer>> ListCustomersAsync(string query, CancellationToken cancellationToken = default);

    Task<Customer> CreateCustomerAsync(CreateCustomer customer, CancellationToken cancellationToken = default);

    Task<Subscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<Subscription> CreateSubscriptionAsync(CreateSubscription subscription, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);
}
