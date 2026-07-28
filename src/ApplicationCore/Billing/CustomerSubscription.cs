using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A shopper's enrollment in a <see cref="SubscriptionPlan"/>, as recorded by Maxio
/// (the billing system of record), projected into eShopOnWeb's domain shape.
/// </summary>
public class CustomerSubscription
{
    /// <summary>Maxio subscription id.</summary>
    public int Id { get; init; }

    public string PlanHandle { get; init; } = string.Empty;

    public string PlanName { get; init; } = string.Empty;

    /// <summary>Recurring price in cents, when Maxio reports it on the subscription's product.</summary>
    public long? PriceInCents { get; init; }

    public string? Currency { get; init; }

    /// <summary>Subscription lifecycle state as reported by Maxio, e.g. <c>active</c>, <c>trialing</c>, <c>canceled</c>.</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>End of the current billing period.</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>When the subscription will next be billed.</summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    /// <summary>Maxio customer id that owns this subscription.</summary>
    public int CustomerId { get; init; }

    /// <summary>The eShopOnWeb user reference stored on the Maxio customer.</summary>
    public string? CustomerReference { get; init; }
}
