namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The outcome of reporting usage: what was recorded, plus the running period-to-date balance
/// that will be billed on the next renewal invoice (UC2).
/// </summary>
public class UsageReport
{
    public UsageReport(UsageRecord record, decimal? periodToDateBalance)
    {
        Record = record;
        PeriodToDateBalance = periodToDateBalance;
    }

    public UsageRecord Record { get; }

    /// <summary>
    /// Units accrued so far this period, or <c>null</c> when the read-back failed. A failed
    /// read-back does not fail the report — the usage still stands (UC2 failure scenarios).
    /// </summary>
    public decimal? PeriodToDateBalance { get; }

    /// <summary>
    /// True when the running total could not be read back and is therefore unavailable.
    /// </summary>
    public bool BalanceUnavailable => PeriodToDateBalance is null;
}
