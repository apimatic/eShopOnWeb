using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A Maxio subscription as surfaced to the shopper.
/// </summary>
public class SubscriptionDto
{
    public long? Id { get; set; }

    public string? Reference { get; set; }

    public string State { get; set; } = string.Empty;

    public string ProductHandle { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public long? ProductPriceInCents { get; set; }

    public decimal? ProductPrice => ProductPriceInCents is null ? null : ProductPriceInCents / 100m;

    public string? Currency { get; set; }

    public int? Interval { get; set; }

    public string? IntervalUnit { get; set; }

    /// <summary>The end of the current billing period; i.e. the next billing date.</summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public long? CustomerId { get; set; }

    public string? CustomerEmail { get; set; }
}
