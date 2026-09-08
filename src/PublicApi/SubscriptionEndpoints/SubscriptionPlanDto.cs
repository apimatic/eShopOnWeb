namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A subscribable plan offered by the configured Maxio product family.</summary>
public class SubscriptionPlanDto
{
    /// <summary>The Maxio product id. Numeric ids are unstable across sandbox reseeds; use <see cref="Handle"/>.</summary>
    public long Id { get; set; }

    /// <summary>The stable API handle of the plan (e.g. "eshop-pro").</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>The recurring price in integer cents (e.g. 29900 for $299.00).</summary>
    public long? PriceInCents { get; set; }

    /// <summary>The recurring price as a decimal amount (e.g. 299.00).</summary>
    public decimal Price { get; set; }

    /// <summary>The billing interval (e.g. 1) combined with <see cref="IntervalUnit"/> (e.g. "month").</summary>
    public int? Interval { get; set; }

    public string? IntervalUnit { get; set; }

    /// <summary>Whether a payment method is required to subscribe to this plan.</summary>
    public bool RequiresPaymentMethod { get; set; }
}
