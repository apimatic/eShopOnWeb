using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface IMaxioClient
{
    Task<MaxioProductFamily?> FindProductFamilyByHandleAsync(string handle, CancellationToken cancellationToken = default);

    Task<MaxioProduct?> GetProductByHandleAsync(string handle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MaxioProduct>> ListProductFamilyProductsAsync(long productFamilyId, CancellationToken cancellationToken = default);

    Task<MaxioCustomer?> GetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken cancellationToken = default);

    Task<MaxioSubscription?> GetSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, long customerId, string reference, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default);
}
