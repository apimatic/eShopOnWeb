namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A plan a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable identifier of the plan; pass this to POST /api/subscriptions.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price in the major unit, e.g. 299.00.</summary>
    public decimal Price { get; set; }

    /// <summary>Recurring price in the minor unit, e.g. 29900.</summary>
    public long PriceInCents { get; set; }

    /// <summary>Billing interval count, combined with <see cref="IntervalUnit"/> (e.g. 1 "month").</summary>
    public int Interval { get; set; }

    public string? IntervalUnit { get; set; }

    /// <summary>Product family the plan belongs to.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>True when the billing provider needs a stored payment method before the plan can start.</summary>
    public bool RequiresPaymentMethod { get; set; }

    public int? TrialInterval { get; set; }

    public string? TrialIntervalUnit { get; set; }
}
