namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The outcome of reporting usage: what was recorded, plus the running period-to-date balance.
/// The balance is optional because a failed read-back must not fail the whole operation —
/// the usage still stands.
/// </summary>
public class UsageReport
{
    public UsageReport(UsageRecord recordedUsage, decimal? periodToDateTotal)
    {
        RecordedUsage = recordedUsage;
        PeriodToDateTotal = periodToDateTotal;
    }

    public UsageRecord RecordedUsage { get; private set; }

    /// <summary>Units accrued against the component so far this billing period, or null if unavailable.</summary>
    public decimal? PeriodToDateTotal { get; private set; }

    public bool IsPeriodToDateTotalAvailable => PeriodToDateTotal.HasValue;
}
