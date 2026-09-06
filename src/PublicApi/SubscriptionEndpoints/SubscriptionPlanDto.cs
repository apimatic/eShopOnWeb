namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    /// <summary>The value to send back as <c>planHandle</c> when subscribing.</summary>
    public string Handle { get; set; } = string.Empty;

    public string? Name { get; set; }

    public string? Description { get; set; }

    public long? PriceInCents { get; set; }

    public decimal? Price { get; set; }

    /// <summary>The recurring price rendered for display, without a currency symbol.</summary>
    public string? PriceDisplay { get; set; }

    /// <summary>How often the plan bills, for example <c>1 month</c>.</summary>
    public string? BillingPeriod { get; set; }

    public int? Interval { get; set; }

    public string? IntervalUnit { get; set; }

    public bool HasTrial { get; set; }

    public string? TrialPeriod { get; set; }

    public decimal? SetupFee { get; set; }

    /// <summary>When true, subscribing requires a payment method to be captured first.</summary>
    public bool RequiresCreditCard { get; set; }

    public bool? Taxable { get; set; }
}
