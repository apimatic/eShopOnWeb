using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Services;

public interface IMaxioService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync();
    Task<SubscriptionResult> CreateSubscriptionAsync(string email, string firstName, string lastName, string productHandle);
    Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsByEmailAsync(string email);
}
