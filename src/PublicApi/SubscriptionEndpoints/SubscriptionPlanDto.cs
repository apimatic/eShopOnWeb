namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription plan as returned to API callers.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable handle used to subscribe to this plan.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price in the smallest unit of <see cref="Currency"/>.</summary>
    public long PriceInCents { get; set; }

    /// <summary>Recurring price as a decimal amount.</summary>
    public decimal Price { get; set; }

    public string Currency { get; set; } = string.Empty;

    /// <summary>Number of <see cref="IntervalUnit"/>s between renewals.</summary>
    public int Interval { get; set; }

    /// <summary>Renewal interval unit, e.g. month or day.</summary>
    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>Human readable billing period, e.g. "month" or "3 months".</summary>
    public string BillingPeriod { get; set; } = string.Empty;

    /// <summary>Whether the plan needs a payment method before it can be subscribed to.</summary>
    public bool RequiresPaymentMethod { get; set; }

    /// <summary>Name of the price point this plan is currently priced from.</summary>
    public string? PricePointName { get; set; }

    /// <summary>Trial length, when the plan offers one.</summary>
    public int? TrialInterval { get; set; }

    /// <summary>Unit of <see cref="TrialInterval"/>.</summary>
    public string? TrialIntervalUnit { get; set; }

    /// <summary>Handle of the billing product family the plan belongs to.</summary>
    public string? ProductFamilyHandle { get; set; }
}
