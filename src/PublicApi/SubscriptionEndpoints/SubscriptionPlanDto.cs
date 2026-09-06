namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A plan a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable identifier of the plan. Use this value when subscribing.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price in cents, e.g. <c>29900</c>.</summary>
    public long PriceInCents { get; set; }

    /// <summary>Recurring price as a decimal amount, e.g. <c>299.00</c>.</summary>
    public string Price { get; set; } = string.Empty;

    /// <summary>Human-readable billing period, e.g. <c>month</c> or <c>3 months</c>.</summary>
    public string BillingPeriod { get; set; } = string.Empty;

    /// <summary>True when a payment method must be captured before the shopper can subscribe.</summary>
    public bool RequiresPaymentMethod { get; set; }

    /// <summary>One-off signup charge in cents, when the plan has one.</summary>
    public long? SetupFeeInCents { get; set; }

    /// <summary>Human-readable trial period, when the plan has one.</summary>
    public string? TrialPeriod { get; set; }

    public string? PricePointName { get; set; }
}
