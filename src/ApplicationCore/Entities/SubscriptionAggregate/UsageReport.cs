namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Result of recording usage (UC2). <see cref="PeriodToDateTotal"/> is the running balance for the
/// current billing period; per plan.md UC2's failure scenarios, a failure to read the total back
/// after a successful write must not fail the whole operation, so it is reported as unavailable
/// via <see cref="TotalAvailable"/> instead of throwing.
/// </summary>
public class UsageReport
{
    public UsageReport(int subscriptionId, int recordedQuantity, int? periodToDateTotal, bool totalAvailable)
    {
        SubscriptionId = subscriptionId;
        RecordedQuantity = recordedQuantity;
        PeriodToDateTotal = periodToDateTotal;
        TotalAvailable = totalAvailable;
    }

    public int SubscriptionId { get; }
    public int RecordedQuantity { get; }
    public int? PeriodToDateTotal { get; }
    public bool TotalAvailable { get; }
}
