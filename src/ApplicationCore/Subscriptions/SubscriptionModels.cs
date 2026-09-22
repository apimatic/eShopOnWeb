using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Identifies the eShopOnWeb user a billing operation acts on. Resolved from the caller's JWT at the
/// endpoint boundary and passed in, so the billing layer stays independent of ASP.NET Identity.
/// <see cref="UserId"/> is the stable idempotency key: it becomes the Maxio customer <c>reference</c>.
/// </summary>
public record SubscriberIdentity(string UserId, string Email, string FirstName, string LastName);

/// <summary>A subscription plan a shopper can subscribe to (a Maxio product within the configured family).</summary>
public record SubscriptionPlan
{
    public required string Handle { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }

    /// <summary>Recurring price, in integer cents.</summary>
    public long PriceInCents { get; init; }

    /// <summary>Billing interval count (e.g. 1) paired with <see cref="IntervalUnit"/> (e.g. "month").</summary>
    public int Interval { get; init; }
    public string? IntervalUnit { get; init; }

    /// <summary>Whether Maxio requires a payment method to subscribe to this plan.</summary>
    public bool RequiresPaymentMethod { get; init; }
}

/// <summary>A subscription a customer holds in Maxio.</summary>
public record CustomerSubscription
{
    public int Id { get; init; }
    public string? PlanHandle { get; init; }
    public string? PlanName { get; init; }

    /// <summary>The raw Maxio subscription state wire value, e.g. "active", "trialing", "canceled".</summary>
    public required string State { get; init; }

    /// <summary>True when <see cref="State"/> is one of Maxio's live states (the customer has access).</summary>
    public bool IsLive { get; init; }

    public long? PriceInCents { get; init; }

    /// <summary>End of the current billing period — i.e. the next billing date.</summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    /// <summary>The app-supplied subscription reference (idempotency key).</summary>
    public string? Reference { get; init; }
}

/// <summary>Outcome of a subscribe request.</summary>
public record SubscribeResult
{
    public required CustomerSubscription Subscription { get; init; }

    /// <summary>The Maxio customer id the subscription belongs to.</summary>
    public int CustomerId { get; init; }

    /// <summary>
    /// True when a live subscription already existed and was returned unchanged (idempotent hit) rather than
    /// a new subscription being created.
    /// </summary>
    public bool AlreadySubscribed { get; init; }
}
