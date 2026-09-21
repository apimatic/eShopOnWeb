using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb domain view of a shopper's active/enrolled subscription, as reflected back from the
/// billing system of record after a subscribe or a lookup.
/// </summary>
public record CustomerSubscription
{
    /// <summary>The subscription id in the billing system.</summary>
    public required int Id { get; init; }

    /// <summary>The API handle of the subscribed plan/product.</summary>
    public string? PlanHandle { get; init; }

    /// <summary>The name of the subscribed plan/product.</summary>
    public string? PlanName { get; init; }

    /// <summary>The recurring product price in integer cents at the time of enrollment.</summary>
    public long? PriceInCents { get; init; }

    /// <summary>The subscription lifecycle state (e.g. <c>active</c>, <c>trialing</c>).</summary>
    public string? State { get; init; }

    /// <summary>
    /// When the next regularly scheduled charge will occur — the shopper-facing "next billing date".
    /// </summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    /// <summary>The billing-system customer id this subscription belongs to.</summary>
    public required int CustomerId { get; init; }

    /// <summary>The eShopOnWeb-owned reference stored on the billing customer (the idempotency key).</summary>
    public string? CustomerReference { get; init; }
}
