using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's enrollment in a plan, projected from a Maxio subscription.
/// </summary>
public class CustomerSubscription
{
    /// <summary>Maxio subscription id.</summary>
    public int SubscriptionId { get; set; }

    /// <summary>Lifecycle state wire value (e.g. "active", "trialing", "canceled").</summary>
    public string? State { get; set; }

    public string? ProductHandle { get; set; }

    public string? ProductName { get; set; }

    /// <summary>Price billed for this subscription in minor units (cents).</summary>
    public long PriceInCents { get; set; }

    public decimal Price => PriceInCents / 100m;

    public string Currency { get; set; } = "USD";

    /// <summary>
    /// End of the current billing period — Maxio's field for the next billing date
    /// (there is no separate next_billing_at on the response).
    /// </summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    /// <summary>The stable customer reference (the eShopOnWeb user identity).</summary>
    public string? CustomerReference { get; set; }
}
