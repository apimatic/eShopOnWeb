namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A recurring plan a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable identifier of the plan; pass this to POST /api/subscriptions.</summary>
    public string Handle { get; set; } = string.Empty;

    public string? Name { get; set; }

    public string? Description { get; set; }

    /// <summary>Recurring price in cents, as the billing provider reports it.</summary>
    public long? PriceInCents { get; set; }

    /// <summary>Recurring price as a decimal amount.</summary>
    public decimal? Price { get; set; }

    /// <summary>Number of <see cref="IntervalUnit"/>s between billings.</summary>
    public int? Interval { get; set; }

    /// <summary>Unit the billing interval is measured in, for example "month".</summary>
    public string? IntervalUnit { get; set; }

    public string? ProductFamilyHandle { get; set; }

    public string? ProductFamilyName { get; set; }

    /// <summary>True when a payment method must be captured before subscribing.</summary>
    public bool RequiresPaymentMethod { get; set; }

    public bool HasTrial { get; set; }

    /// <summary>One-off signup charge in cents; zero when there is no setup fee.</summary>
    public long SetupFeeInCents { get; set; }
}
