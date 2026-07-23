namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The outcome of reporting usage (UC2). The <see cref="Record"/> always reflects units that were
/// accepted by the provider; <see cref="Summary"/> is null when the period-to-date read-back failed,
/// because a failed read-back must not fail the whole operation (UC2 failure scenarios).
/// </summary>
public class UsageReport
{
    public UsageReport(UsageRecord record, UsageSummary? summary)
    {
        Record = record;
        Summary = summary;
    }

    public UsageRecord Record { get; private set; }

    public UsageSummary? Summary { get; private set; }

    /// <summary>False when the running total could not be read back; the recorded usage still stands.</summary>
    public bool IsSummaryAvailable => Summary is not null;
}
