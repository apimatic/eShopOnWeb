using Microsoft.eShopWeb.PublicApi;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscribable plan offered to the shopper.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>
    /// Maxio product handle — pass this value to POST /api/subscriptions.
    /// </summary>
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Recurring price, e.g. 299.00.
    /// </summary>
    public decimal Price { get; set; }

    /// <summary>
    /// Recurring price in cents, as reported by Maxio.
    /// </summary>
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Whether Maxio requires a payment method on file for this plan.
    /// </summary>
    public bool RequireCreditCard { get; set; }
}
