namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A recurring plan a customer can subscribe to.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>The durable plan identifier, for example <c>eshop-pro</c>.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price in dollars.</summary>
    public decimal Price { get; set; }

    /// <summary>Recurring price in cents, as the billing provider reports it.</summary>
    public long PriceInCents { get; set; }

    public int Interval { get; set; }

    public string IntervalUnit { get; set; } = string.Empty;

    public bool RequiresPaymentMethod { get; set; }
}
