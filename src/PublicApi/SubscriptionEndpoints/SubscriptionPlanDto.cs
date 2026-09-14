namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A recurring plan a shopper can subscribe to.</summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable identifier to pass to POST /api/subscriptions.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price in minor units, e.g. 29900 for $299.00.</summary>
    public long PriceInCents { get; set; }

    /// <summary>Recurring price in major units.</summary>
    public decimal Price { get; set; }

    public string Currency { get; set; } = string.Empty;

    /// <summary>Number of <see cref="IntervalUnit"/>s between renewals.</summary>
    public int Interval { get; set; }

    /// <summary>Renewal interval unit, "month" or "day".</summary>
    public string IntervalUnit { get; set; } = string.Empty;

    public bool RequiresPaymentMethod { get; set; }

    public bool HasTrial { get; set; }

    public int? TrialInterval { get; set; }

    public string? TrialIntervalUnit { get; set; }

    public long? SetupFeeInCents { get; set; }
}
