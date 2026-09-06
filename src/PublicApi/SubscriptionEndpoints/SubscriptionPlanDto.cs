namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A recurring plan a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable API handle of the plan; use this value when subscribing.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price in the major currency unit, e.g. 299.00.</summary>
    public decimal Price { get; set; }

    /// <summary>Recurring price in the minor currency unit, e.g. 29900.</summary>
    public long PriceInCents { get; set; }

    /// <summary>Number of <see cref="IntervalUnit"/>s between renewals.</summary>
    public int Interval { get; set; }

    /// <summary>"month" or "day".</summary>
    public string? IntervalUnit { get; set; }

    /// <summary>Human readable cadence, e.g. "month" or "3 months".</summary>
    public string? BillingPeriod { get; set; }

    public string? PricePointName { get; set; }

    /// <summary>True when Maxio requires a stored payment method before the subscription can start.</summary>
    public bool RequiresPaymentMethod { get; set; }

    public bool Taxable { get; set; }

    public int? TrialInterval { get; set; }

    public string? TrialIntervalUnit { get; set; }

    public decimal? TrialPrice { get; set; }

    public decimal? SetupFee { get; set; }

    public string? ProductFamilyHandle { get; set; }

    public string? ProductFamilyName { get; set; }
}
