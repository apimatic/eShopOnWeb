using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int Id { get; init; }
    public string State { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public DateTimeOffset CurrentPeriodEndsAt { get; init; }
    public DateTimeOffset NextBillingAt { get; init; }
}
