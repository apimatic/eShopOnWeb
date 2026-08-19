using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Billing;

/// <summary>
/// A Maxio subscription belonging to an eShopOnWeb shopper.
/// </summary>
public sealed class CustomerSubscription
{
    public int Id { get; init; }
    public string State { get; init; } = string.Empty;
    public string? Reference { get; init; }
    public int? CustomerId { get; init; }
    public string ProductHandle { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public long PriceInCents { get; init; }
    public decimal Price => PriceInCents / 100m;
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    public DateTimeOffset? NextBillingDate { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? ActivatedAt { get; init; }

    public bool IsLive => SubscriptionStates.IsLive(State);
}
