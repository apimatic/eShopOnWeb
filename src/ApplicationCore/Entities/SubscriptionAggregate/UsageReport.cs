namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The outcome of recording usage (UC2): the accepted record plus, when it could be read back, the
/// running period-to-date total.
/// </summary>
/// <remarks>
/// A failed read-back does not fail the operation — the usage stands and <see cref="Usage"/> is
/// simply <c>null</c> (UC2 failure scenario "read-back of the running total fails").
/// </remarks>
public class UsageReport
{
    public UsageReport(int subscriptionId, UsageRecord recorded, ComponentUsageSummary? usage)
    {
        SubscriptionId = subscriptionId;
        Recorded = recorded;
        Usage = usage;
    }

    public int SubscriptionId { get; }

    public UsageRecord Recorded { get; }

    /// <summary>The period-to-date total, or <c>null</c> when it could not be read back.</summary>
    public ComponentUsageSummary? Usage { get; }

    /// <summary>False when the running total was unavailable at the time of reporting.</summary>
    public bool IsTotalAvailable => Usage is not null;
}
