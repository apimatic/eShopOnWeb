using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A recurring plan a shopper can subscribe to, as defined in the billing system of record.
/// </summary>
public class SubscriptionPlan
{
    /// <summary>
    /// Stable, human readable identifier of the plan. This — never a numeric id — is what callers
    /// send back to subscribe, because the billing provider reassigns numeric ids when a site is re-seeded.
    /// </summary>
    public string Handle { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>Recurring price per billing period, in the site's currency.</summary>
    public decimal Price { get; init; }

    /// <summary>Number of <see cref="IntervalUnit"/>s in one billing period (for example 1).</summary>
    public int Interval { get; init; }

    /// <summary>Unit of the billing period as reported by the provider (for example "month").</summary>
    public string IntervalUnit { get; init; } = string.Empty;

    /// <summary>True when the provider requires a payment method before the subscription can be created.</summary>
    public bool RequiresPaymentMethod { get; init; }

    /// <summary>Handle of the product family the plan belongs to.</summary>
    public string? ProductFamilyHandle { get; init; }

    /// <summary>Human readable billing cadence, for example "1 month".</summary>
    public string BillingPeriod => $"{Interval} {IntervalUnit}".Trim();
}
