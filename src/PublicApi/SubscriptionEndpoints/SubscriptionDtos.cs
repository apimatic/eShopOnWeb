using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A subscription plan offered for sale (a product in the configured Maxio product family).</summary>
public class SubscriptionPlanDto
{
    public long ProductId { get; set; }

    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>The recurring price in integer cents.</summary>
    public long PriceInCents { get; set; }

    /// <summary>The recurring price in dollars (PriceInCents / 100).</summary>
    public decimal Price { get; set; }

    /// <summary>The billing interval (e.g. 1) coupled with <see cref="IntervalUnit"/>.</summary>
    public int Interval { get; set; }

    /// <summary>The billing interval unit: "month" or "day".</summary>
    public string IntervalUnit { get; set; } = string.Empty;

    public bool RequiresCreditCard { get; set; }
}

/// <summary>A subscription in the shopper's account.</summary>
public class SubscriptionDto
{
    public long SubscriptionId { get; set; }

    public string State { get; set; } = string.Empty;

    public string Currency { get; set; } = string.Empty;

    public long PriceInCents { get; set; }

    public decimal Price { get; set; }

    public long? ProductId { get; set; }

    public string? ProductHandle { get; set; }

    public string? ProductName { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    /// <summary>The next billing date (end of the current period).</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }
}
