using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioService
{
    Task<CustomerResponse?> FindCustomerByReferenceAsync(string reference);
    Task<CustomerResponse> CreateCustomerAsync(string reference, string email, string firstName, string lastName);
    Task<List<ProductResponse>> ListPlansAsync(string familyHandle);
    Task<SubscriptionResponse> CreateSubscriptionAsync(string productHandle, string customerReference);
    Task<List<SubscriptionResponse>> ListSubscriptionsForCustomerAsync(int customerId);
}
