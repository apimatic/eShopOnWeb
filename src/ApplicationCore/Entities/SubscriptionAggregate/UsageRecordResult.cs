using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The outcome of recording usage (UC2). <see cref="PeriodToDateQuantity"/> is null when the
/// running-total read-back failed after a successful record — the usage still stands (§ UC2 failure scenarios).
/// </summary>
public sealed record UsageRecordResult(
    int QuantityRecorded,
    string? Memo,
    DateTimeOffset? RecordedAt,
    long? PeriodToDateQuantity);
