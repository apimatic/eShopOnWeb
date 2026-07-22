using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The outcome of reporting usage: the record the provider accepted, plus the running
/// period-to-date balance where it could be read back.
/// </summary>
/// <remarks>
/// Per UC2, a failure to read the running total after a successful report must not fail the whole
/// operation — the usage stands and <see cref="PeriodToDateUnitsAvailable"/> reports the total as
/// unavailable instead.
/// </remarks>
public class UsageReport
{
    public UsageReport(UsageRecord record, int? periodToDateUnits, decimal? unitPrice)
    {
        Guard.Against.Null(record, nameof(record));

        Record = record;
        PeriodToDateUnits = periodToDateUnits;
        UnitPrice = unitPrice;
    }

    public UsageRecord Record { get; private set; }

    /// <summary>Units accrued in the current billing period, or <c>null</c> when the read-back failed.</summary>
    public int? PeriodToDateUnits { get; private set; }

    public bool PeriodToDateUnitsAvailable => PeriodToDateUnits.HasValue;

    /// <summary>Price per unit in whole currency units (dollars), when known.</summary>
    public decimal? UnitPrice { get; private set; }

    /// <summary>
    /// Period-to-date charge that will appear on the next renewal invoice, when both the running
    /// total and the unit price are known.
    /// </summary>
    public decimal? PeriodToDateCharge => PeriodToDateUnits.HasValue && UnitPrice.HasValue
        ? PeriodToDateUnits.Value * UnitPrice.Value
        : null;
}
