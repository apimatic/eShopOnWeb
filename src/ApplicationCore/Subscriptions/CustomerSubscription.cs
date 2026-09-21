using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's subscription as reflected in Maxio (the billing system of record).
/// </summary>
public class CustomerSubscription
{
    /// <summary>Maxio subscription id.</summary>
    public int Id { get; set; }

    /// <summary>The subscribed product's API handle.</summary>
    public string? PlanHandle { get; set; }

    /// <summary>The subscribed product's display name.</summary>
    public string? PlanName { get; set; }

    /// <summary>Current subscription state wire value (e.g. "active", "trialing").</summary>
    public string? State { get; set; }

    /// <summary>Current recurring amount in cents.</summary>
    public long? PriceInCents { get; set; }

    /// <summary>
    /// The next billing / assessment date, when Maxio will next bill this subscription.
    /// </summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    /// <summary>The reference value this app assigned to the subscription (traceability).</summary>
    public string? Reference { get; set; }
}
