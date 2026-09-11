using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioClient
{
    Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(string? productFamilyHandle = null);
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference);
    Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference);
    Task<MaxioCustomer> GetOrCreateCustomerAsync(string firstName, string lastName, string email, string reference);
    Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, int customerId);
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId);
}
