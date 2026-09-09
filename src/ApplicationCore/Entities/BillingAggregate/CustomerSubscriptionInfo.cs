using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

/// <summary>
/// A shopper's subscription as recorded in the billing system of record.
/// </summary>
public sealed record CustomerSubscriptionInfo
{
    public int Id { get; init; }

    /// <summary>Billing-system state, e.g. "active", "trialing", "past_due".</summary>
    public string? State { get; init; }

    public string? PlanHandle { get; init; }

    public string? PlanName { get; init; }

    /// <summary>Recurring price in minor units (e.g. cents), as billed.</summary>
    public long? PriceInCents { get; init; }

    public string? Currency { get; init; }

    /// <summary>End of the current paid period.</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>When the subscription will next be assessed/billed.</summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }
}
