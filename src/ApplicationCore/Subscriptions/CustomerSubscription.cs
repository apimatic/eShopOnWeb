using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription belonging to an eShopOnWeb user, as tracked by Maxio Advanced Billing.
/// </summary>
public class CustomerSubscription
{
    /// <summary>Maxio subscription id (system of record).</summary>
    public long Id { get; set; }

    /// <summary>Maxio subscription state, e.g. "active", "trialing", "canceled".</summary>
    public string State { get; set; } = string.Empty;

    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    public int PriceInCents { get; set; }

    public string FormattedPrice { get; set; } = string.Empty;

    public string IntervalUnit { get; set; } = string.Empty;

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When Maxio will next assess/bill this subscription (the "next billing date").</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>Maxio customer id this subscription belongs to.</summary>
    public long CustomerId { get; set; }

    /// <summary>Stable eShopOnWeb reference used to correlate the Maxio customer to the user.</summary>
    public string? CustomerReference { get; set; }

    /// <summary>
    /// True when this subscription was created by the current request; false when an existing
    /// matching subscription was returned (idempotent replay of a duplicate subscribe).
    /// </summary>
    public bool WasCreated { get; set; }
}
