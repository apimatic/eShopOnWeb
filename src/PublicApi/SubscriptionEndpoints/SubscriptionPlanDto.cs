using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A plan a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>
    /// Stable identifier for the plan; the value to send back as <c>planHandle</c> when subscribing.
    /// </summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price per billing period, in <see cref="Currency"/>.</summary>
    public decimal Price { get; set; }

    /// <summary>Recurring price in minor units, for callers that prefer integer money.</summary>
    public long PriceInCents { get; set; }

    /// <summary>ISO currency code, or null if the billing site did not report one.</summary>
    public string? Currency { get; set; }

    /// <summary>Number of <see cref="IntervalUnit"/>s per billing period.</summary>
    public int Interval { get; set; }

    /// <summary>"day" or "month".</summary>
    public string? IntervalUnit { get; set; }

    /// <summary>Human-readable billing period, e.g. "every month".</summary>
    public string BillingPeriod { get; set; } = string.Empty;

    /// <summary>True when subscribing to this plan requires a stored payment method.</summary>
    public bool RequiresPaymentMethod { get; set; }

    /// <summary>Trial length in <see cref="TrialIntervalUnit"/>s, or null when the plan has no trial.</summary>
    public int? TrialInterval { get; set; }

    public string? TrialIntervalUnit { get; set; }

    public decimal? TrialPrice { get; set; }
}
