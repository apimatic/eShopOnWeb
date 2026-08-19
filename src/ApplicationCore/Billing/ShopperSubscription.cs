using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public class ShopperSubscription
{
    public long Id { get; init; }
    public string State { get; init; } = string.Empty;
    public string ProductHandle { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public DateTimeOffset? NextBillingAt { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    public string? Reference { get; init; }
    public bool AlreadyExisted { get; init; }
}
