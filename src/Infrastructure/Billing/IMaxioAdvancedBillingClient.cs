using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Billing.MaxioModels;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Low-level Maxio Advanced Billing HTTP API (JSON + Basic auth).
/// Endpoints verified against Maxio Advanced Billing developer docs / SDK:
/// list products for family, create/lookup customer, create/lookup subscription,
/// list customer subscriptions.
/// </summary>
public interface IMaxioAdvancedBillingClient
{
    Task<IReadOnlyList<ProductDto>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken);

    Task<CustomerDto?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);

    Task<CustomerDto> CreateCustomerAsync(CreateCustomerBody customer, CancellationToken cancellationToken);

    Task<SubscriptionDto?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken);

    Task<SubscriptionDto> CreateSubscriptionAsync(CreateSubscriptionBody subscription, CancellationToken cancellationToken);

    Task<IReadOnlyList<SubscriptionDto>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken);
}
