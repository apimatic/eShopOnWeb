using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Subscriptions;

public class UsageRecordResult
{
    public UsageRecordResult(long usageId, double quantity, DateTimeOffset recordedAt, int? periodToDateBalance)
    {
        UsageId = usageId;
        Quantity = quantity;
        RecordedAt = recordedAt;
        PeriodToDateBalance = periodToDateBalance;
    }

    public long UsageId { get; }
    public double Quantity { get; }
    public DateTimeOffset RecordedAt { get; }

    // Null when the read-back of the running total failed after a successful usage record;
    // the usage still stands (see UC2 failure scenarios) — the caller must treat null as "unavailable", not zero.
    public int? PeriodToDateBalance { get; }
}
