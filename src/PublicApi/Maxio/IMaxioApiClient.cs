using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioApiClient
{
    Task<List<PlanDto>> GetPlansAsync(string productFamilyHandle);
    Task<CustomerDto?> FindCustomerByReferenceAsync(string reference);
    Task<CustomerDto> CreateCustomerAsync(string reference, string email, string firstName, string lastName);
    Task<SubscriptionDto> CreateSubscriptionAsync(int customerId, string productHandle, string uniquenessToken);
    Task<List<SubscriptionDto>> GetCustomerSubscriptionsAsync(int customerId);
}
