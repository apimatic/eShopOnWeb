using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class CustomerSubscription
{
    public int Id { get; init; }
    public string State { get; init; } = string.Empty;
    public string ProductHandle { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public DateTimeOffset? NextBillingDate { get; init; }
    public string? Reference { get; init; }
}
