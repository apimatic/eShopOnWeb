namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription plan a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable plan handle to pass to POST /api/subscriptions (e.g. "eshop-pro").</summary>
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Recurring price in cents.</summary>
    public int PriceInCents { get; set; }

    /// <summary>Recurring price in major currency units.</summary>
    public decimal Price { get; set; }
    public string Currency { get; set; } = "USD";

    /// <summary>Billing interval unit, e.g. "month".</summary>
    public string IntervalUnit { get; set; } = "month";

    /// <summary>Number of interval units per billing period, e.g. 1.</summary>
    public int IntervalCount { get; set; }

    /// <summary>Whether the plan requires a card on file to subscribe.</summary>
    public bool RequiresPaymentMethod { get; set; }
}
