using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The outcome of a UC2 usage report: what was recorded, plus the running period-to-date balance
/// when the provider could supply it.
/// </summary>
/// <remarks>
/// Per UC2, a failure to read the running total after a successful record does not fail the whole
/// operation — <see cref="PeriodToDateUnits"/> is simply left null and
/// <see cref="PeriodToDateAvailable"/> reports false.
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

    public UsageRecord Record { get; }

    /// <summary>Accumulated metered units for the current billing period, or null if unavailable.</summary>
    public int? PeriodToDateUnits { get; }

    /// <summary>The metered component's per-unit price in major currency units.</summary>
    public decimal? UnitPrice { get; }

    public bool PeriodToDateAvailable => PeriodToDateUnits.HasValue;

    /// <summary>
    /// The period-to-date amount that will appear on the next renewal invoice, when both the
    /// running balance and the unit price are known.
    /// </summary>
    public decimal? PeriodToDateAmount =>
        PeriodToDateUnits.HasValue && UnitPrice.HasValue ? PeriodToDateUnits.Value * UnitPrice.Value : null;
}
