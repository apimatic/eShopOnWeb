using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription a customer holds in Maxio, projected to the fields the
/// storefront needs to confirm plan / price / state / next billing date.
/// </summary>
public sealed class CustomerSubscription
{
    public int Id { get; init; }

    /// <summary>Maxio subscription state, e.g. "active", "trialing", "canceled".</summary>
    public string State { get; init; } = string.Empty;

    public string PlanHandle { get; init; } = string.Empty;
    public string PlanName { get; init; } = string.Empty;

    /// <summary>Recurring plan price in cents at the time of subscription.</summary>
    public int ProductPriceInCents { get; init; }

    /// <summary>Recurring plan price as a decimal amount.</summary>
    public decimal Price => ProductPriceInCents / 100m;

    public string Currency { get; init; } = "USD";

    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>
    /// When the next charge is assessed. Maps to Maxio's <c>next_assessment_at</c>,
    /// falling back to <c>current_period_ends_at</c> (Maxio does not reliably expose
    /// a <c>next_billing_at</c> field for the create-subscription response).
    /// </summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public int CustomerId { get; init; }
    public string CustomerReference { get; init; } = string.Empty;

    public DateTimeOffset? CreatedAt { get; init; }
}
