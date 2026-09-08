namespace Microsoft.eShopWeb.Maxio.Contracts;

/// <summary>
/// A subscribable plan as exposed by the Maxio product catalog for the configured product family.
/// </summary>
public sealed class SubscriptionPlan
{
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price in the site currency.</summary>
    public decimal Price { get; set; }

    /// <summary>Billing frequency (e.g. 1, 3).</summary>
    public int Interval { get; set; }

    /// <summary>Billing frequency unit: "month" or "day".</summary>
    public string IntervalUnit { get; set; } = string.Empty;

    public string Currency { get; set; } = string.Empty;

    public bool RequiresCreditCard { get; set; }

    public int? TrialInterval { get; set; }

    public string? TrialIntervalUnit { get; set; }

    public decimal? InitialCharge { get; set; }
}
