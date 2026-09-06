using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription plan as exposed by the public API.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable handle used to subscribe to this plan.</summary>
    public string Handle { get; set; } = string.Empty;

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional description.</summary>
    public string? Description { get; set; }

    /// <summary>Recurring price in major units, e.g. 299.00.</summary>
    public decimal Price { get; set; }

    /// <summary>Recurring price in minor units, e.g. 29900.</summary>
    public long PriceInCents { get; set; }

    /// <summary>Number of <see cref="IntervalUnit"/>s per billing period.</summary>
    public int Interval { get; set; }

    /// <summary>Billing period unit, "day" or "month".</summary>
    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>Human-readable billing period, e.g. "every month".</summary>
    public string BillingPeriod { get; set; } = string.Empty;

    /// <summary>Handle of the product family the plan belongs to.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>True when subscribing to this plan requires a stored payment method.</summary>
    public bool RequiresPaymentMethod { get; set; }

    /// <summary>Trial length, when the plan offers one.</summary>
    public int? TrialInterval { get; set; }

    /// <summary>Unit of <see cref="TrialInterval"/>.</summary>
    public string? TrialIntervalUnit { get; set; }

    /// <summary>One-off signup charge in minor units, when the plan has one.</summary>
    public long? SetupFeeInCents { get; set; }

    /// <summary>Whether the plan is taxable.</summary>
    public bool Taxable { get; set; }
}
