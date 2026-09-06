namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A recurring plan a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable API handle of the plan. Send this value to <c>POST /api/subscriptions</c>.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price expressed in <see cref="Currency"/>.</summary>
    public decimal Price { get; set; }

    /// <summary>Recurring price in the smallest unit of <see cref="Currency"/>.</summary>
    public long PriceInCents { get; set; }

    /// <summary>ISO-4217 currency code.</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>Number of <see cref="IntervalUnit"/>s in a billing period.</summary>
    public int Interval { get; set; }

    /// <summary><c>month</c> or <c>day</c>.</summary>
    public string IntervalUnit { get; set; } = string.Empty;

    public int? TrialInterval { get; set; }

    public string? TrialIntervalUnit { get; set; }

    public long? TrialPriceInCents { get; set; }

    /// <summary>One-off charge applied at signup, when the plan defines one.</summary>
    public long? SetupFeeInCents { get; set; }

    /// <summary>
    /// When true the plan cannot be subscribed to through this API, because eShopOnWeb does not capture
    /// payment instruments.
    /// </summary>
    public bool RequiresPaymentMethod { get; set; }

    public string? ProductFamilyHandle { get; set; }

    /// <summary>Billing-system identifier. Not stable across catalogue re-seeds; prefer <see cref="Handle"/>.</summary>
    public int Id { get; set; }
}
