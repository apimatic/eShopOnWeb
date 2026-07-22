using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A read-only view of what a subscription has consumed so far in the current billing period, for the
/// storefront's usage panel (UC2).
/// </summary>
public class UsageSummary
{
    public UsageSummary(int subscriptionId,
        string componentHandle,
        string? unitName,
        decimal unitPrice,
        decimal? periodToDateQuantity,
        int? currentUnitBalance,
        DateTimeOffset? periodStartedAt,
        DateTimeOffset? periodEndsAt)
    {
        SubscriptionId = subscriptionId;
        ComponentHandle = componentHandle;
        UnitName = unitName;
        UnitPrice = unitPrice;
        PeriodToDateQuantity = periodToDateQuantity;
        CurrentUnitBalance = currentUnitBalance;
        PeriodStartedAt = periodStartedAt;
        PeriodEndsAt = periodEndsAt;
    }

    public int SubscriptionId { get; }

    public string ComponentHandle { get; }

    public string? UnitName { get; }

    /// <summary>Price of a single unit, in whole currency units.</summary>
    public decimal UnitPrice { get; }

    /// <summary>Units consumed since the current period started. Null when it could not be read.</summary>
    public decimal? PeriodToDateQuantity { get; }

    /// <summary>The provider's running unit balance. Null when it could not be read.</summary>
    public int? CurrentUnitBalance { get; }

    public DateTimeOffset? PeriodStartedAt { get; }

    public DateTimeOffset? PeriodEndsAt { get; }

    /// <summary>What the period-to-date usage will add to the next renewal invoice, when known.</summary>
    public decimal? EstimatedCharge => PeriodToDateQuantity.HasValue
        ? decimal.Round(PeriodToDateQuantity.Value * UnitPrice, 2)
        : null;
}
