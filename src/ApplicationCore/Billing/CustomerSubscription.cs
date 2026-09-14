using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A Maxio subscription belonging to an eShopOnWeb customer, reduced to the facts the
/// storefront needs to confirm and display an enrollment: which plan, at what price, in
/// what state, and when it renews next.
/// </summary>
public class CustomerSubscription
{
    public long Id { get; init; }

    /// <summary>The Maxio customer this subscription belongs to.</summary>
    public long CustomerId { get; init; }
    public string? CustomerReference { get; init; }

    /// <summary>Maxio subscription state, e.g. <c>active</c>, <c>trialing</c>, <c>past_due</c>.</summary>
    public string State { get; init; } = string.Empty;

    public string? PlanHandle { get; init; }
    public string? PlanName { get; init; }
    public string? ProductFamilyHandle { get; init; }

    /// <summary>The recurring price captured for this subscription, in cents.</summary>
    public long? PriceInCents { get; init; }
    public string? FormattedPrice { get; init; }
    public int? Interval { get; init; }
    public string? IntervalUnit { get; init; }

    /// <summary>
    /// When the next renewal charge is scheduled (Maxio <c>next_assessment_at</c>, falling
    /// back to <c>current_period_ends_at</c>). Null for subscriptions that never renew.
    /// </summary>
    public DateTimeOffset? NextBillingAt { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>
    /// True when this enrollment already existed and was reused rather than newly created
    /// (double-click / retry safe). Only meaningful on the subscribe response.
    /// </summary>
    public bool AlreadyExisted { get; init; }
}
