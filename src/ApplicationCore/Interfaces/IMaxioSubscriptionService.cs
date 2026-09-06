using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMaxioSubscriptionService
{
    Task<List<MaxioProductDto>> GetAvailablePlansAsync();
    Task<List<MaxioSubscriptionDto>> GetUserSubscriptionsAsync(string userId);
    Task<MaxioSubscriptionDto> CreateSubscriptionAsync(string userId, string userEmail, string firstName, string lastName, string planHandle);
}
