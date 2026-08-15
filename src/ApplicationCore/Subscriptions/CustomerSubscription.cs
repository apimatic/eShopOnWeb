using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's enrollment in a plan, as reported back by the billing provider. Provider-neutral.
/// </summary>
public class CustomerSubscription
{
    public int SubscriptionId { get; set; }

    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    /// <summary>Current recurring price in the smallest currency unit (cents).</summary>
    public long PriceInCents { get; set; }

    /// <summary>Provider subscription state, e.g. "active", "trialing", "canceled".</summary>
    public string? State { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    /// <summary>
    /// End of the current billing period — the effective next-billing date. (Providers surface the
    /// next charge date here rather than a separate "next billing at" field on reads.)
    /// </summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    public string? CustomerReference { get; set; }
}
