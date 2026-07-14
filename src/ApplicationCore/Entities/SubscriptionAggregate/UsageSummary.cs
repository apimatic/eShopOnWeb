namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public sealed class UsageSummary
{
    public UsageSummary(int subscriptionId, string componentHandle, int quantityRecorded, string? memo, int? periodToDateTotal)
    {
        SubscriptionId = subscriptionId;
        ComponentHandle = componentHandle;
        QuantityRecorded = quantityRecorded;
        Memo = memo;
        PeriodToDateTotal = periodToDateTotal;
    }

    public int SubscriptionId { get; }
    public string ComponentHandle { get; }
    public int QuantityRecorded { get; }
    public string? Memo { get; }

    /// <summary>
    /// Running period-to-date total units. Null when usage was recorded successfully but the
    /// read-back of the running total failed (the recorded usage still stands, §UC2).
    /// </summary>
    public int? PeriodToDateTotal { get; }
}
