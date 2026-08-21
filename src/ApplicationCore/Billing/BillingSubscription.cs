using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed class BillingSubscription
{
    public int Id { get; init; }
    public string State { get; init; } = string.Empty;
    public string ProductHandle { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public long ProductPriceInCents { get; init; }
    public DateTimeOffset? NextBillingAt { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public string? Reference { get; init; }

    public decimal ProductPrice => ProductPriceInCents / 100m;
}
