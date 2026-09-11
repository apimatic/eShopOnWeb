using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi;

public interface IMaxioBillingService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync();
    Task<SubscriptionResult> SubscribeAsync(string userId, string email, string firstName, string lastName, string productHandle);
    Task<IReadOnlyList<SubscriptionResult>> GetMySubscriptionsAsync(string userId);
}
