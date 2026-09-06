using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's enrollment in a <see cref="SubscriptionPlan"/>, as reported by the billing system
/// of record. eShopOnWeb never persists this - it is always read back from the provider.
/// </summary>
public sealed record Subscription
{
    /// <summary>Provider-assigned subscription id.</summary>
    public required string Id { get; init; }

    /// <summary>Coarse lifecycle bucket used for entitlement decisions.</summary>
    public required SubscriptionState State { get; init; }

    /// <summary>Verbatim provider state, e.g. "active", "past_due", "trialing".</summary>
    public required string ProviderState { get; init; }

    public required string PlanHandle { get; init; }
    public required string PlanName { get; init; }

    /// <summary>Recurring price actually being charged for this subscription.</summary>
    public required decimal Price { get; init; }

    public required string Currency { get; init; }
    public required BillingInterval Interval { get; init; }

    /// <summary>When the next recurring charge is expected. Null once the subscription ends.</summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    public DateTimeOffset? CanceledAt { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Provider-assigned customer id this subscription belongs to.</summary>
    public required string CustomerId { get; init; }

    /// <summary>The eShopOnWeb-owned reference stored against the provider customer record.</summary>
    public required string CustomerReference { get; init; }

    /// <summary>The eShopOnWeb-owned reference stored against the subscription itself.</summary>
    public string? Reference { get; init; }
}
