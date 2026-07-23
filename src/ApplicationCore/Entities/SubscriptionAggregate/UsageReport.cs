using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The outcome of reporting usage: what was recorded, plus the running period-to-date balance.
/// The balance is optional because a failed read-back must not fail the whole operation — the usage
/// still stands and is reported with the total marked unavailable.
/// </summary>
public class UsageReport
{
    public UsageReport(UsageRecord record, decimal? periodToDateTotal, decimal unitPrice)
    {
        Guard.Against.Null(record, nameof(record));

        Record = record;
        PeriodToDateTotal = periodToDateTotal;
        UnitPrice = unitPrice;
    }

    public UsageRecord Record { get; }

    /// <summary>The accrued unit balance for the current period, or null if it could not be read back.</summary>
    public decimal? PeriodToDateTotal { get; }

    /// <summary>The price of one unit in major units.</summary>
    public decimal UnitPrice { get; }

    /// <summary>What the accrued usage will add to the next renewal invoice, or null if the total is unavailable.</summary>
    public decimal? EstimatedCharge => PeriodToDateTotal * UnitPrice;
}
