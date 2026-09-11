using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Maxio.Models;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioHttpClient
{
    Task<List<MaxioProduct>> ListProductsForFamilyAsync(string familyHandle);
    Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference);
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomerRequest request);
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request);
    Task<List<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId);
}
