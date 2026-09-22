using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's subscription as recorded in Maxio (the system of record).
/// <see cref="State"/> is the Maxio state wire value (e.g. "active", "trialing"), or "unknown"
/// when the provider returned no state — never defaulted to a success value.
/// </summary>
public record CustomerSubscription
{
    public int? Id { get; init; }

    /// <summary>The app-supplied subscription reference (deterministic per user + plan).</summary>
    public string? Reference { get; init; }

    public string? PlanHandle { get; init; }
    public string? PlanName { get; init; }

    /// <summary>Maxio subscription state wire value, or "unknown" when absent.</summary>
    public required string State { get; init; }

    public long? PriceInCents { get; init; }

    /// <summary>The next date Maxio will assess/bill the subscription.</summary>
    public DateTimeOffset? NextBillingDate { get; init; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
}
