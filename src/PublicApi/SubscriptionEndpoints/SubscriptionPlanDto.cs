namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A plan a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable API handle. This is the value to send as <c>planHandle</c> when subscribing.</summary>
    public string Handle { get; set; } = string.Empty;

    /// <summary>The billing provider's numeric product id. Informational only - it is not stable across catalog re-seeds.</summary>
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price in minor currency units.</summary>
    public long PriceInCents { get; set; }

    /// <summary>Recurring price as a decimal amount.</summary>
    public decimal Price { get; set; }

    /// <summary>ISO currency code of the billing site, when it could be resolved.</summary>
    public string? Currency { get; set; }

    /// <summary>Number of <see cref="IntervalUnit"/>s between renewals.</summary>
    public int Interval { get; set; }

    /// <summary>Renewal interval unit: <c>day</c> or <c>month</c>.</summary>
    public string? IntervalUnit { get; set; }

    /// <summary>True when a payment method must be captured before the shopper can be enrolled.</summary>
    public bool RequiresPaymentMethod { get; set; }

    public int? TrialInterval { get; set; }

    public string? TrialIntervalUnit { get; set; }

    public long? TrialPriceInCents { get; set; }

    public int? PricePointId { get; set; }

    public string? PricePointName { get; set; }

    /// <summary>Handle of the product family the plan belongs to.</summary>
    public string? ProductFamilyHandle { get; set; }
}
