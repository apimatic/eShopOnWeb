using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A domain-level view of a shopper's subscription as held by the billing system of record.
/// </summary>
public class SubscriptionDetails
{
    /// <summary>The subscription's unique id in the billing system.</summary>
    public int SubscriptionId { get; set; }

    /// <summary>The plan (product) handle the subscription is on, when known.</summary>
    public string? PlanHandle { get; set; }

    /// <summary>The plan (product) name, when known.</summary>
    public string? PlanName { get; set; }

    /// <summary>Current subscription state wire value (e.g. <c>active</c>, <c>trialing</c>).</summary>
    public string? State { get; set; }

    /// <summary>The recurring product price for this subscription, in integer cents.</summary>
    public long PriceInCents { get; set; }

    /// <summary>The recurring price expressed in major currency units (cents / 100).</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>When the next regularly scheduled charge is expected (end of the current period).</summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    /// <summary>The app-supplied reference used for idempotent subscription lookup.</summary>
    public string? Reference { get; set; }
}
