namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The outcome of recording usage against a subscription's metered component. <see cref="PeriodToDateTotal"/>
/// is null when the usage was recorded successfully but the read-back of the running total failed
/// (UC2 failure scenarios: report success with the total marked unavailable rather than failing the whole operation).
/// </summary>
public class UsageRecordResult
{
    public UsageRecordResult(long usageId, double quantityRecorded, int? periodToDateTotal)
    {
        UsageId = usageId;
        QuantityRecorded = quantityRecorded;
        PeriodToDateTotal = periodToDateTotal;
    }

    public long UsageId { get; }
    public double QuantityRecorded { get; }
    public int? PeriodToDateTotal { get; }
}
