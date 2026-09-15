using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed class BillingSubscription
{
    public int Id { get; init; }
    public string State { get; init; } = string.Empty;
    public string ProductHandle { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public long PriceInCents { get; init; }
    public decimal Price { get; init; }
    public DateTimeOffset? NextBillingDate { get; init; }
    public string? Reference { get; init; }
}
