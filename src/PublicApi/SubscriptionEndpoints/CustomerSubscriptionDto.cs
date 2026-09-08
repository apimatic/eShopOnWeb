using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A shopper's enrollment in a plan, as reported by Maxio.
/// </summary>
public class CustomerSubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string? State { get; set; }
    public string? ProductHandle { get; set; }
    public string? ProductName { get; set; }
    public long PriceInCents { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "USD";

    /// <summary>End of the current billing period — Maxio's next-billing date.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }
}
