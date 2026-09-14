namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscribable plan as presented to API clients.
/// </summary>
public class SubscriptionPlanDto
{
    public int ProductId { get; set; }

    /// <summary>Stable handle used to subscribe (pass as <c>planHandle</c> to POST api/subscriptions).</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public int PriceInCents { get; set; }

    /// <summary>Price as a decimal amount (major units).</summary>
    public decimal Price { get; set; }

    public string FormattedPrice { get; set; } = string.Empty;

    /// <summary>Billing interval count (e.g. 1).</summary>
    public int Interval { get; set; }

    /// <summary>Billing interval unit (e.g. "month").</summary>
    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>Human-readable billing cadence, e.g. "every month".</summary>
    public string BillingCadence { get; set; } = string.Empty;

    public bool RequiresPaymentMethod { get; set; }
}
