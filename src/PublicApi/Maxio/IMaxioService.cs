using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Maxio.Models;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioService
{
    Task<List<Product>> ListPlansAsync();
    Task<Subscription> SubscribeAsync(string userReference, string productHandle, string email, string firstName, string lastName);
    Task<List<Subscription>> GetMySubscriptionsAsync(string userReference);
}
