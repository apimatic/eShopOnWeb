namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A recurring plan a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable plan identifier. Post this back to <c>/api/subscriptions</c> to subscribe.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price per billing period, in <see cref="Currency"/>.</summary>
    public decimal Price { get; set; }

    public long PriceInCents { get; set; }

    public string? Currency { get; set; }

    /// <summary>Number of <see cref="IntervalUnit"/>s per billing period, e.g. 1.</summary>
    public int Interval { get; set; }

    /// <summary>Billing period unit, e.g. <c>month</c>.</summary>
    public string? IntervalUnit { get; set; }

    public decimal SetupFee { get; set; }

    public bool HasTrial { get; set; }

    public int TrialInterval { get; set; }

    public string? TrialIntervalUnit { get; set; }

    /// <summary>
    /// Whether the billing provider's own hosted signup page asks for a payment profile. Informational:
    /// it does not affect <c>POST /api/subscriptions</c>, which enrolls on an invoiced collection method
    /// and captures no card.
    /// </summary>
    public bool RequiresPaymentProfileAtSignup { get; set; }
}
