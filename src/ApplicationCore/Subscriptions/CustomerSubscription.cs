using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's enrollment in a <see cref="SubscriptionPlan"/>, as reported by the billing
/// system of record (Maxio). Read-only projection returned to the caller.
/// </summary>
public sealed class CustomerSubscription
{
    /// <summary>Billing-system subscription identifier.</summary>
    public long Id { get; init; }

    /// <summary>Lifecycle state reported by the billing system, e.g. "active", "trialing", "canceled".</summary>
    public string State { get; init; } = string.Empty;

    public string PlanName { get; init; } = string.Empty;

    public string PlanHandle { get; init; } = string.Empty;

    /// <summary>Recurring price of the enrolled plan, in the smallest currency unit.</summary>
    public long PriceInCents { get; init; }

    public string Currency { get; init; } = "USD";

    /// <summary>When the current paid period ends.</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>When the billing system will next assess/charge this subscription.</summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }
}
