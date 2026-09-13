using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioClient
{
    Task<IReadOnlyList<MaxioProduct>> ListPlansAsync(string productFamilyHandle);
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference);
    Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference);
    Task<MaxioCustomer> EnsureCustomerAsync(string firstName, string lastName, string email, string reference);
    Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, string customerReference);
    Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsByCustomerReferenceAsync(string customerReference);
}
