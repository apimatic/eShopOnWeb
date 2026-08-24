using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionMapper
{
    public static SubscriptionDto ToDto(SubscriptionDetails details) => new()
    {
        Id = details.Id,
        State = details.State,
        ProductHandle = details.ProductHandle,
        ProductName = details.ProductName,
        PriceInCents = details.PriceInCents,
        Interval = details.Interval,
        IntervalUnit = details.IntervalUnit,
        ActivatedAt = details.ActivatedAt,
        NextBillingDate = details.NextBillingDate,
        CreatedAt = details.CreatedAt
    };
}
