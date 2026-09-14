using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's subscription as reported by Maxio. Carries the plan, price, state and the
/// next billing date so the enrolment can be confirmed back to the user.
/// </summary>
public record CustomerSubscription
{
    /// <summary>Maxio subscription id.</summary>
    public int Id { get; init; }

    /// <summary>Current lifecycle state (e.g. "active", "trialing", "canceled").</summary>
    public required string State { get; init; }

    /// <summary>Handle of the subscribed plan (Maxio product handle).</summary>
    public string? PlanHandle { get; init; }

    /// <summary>Name of the subscribed plan.</summary>
    public string? PlanName { get; init; }

    /// <summary>Recurring product price in cents at the time of subscription.</summary>
    public int ProductPriceInCents { get; init; }

    /// <summary>Recurring product price as a decimal amount.</summary>
    public decimal ProductPrice => ProductPriceInCents / 100m;

    /// <summary>Start of the current billing period.</summary>
    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    /// <summary>End of the current billing period.</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>The next date Maxio will assess/bill the subscription.</summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    /// <summary>When the subscription was created.</summary>
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>Application-supplied reference used to make enrolment idempotent.</summary>
    public string? Reference { get; init; }

    /// <summary>Maxio customer id owning the subscription.</summary>
    public int CustomerId { get; init; }

    /// <summary>
    /// True when the subscription already existed and was returned instead of creating a new one
    /// (idempotent subscribe). False when this call created it.
    /// </summary>
    public bool AlreadyExisted { get; init; }
}
