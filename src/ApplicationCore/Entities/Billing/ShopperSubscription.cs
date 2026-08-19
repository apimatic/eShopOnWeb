using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Billing;

public sealed class ShopperSubscription
{
    public int Id { get; init; }
    public string State { get; init; } = string.Empty;
    public string? ProductHandle { get; init; }
    public string? ProductName { get; init; }
    public long ProductPriceInCents { get; init; }
    public DateTimeOffset? NextBillingDate { get; init; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public string? Reference { get; init; }
    public int? CustomerId { get; init; }
}
