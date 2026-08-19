using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public sealed class CustomerSubscription
{
    public int Id { get; init; }
    public string? Reference { get; init; }
    public string State { get; init; } = string.Empty;
    public string ProductHandle { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public int PriceInCents { get; init; }
    public decimal Price { get; init; }
    public string Currency { get; init; } = "USD";
    public DateTimeOffset? NextBillingDate { get; init; }
    public int? CustomerId { get; init; }
}
