using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Models.SubscriptionBilling;

/// <summary>
/// A subscription plan offered by the billing provider (a Maxio product in the configured product family).
/// </summary>
public record SubscriptionPlanInfo
{
    /// <summary>
    /// The provider's product id. Numeric provider ids are not stable across re-seeds; prefer Handle.
    /// </summary>
    public int MaxioProductId { get; init; }

    /// <summary>
    /// The stable API handle of the plan.
    /// </summary>
    public string Handle { get; init; } = default!;

    public string Name { get; init; } = default!;

    public string? Description { get; init; }

    /// <summary>
    /// Recurring price in integer cents.
    /// </summary>
    public long PriceInCents { get; init; }

    /// <summary>
    /// Billing interval count (e.g. 1 for every unit).
    /// </summary>
    public int? Interval { get; init; }

    /// <summary>
    /// Billing interval unit ("day" / "month"), or the raw provider value when undeclared.
    /// </summary>
    public string? IntervalUnit { get; init; }

    /// <summary>
    /// Whether the provider requires a payment profile to subscribe to this plan.
    /// </summary>
    public bool RequiresPaymentProfile { get; init; }
}

/// <summary>
/// A subscription an eShopOnWeb user holds at the billing provider.
/// </summary>
public record SubscriptionInfo
{
    public int MaxioSubscriptionId { get; init; }

    public string PlanHandle { get; init; } = default!;

    public string? PlanName { get; init; }

    /// <summary>
    /// Recurring price in integer cents, as subscribed.
    /// </summary>
    public long PriceInCents { get; init; }

    /// <summary>
    /// Provider subscription state (e.g. "active", "past_due").
    /// </summary>
    public string State { get; init; } = default!;

    /// <summary>
    /// When the next regularly scheduled charge will occur.
    /// </summary>
    public System.DateTimeOffset? NextBillingAt { get; init; }

    public System.DateTimeOffset? CreatedAt { get; init; }

    /// <summary>
    /// True when the subscription already existed (an idempotent replay), false when it was created by this call.
    /// </summary>
    public bool Existing { get; init; }
}