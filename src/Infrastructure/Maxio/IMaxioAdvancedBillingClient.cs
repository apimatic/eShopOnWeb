using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public interface IMaxioAdvancedBillingClient
{
    Task<IReadOnlyList<ProductPayload>> ListProductsForProductFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken = default);

    Task<ProductPayload?> ReadProductByHandleAsync(
        string productHandle,
        CancellationToken cancellationToken = default);

    Task<CustomerPayload?> ReadCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default);

    Task<CustomerPayload> CreateCustomerAsync(
        CreateCustomerPayload customer,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubscriptionPayload>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default);

    Task<SubscriptionPayload> CreateSubscriptionAsync(
        CreateSubscriptionPayload subscription,
        CancellationToken cancellationToken = default);
}
