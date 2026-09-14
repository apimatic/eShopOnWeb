namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A recurring plan a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable handle to pass to POST /api/subscriptions. Numeric plan ids are not exposed.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price per billing period, in <see cref="Currency"/>.</summary>
    public decimal Price { get; set; }

    public long PriceInCents { get; set; }

    public string Currency { get; set; } = string.Empty;

    /// <summary>Billing period length, counted in <see cref="IntervalUnit"/>s (e.g. 1 month).</summary>
    public int Interval { get; set; }

    public string IntervalUnit { get; set; } = string.Empty;

    public decimal SetupFee { get; set; }

    public bool HasTrial { get; set; }

    public int? TrialInterval { get; set; }

    public string? TrialIntervalUnit { get; set; }

    /// <summary>True when the plan cannot be subscribed to without a payment method on file.</summary>
    public bool RequiresPaymentMethod { get; set; }

    public string ProductFamilyHandle { get; set; } = string.Empty;
}
