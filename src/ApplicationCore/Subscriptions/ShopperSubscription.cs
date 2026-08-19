using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's subscription as recorded by Maxio Advanced Billing.
/// </summary>
public class ShopperSubscription
{
    public int Id { get; init; }
    public string? Reference { get; init; }
    public string State { get; init; } = string.Empty;
    public string ProductHandle { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public DateTimeOffset? NextBillingDate { get; init; }
    public int? CustomerId { get; init; }
}
