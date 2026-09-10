using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's subscription as held by the billing system of record (Maxio). Projected from a
/// Maxio subscription so no SDK type leaks past the application-core boundary.
/// </summary>
public sealed class CustomerSubscriptionInfo
{
    public int Id { get; init; }
    public string? PlanHandle { get; init; }
    public string? PlanName { get; init; }

    /// <summary>Subscription state as reported by Maxio (e.g. "active", "trialing", "canceled").</summary>
    public string? State { get; init; }

    public long? PriceInCents { get; init; }

    /// <summary>When the current paid period ends.</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>The next date Maxio will assess/bill the subscription.</summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? ActivatedAt { get; init; }
}
