namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A plan a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable identifier of the plan. Send this as <c>planHandle</c> to subscribe.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price, for example 299.00.</summary>
    public decimal Price { get; set; }

    /// <summary>Recurring price in the smallest currency unit, for example 29900.</summary>
    public long PriceInCents { get; set; }

    public string Currency { get; set; } = "USD";

    /// <summary>Number of <see cref="IntervalUnit"/>s in one billing period.</summary>
    public int Interval { get; set; }

    /// <summary>Unit of the billing period, for example "month".</summary>
    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>Human-readable billing cadence, for example "$299.00 / month".</summary>
    public string BillingPeriod { get; set; } = string.Empty;

    /// <summary>True when subscribing requires a payment method to already be on file.</summary>
    public bool RequiresPaymentMethod { get; set; }

    public int? TrialInterval { get; set; }

    public string? TrialIntervalUnit { get; set; }
}
