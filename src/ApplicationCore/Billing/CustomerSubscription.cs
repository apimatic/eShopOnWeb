using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed class CustomerSubscription
{
    public int Id { get; init; }
    public required string State { get; init; }
    public required string ProductHandle { get; init; }
    public required string ProductName { get; init; }
    public decimal Price { get; init; }
    public long PriceInCents { get; init; }
    public DateTimeOffset? NextBillingDate { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public string? Reference { get; init; }
}
