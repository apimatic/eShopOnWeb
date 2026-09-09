using Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps subscription service models to API DTOs.
/// </summary>
public static class SubscriptionMapper
{
    public static SubscriptionDto Map(UserSubscriptionInfo info)
    {
        return new SubscriptionDto
        {
            MaxioSubscriptionId = info.MaxioSubscriptionId,
            MaxioCustomerId = info.MaxioCustomerId,
            PlanHandle = info.PlanHandle,
            PlanName = info.PlanName,
            State = info.State,
            Price = info.Price,
            Currency = info.Currency,
            NextBillingDate = info.NextBillingDate,
            CreatedAt = info.CreatedAt
        };
    }
}
