namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The outcome of recording one usage report against a subscription's metered component.
/// <see cref="PeriodToDateUnits"/> is null when the usage was recorded successfully but the
/// read-back of the running total failed — the usage still stands (§UC2 failure scenarios).
/// </summary>
public class UsageRecordResult
{
    public UsageRecordResult(long usageId, int quantity, string? memo, int? periodToDateUnits)
    {
        UsageId = usageId;
        Quantity = quantity;
        Memo = memo;
        PeriodToDateUnits = periodToDateUnits;
    }

    public long UsageId { get; }
    public int Quantity { get; }
    public string? Memo { get; }
    public int? PeriodToDateUnits { get; }
}
