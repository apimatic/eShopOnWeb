using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioClient
{
    Task<IReadOnlyList<MaxioPlanDto>> ListPlansAsync(string productFamilyHandle);
    Task<MaxioCustomerDto?> FindCustomerByReferenceAsync(string reference);
    Task<MaxioCustomerDto> CreateCustomerAsync(MaxioCustomerCreateAttributes attributes);
    Task<MaxioSubscriptionDto> CreateSubscriptionAsync(string productHandle, string customerReference, MaxioCustomerCreateAttributes customerAttributes, bool customerAlreadyExists = false);
    Task<IReadOnlyList<MaxioSubscriptionDto>> ListCustomerSubscriptionsAsync(int customerId);
}
