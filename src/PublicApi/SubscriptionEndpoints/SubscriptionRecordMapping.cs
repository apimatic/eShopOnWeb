using Microsoft.eShopWeb.Infrastructure.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Maps infrastructure-layer subscription records to PublicApi response DTOs.</summary>
internal static class SubscriptionRecordMapping
{
    public static SubscriptionDto ToDto(SubscriptionRecord record)
    {
        return new SubscriptionDto
        {
            SubscriptionId = record.SubscriptionId,
            PlanHandle = record.PlanHandle ?? string.Empty,
            PlanName = record.PlanName ?? string.Empty,
            PriceInCents = record.PriceInCents,
            Price = record.PriceInCents.HasValue ? record.PriceInCents.Value / 100m : null,
            Currency = record.Currency ?? string.Empty,
            State = record.State,
            NextBillingDate = record.NextBillingDate
        };
    }
}
