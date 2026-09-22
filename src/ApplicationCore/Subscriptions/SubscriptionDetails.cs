using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's subscription as read back from the billing provider, projected into plain,
/// provider-agnostic terms for confirmation and display.
/// </summary>
public record SubscriptionDetails
{
    /// <summary>The provider's numeric subscription id.</summary>
    public int? Id { get; init; }

    /// <summary>The deterministic reference we assigned to this subscription.</summary>
    public string? Reference { get; init; }

    public string? PlanHandle { get; init; }

    public string? PlanName { get; init; }

    /// <summary>The recurring price in the smallest currency unit (e.g. cents).</summary>
    public long? PriceInCents { get; init; }

    public string? Currency { get; init; }

    /// <summary>The provider's subscription state verbatim (e.g. "active", "trialing").</summary>
    public string? State { get; init; }

    /// <summary>When the next billing charge is scheduled (end of the current period).</summary>
    public DateTimeOffset? NextBillingDate { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }
}
