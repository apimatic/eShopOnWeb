namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A recurring plan a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable identifier used when subscribing. Plans are addressed by handle, never by id.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price in major currency units, e.g. 299.00.</summary>
    public decimal Price { get; set; }

    /// <summary>Recurring price in minor currency units, exactly as the billing provider reports it.</summary>
    public long PriceInCents { get; set; }

    public string? Currency { get; set; }

    /// <summary>Billing period length, counted in <see cref="IntervalUnit"/>.</summary>
    public int? Interval { get; set; }

    /// <summary>Billing period unit, e.g. <c>month</c>.</summary>
    public string? IntervalUnit { get; set; }

    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// True when the billing provider requires a stored payment profile to sign up for this plan. This
    /// subscribe flow captures no card, so such a plan cannot be subscribed to here.
    /// </summary>
    public bool RequiresPaymentProfileAtSignup { get; set; }
}
