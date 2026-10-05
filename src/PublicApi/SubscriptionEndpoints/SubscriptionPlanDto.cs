namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription plan (Maxio product) available to shoppers.
/// </summary>
public class SubscriptionPlanDto
{
    public int Id { get; set; }

    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>
    /// The recurring price, in the site's currency.
    /// </summary>
    public decimal Price { get; set; }

    public long PriceInCents { get; set; }

    /// <summary>
    /// The billing interval, e.g. every 1 month.
    /// </summary>
    public int BillingInterval { get; set; }

    public string BillingIntervalUnit { get; set; } = string.Empty;

    /// <summary>
    /// True when a payment method is required to subscribe (false means shoppers can
    /// subscribe without entering a card).
    /// </summary>
    public bool RequiresPaymentMethod { get; set; }

    public bool IsTaxable { get; set; }
}