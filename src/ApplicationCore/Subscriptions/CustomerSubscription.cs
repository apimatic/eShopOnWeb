using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription belonging to a customer, projected from a Maxio subscription.
/// </summary>
public sealed class CustomerSubscription
{
    /// <summary>The Maxio subscription id.</summary>
    public required long Id { get; init; }

    /// <summary>Maxio subscription state (e.g. "active", "trialing", "canceled").</summary>
    public required string State { get; init; }

    public required string PlanHandle { get; init; }

    public required string PlanName { get; init; }

    public required int PriceInCents { get; init; }

    public decimal Price => PriceInCents / 100m;

    /// <summary>End of the current billing period, i.e. the next scheduled charge date.</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>When payment capture will next be attempted (usually tracks the period end).</summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>True when the subscription counts as live (not canceled/expired/failed).</summary>
    public bool IsActive =>
        !string.Equals(State, "canceled", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(State, "expired", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(State, "failed_to_create", StringComparison.OrdinalIgnoreCase);
}
