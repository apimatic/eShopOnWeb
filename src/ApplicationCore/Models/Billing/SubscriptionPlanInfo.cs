using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Billing;

/// <summary>
/// A subscribable plan offered by the billing system (a Maxio product in the
/// configured product family). Prices are read live from the provider.
/// </summary>
public class SubscriptionPlanInfo
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>Unit price in cents for one billing interval.</summary>
    public long PriceInCents { get; set; }
    /// <summary>Unit price in the account currency (e.g. USD).</summary>
    public decimal Price => PriceInCents / 100m;
    /// <summary>Number of interval units between billings.</summary>
    public int Interval { get; set; }
    /// <summary>"day" or "month" — the unit of <see cref="Interval"/>.</summary>
    public string IntervalUnit { get; set; } = string.Empty;
}
