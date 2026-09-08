namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription plan offered by the Maxio catalog (a product in the configured product family).
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>The Maxio product id.</summary>
    public long Id { get; set; }

    /// <summary>Stable Maxio API handle used to subscribe (e.g. <c>eshop-pro</c>).</summary>
    public string Handle { get; set; } = string.Empty;

    /// <summary>Display name of the plan (e.g. "Pro Plan").</summary>
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price expressed in the smallest currency unit (cents).</summary>
    public long PriceInCents { get; set; }

    /// <summary>ISO 4217 currency code of the site (e.g. "USD").</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Billing frequency magnitude (e.g. 1).</summary>
    public int Interval { get; set; }

    /// <summary>Billing frequency unit (e.g. "month").</summary>
    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>Id of the product price point that will be used at signup.</summary>
    public long? PricePointId { get; set; }

    /// <summary>True when Maxio requires a payment method before the subscription can be created.</summary>
    public bool RequiresPaymentMethod { get; set; }
}
