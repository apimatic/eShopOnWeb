using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public record SubscriptionInfo
{
    public int SubscriptionId { get; init; }
    public string State { get; init; } = string.Empty;
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    public string ProductHandle { get; init; } = string.Empty;
}
