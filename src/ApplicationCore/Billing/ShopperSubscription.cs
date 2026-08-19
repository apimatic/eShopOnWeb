using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public class ShopperSubscription
{
    public int Id { get; init; }
    public string? Reference { get; init; }
    public string ProductHandle { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public string Currency { get; init; } = "USD";
    public string State { get; init; } = string.Empty;
    public DateTimeOffset? NextBillingAt { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    public bool Created { get; init; }
}
