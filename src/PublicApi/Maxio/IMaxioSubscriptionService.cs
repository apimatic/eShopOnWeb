using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioSubscriptionService
{
    Task<List<Models.MaxioProduct>> GetAvailablePlansAsync();
    Task<Models.MaxioSubscription> SubscribeAsync(string userReference, string userFirstName, string userLastName, string userEmail, string productHandle);
    Task<List<Models.MaxioSubscription>> GetMySubscriptionsAsync(string userReference);
}
