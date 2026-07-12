using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The result of recording a unit of metered usage (UC2). <see cref="PeriodToDateTotal"/> is
/// <c>null</c> when the usage was recorded successfully but the read-back of the running total
/// failed — the usage still stands (§ UC2 failure scenarios: report success, mark the total
/// unavailable rather than failing the whole operation).
/// </summary>
public class UsageRecord
{
    public double Quantity { get; }
    public string? Memo { get; }
    public DateTimeOffset RecordedAt { get; }
    public int? PeriodToDateTotal { get; }

    public UsageRecord(double quantity, string? memo, DateTimeOffset recordedAt, int? periodToDateTotal)
    {
        Quantity = quantity;
        Memo = memo;
        RecordedAt = recordedAt;
        PeriodToDateTotal = periodToDateTotal;
    }
}
