using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Billing;

public class ShopperSubscription
{
    public int Id { get; init; }
    public string ProductHandle { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public string State { get; init; } = string.Empty;
    public DateTimeOffset? NextBillingAt { get; init; }
}
