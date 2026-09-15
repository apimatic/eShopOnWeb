using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Billing.Models;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Typed client for the Maxio Advanced Billing OpenAPI operations used by subscription billing.
/// </summary>
public interface IMaxioAdvancedBillingClient
{
    /// <summary>OpenAPI operationId: listProductsForProductFamily</summary>
    Task<IReadOnlyList<ProductDto>> ListProductsForProductFamilyAsync(
        string productFamilyIdOrHandle,
        int page,
        int perPage,
        CancellationToken cancellationToken);

    /// <summary>OpenAPI operationId: readCustomerByReference</summary>
    Task<CustomerDto?> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);

    /// <summary>OpenAPI operationId: createCustomer</summary>
    Task<CustomerDto> CreateCustomerAsync(CreateCustomerRequest request, CancellationToken cancellationToken);

    /// <summary>OpenAPI operationId: listCustomerSubscriptions</summary>
    Task<IReadOnlyList<SubscriptionDto>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken);

    /// <summary>OpenAPI operationId: findSubscription</summary>
    Task<SubscriptionDto?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken);

    /// <summary>OpenAPI operationId: createSubscription</summary>
    Task<SubscriptionDto> CreateSubscriptionAsync(CreateSubscriptionRequest request, CancellationToken cancellationToken);
}
