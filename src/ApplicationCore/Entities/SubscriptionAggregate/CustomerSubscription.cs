using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public sealed class CustomerSubscription
{
    public int Id { get; init; }
    public required string ProductHandle { get; init; }
    public required string ProductName { get; init; }
    public decimal Price { get; init; }
    public required string State { get; init; }
    public DateTimeOffset? NextBillingDate { get; init; }
}
