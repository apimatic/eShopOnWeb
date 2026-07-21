using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>Outcome of recording one usage report against a subscription's metered component.</summary>
public class UsageRecordResult
{
    public UsageRecordResult(long usageId, decimal quantityRecorded, DateTimeOffset recordedAt, int? periodToDateUnits, bool periodToDateAvailable)
    {
        UsageId = usageId;
        QuantityRecorded = quantityRecorded;
        RecordedAt = recordedAt;
        PeriodToDateUnits = periodToDateUnits;
        PeriodToDateAvailable = periodToDateAvailable;
    }

    public long UsageId { get; }
    public decimal QuantityRecorded { get; }
    public DateTimeOffset RecordedAt { get; }

    /// <summary>The running total for the current billing period, or null when unavailable.</summary>
    public int? PeriodToDateUnits { get; }

    /// <summary>False when the usage was recorded successfully but the period-to-date read-back failed.</summary>
    public bool PeriodToDateAvailable { get; }
}
