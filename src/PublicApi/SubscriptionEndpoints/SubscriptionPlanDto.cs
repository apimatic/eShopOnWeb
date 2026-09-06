namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A plan a shopper can subscribe to. <see cref="Handle"/> is the stable identifier to post back when
/// subscribing; <see cref="Id"/> is informational only, because the billing system reassigns numeric ids
/// when a catalog is re-seeded.
/// </summary>
public class SubscriptionPlanDto
{
    public int? Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Recurring price in cents.</summary>
    public long? PriceInCents { get; set; }

    /// <summary>Recurring price as a decimal amount.</summary>
    public decimal? Price { get; set; }

    /// <summary>Number of <see cref="IntervalUnit"/>s between renewals.</summary>
    public int? Interval { get; set; }

    /// <summary>Billing interval unit, e.g. "month".</summary>
    public string? IntervalUnit { get; set; }

    /// <summary>Null when the billing system did not report it - unknown, rather than "not required".</summary>
    public bool? PaymentMethodRequired { get; set; }
}
