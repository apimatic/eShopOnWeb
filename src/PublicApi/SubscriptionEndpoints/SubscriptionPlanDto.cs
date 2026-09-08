using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription plan as surfaced to shoppers, mapped from a Maxio product.
/// </summary>
public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>
    /// Recurring price in minor units (cents), as reported by Maxio.
    /// </summary>
    public long PriceInCents { get; set; }

    /// <summary>
    /// Recurring price in major units (e.g. dollars).
    /// </summary>
    public decimal Price => PriceInCents / 100m;

    public int? Interval { get; set; }

    public string? IntervalUnit { get; set; }

    public bool HasTrial => TrialInterval is > 0;

    public int? TrialInterval { get; set; }

    public string? TrialIntervalUnit { get; set; }

    /// <summary>
    /// Whether Maxio requires a payment method at signup for this plan.
    /// </summary>
    public bool RequireCreditCard { get; set; }
}
