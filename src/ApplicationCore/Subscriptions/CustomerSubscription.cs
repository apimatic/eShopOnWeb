using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's subscription as recorded by Maxio Advanced Billing (the system of
/// record). Projected into a presentation-safe shape so no SDK types leak upward.
/// </summary>
public class CustomerSubscription
{
    /// <summary>Maxio subscription id.</summary>
    public int Id { get; set; }

    public string? ProductHandle { get; set; }

    public string? ProductName { get; set; }

    /// <summary>Subscription state wire value, e.g. "active", "trialing", "canceled".</summary>
    public string? State { get; set; }

    /// <summary>Current recurring price for the subscription, in cents.</summary>
    public long? PriceInCents { get; set; }

    /// <summary>Convenience view of <see cref="PriceInCents"/> in the major currency unit.</summary>
    public decimal? Price => PriceInCents is null ? null : PriceInCents.Value / 100m;

    /// <summary>Next billing / assessment date.</summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    /// <summary>The customer reference this subscription belongs to (the eShopOnWeb user identity).</summary>
    public string? CustomerReference { get; set; }
}
