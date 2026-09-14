namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription plan a shopper can enroll on.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable identifier of the plan; this is what <c>POST api/subscriptions</c> expects.</summary>
    public string Handle { get; set; } = string.Empty;

    /// <summary>Identifier in the billing system. Not stable across catalog re-seeds.</summary>
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price as a decimal amount, e.g. 299.00.</summary>
    public decimal Price { get; set; }

    /// <summary>Recurring price in cents, e.g. 29900.</summary>
    public long PriceInCents { get; set; }

    /// <summary>ISO currency code, e.g. USD.</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>Number of <see cref="IntervalUnit"/>s between renewals.</summary>
    public int Interval { get; set; }

    /// <summary>Renewal interval unit, <c>month</c> or <c>day</c>.</summary>
    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>Human readable billing cadence, e.g. "every month".</summary>
    public string BillingPeriod { get; set; } = string.Empty;

    /// <summary>True when the plan cannot be subscribed to without a stored payment method.</summary>
    public bool RequiresPaymentMethod { get; set; }

    /// <summary>Trial length in <see cref="TrialIntervalUnit"/>s, null when the plan has no trial.</summary>
    public int? TrialInterval { get; set; }

    public string? TrialIntervalUnit { get; set; }

    /// <summary>Price of the trial period in cents, when the plan has one.</summary>
    public long? TrialPriceInCents { get; set; }

    /// <summary>Handle of the product family the plan belongs to.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>Name of the price point the plan is quoted at.</summary>
    public string? PricePointName { get; set; }
}
