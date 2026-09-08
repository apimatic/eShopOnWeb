using System;

namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

/// <summary>
/// A subscription owned by an eShopOnWeb user, as recorded by the billing system of record.
/// </summary>
public record SubscriptionDetails
{
    public long SubscriptionId { get; init; }
    public string State { get; init; } = string.Empty;
    public long CustomerId { get; init; }
    public long? ProductId { get; init; }
    public string? ProductHandle { get; init; }
    public string? ProductName { get; init; }
    public long ProductPriceInCents { get; init; }
    public decimal Price => Math.Round(ProductPriceInCents / 100m, 2);
    public long? BalanceInCents { get; init; }
    public string? Currency { get; init; }
    public int? Interval { get; init; }
    public string? IntervalUnit { get; init; }
    public string? PaymentCollectionMethod { get; init; }
    public DateTimeOffset? ActivatedAt { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    public bool IsCurrent =>
        !SubscriptionStatus.IsEndedState(State);
}
