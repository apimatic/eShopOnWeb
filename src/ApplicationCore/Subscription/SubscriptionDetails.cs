using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscription;

/// <summary>
/// The state of a subscription in the billing system of record (Maxio Advanced Billing).
/// </summary>
public sealed class SubscriptionDetails
{
    /// <summary>The billing system's subscription id.</summary>
    public int Id { get; init; }

    /// <summary>The app-provided reference stored on the billing subscription.</summary>
    public string Reference { get; init; } = string.Empty;

    /// <summary>Subscription state, e.g. "active", "trialing", "canceled".</summary>
    public string State { get; init; } = string.Empty;

    public string PlanHandle { get; init; } = string.Empty;

    public string PlanName { get; init; } = string.Empty;

    /// <summary>Recurring price in major currency units.</summary>
    public decimal Price { get; init; }

    public string? Currency { get; init; }

    /// <summary>UTC timestamp of the next scheduled billing, when known.</summary>
    public DateTimeOffset? NextBillingDate { get; init; }

    public DateTimeOffset? ActivatedAt { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }
}
