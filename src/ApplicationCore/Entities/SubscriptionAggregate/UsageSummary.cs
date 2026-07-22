using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The outcome of recording usage: the entry that was accepted, plus the running period-to-date
/// total if it could be read back.
/// <para>
/// Reading the total back is deliberately allowed to fail on its own. Once the provider has
/// accepted the usage the charge stands, so a failed read-back is reported as
/// <see cref="IsPeriodTotalAvailable"/> = <c>false</c> rather than failing the whole operation
/// (UC2 failure scenario: "the usage stands; report success with the total marked unavailable").
/// </para>
/// </summary>
public class UsageSummary
{
    private UsageSummary(UsageRecord recorded, decimal? periodToDateQuantity, decimal? unitPrice)
    {
        Guard.Against.Null(recorded, nameof(recorded));

        Recorded = recorded;
        PeriodToDateQuantity = periodToDateQuantity;
        UnitPrice = unitPrice;
    }

    /// <summary>The usage entry the provider accepted.</summary>
    public UsageRecord Recorded { get; }

    /// <summary>Total units recorded so far in the current billing period, or null if it could not be read.</summary>
    public decimal? PeriodToDateQuantity { get; }

    /// <summary>The per-unit price in whole currency units (dollars), or null if it could not be read.</summary>
    public decimal? UnitPrice { get; }

    public DateTimeOffset? PeriodStartedAt { get; private init; }

    public DateTimeOffset? PeriodEndsAt { get; private init; }

    /// <summary>False when the running total could not be read back after a successful record.</summary>
    public bool IsPeriodTotalAvailable => PeriodToDateQuantity.HasValue;

    /// <summary>
    /// The amount the recorded usage will add to the next renewal invoice, or null when either the
    /// running total or the unit price is unavailable.
    /// </summary>
    public decimal? PeriodToDateAmount =>
        PeriodToDateQuantity.HasValue && UnitPrice.HasValue
            ? decimal.Round(PeriodToDateQuantity.Value * UnitPrice.Value, 2, MidpointRounding.AwayFromZero)
            : null;

    public static UsageSummary WithTotal(UsageRecord recorded,
        decimal periodToDateQuantity,
        decimal? unitPrice,
        DateTimeOffset? periodStartedAt,
        DateTimeOffset? periodEndsAt) =>
        new(recorded, periodToDateQuantity, unitPrice)
        {
            PeriodStartedAt = periodStartedAt,
            PeriodEndsAt = periodEndsAt
        };

    /// <summary>The usage was recorded, but the period-to-date total could not be read back.</summary>
    public static UsageSummary WithoutTotal(UsageRecord recorded) => new(recorded, null, null);
}
