namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A recurring plan a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>The identifier to pass as <c>planHandle</c> when subscribing.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price in major currency units, e.g. 299.00.</summary>
    public decimal Price { get; set; }

    /// <summary>Recurring price in the smallest currency unit, e.g. 29900.</summary>
    public long PriceInCents { get; set; }

    public string? Currency { get; set; }

    /// <summary>Number of <see cref="IntervalUnit"/>s in a billing period.</summary>
    public int Interval { get; set; }

    /// <summary>Unit of the billing period, either <c>month</c> or <c>day</c>.</summary>
    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>True when a payment method has to be on file before subscribing.</summary>
    public bool RequiresPaymentMethod { get; set; }
}
