using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioService
{
    Task<IReadOnlyList<Models.MaxioProduct>> ListProductsAsync();
    Task<Models.MaxioCustomer?> FindCustomerByReferenceAsync(string reference);
    Task<Models.MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference);
    Task<Models.MaxioCustomer> FindOrCreateCustomerAsync(string firstName, string lastName, string email, string reference);
    Task<IReadOnlyList<Models.MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId);
    Task<Models.MaxioSubscription> CreateSubscriptionAsync(string productHandle, int customerId);
}
