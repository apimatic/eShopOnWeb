using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi;

public interface IMaxioSubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync();
    Task<SubscriptionResultDto> SubscribeAsync(string userEmail, string? userFirstName, string? userLastName, string productHandle);
    Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(string userEmail);
}
