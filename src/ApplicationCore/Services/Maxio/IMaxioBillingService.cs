using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Services.Maxio;

public interface IMaxioBillingService
{
    Task<IReadOnlyList<MaxioPlanDto>> GetPlansAsync();
    Task<MaxioSubscriptionDto?> SubscribeAsync(string customerReference, string productHandle);
    Task<IReadOnlyList<MaxioSubscriptionDto>> GetSubscriptionsAsync(string customerReference);
}
