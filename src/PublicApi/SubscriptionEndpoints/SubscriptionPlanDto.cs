namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A plan a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Identifier to pass as <c>planHandle</c> when subscribing, e.g. <c>eshop-pro</c>.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price, e.g. 299.00.</summary>
    public decimal Price { get; set; }

    /// <summary>Recurring price in the smallest currency unit, e.g. 29900.</summary>
    public long PriceInCents { get; set; }

    /// <summary>ISO 4217 currency code, e.g. USD.</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>Number of <see cref="IntervalUnit"/>s between renewals.</summary>
    public int Interval { get; set; }

    /// <summary>Billing interval unit, e.g. <c>month</c>.</summary>
    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>Human readable billing cadence, e.g. "$299.00 USD / month".</summary>
    public string BillingSummary { get; set; } = string.Empty;

    public bool HasTrial { get; set; }

    public int? TrialInterval { get; set; }

    public string? TrialIntervalUnit { get; set; }

    /// <summary>One-off signup charge, e.g. 0.00.</summary>
    public decimal InitialCharge { get; set; }

    /// <summary>True when the plan requires a stored payment method before it can be started.</summary>
    public bool RequiresPaymentMethod { get; set; }

    /// <summary>Name of the price point the plan is currently sold at.</summary>
    public string? PricePointName { get; set; }
}
