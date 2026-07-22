using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The running period-to-date picture of a subscription's metered consumption (UC2 steps 3–4).
/// </summary>
public class UsageSummary
{
    private UsageSummary(int subscriptionId,
        string componentHandle,
        bool totalAvailable,
        decimal periodToDateQuantity,
        decimal? unitPrice,
        DateTimeOffset? periodStartedAt,
        DateTimeOffset? nextInvoiceAt,
        IReadOnlyCollection<UsageRecord> records)
    {
        SubscriptionId = subscriptionId;
        ComponentHandle = componentHandle;
        TotalAvailable = totalAvailable;
        PeriodToDateQuantity = periodToDateQuantity;
        UnitPrice = unitPrice;
        PeriodStartedAt = periodStartedAt;
        NextInvoiceAt = nextInvoiceAt;
        Records = records;
    }

    public int SubscriptionId { get; }
    public string ComponentHandle { get; }

    /// <summary>
    /// False when the read-back of the running total failed after the usage itself was recorded.
    /// The recorded usage still stands — see UC2's failure scenarios.
    /// </summary>
    public bool TotalAvailable { get; }

    /// <summary>Units consumed since the start of the current billing period.</summary>
    public decimal PeriodToDateQuantity { get; }

    /// <summary>Price per unit in major units (dollars), when the component exposes one.</summary>
    public decimal? UnitPrice { get; }

    /// <summary>The accrued charge that will appear on the next renewal invoice.</summary>
    public decimal? EstimatedCharge => UnitPrice is null ? null : UnitPrice.Value * PeriodToDateQuantity;

    public DateTimeOffset? PeriodStartedAt { get; }

    /// <summary>When the accrued usage will be invoiced.</summary>
    public DateTimeOffset? NextInvoiceAt { get; }

    public IReadOnlyCollection<UsageRecord> Records { get; }

    public static UsageSummary Available(int subscriptionId,
        string componentHandle,
        decimal periodToDateQuantity,
        decimal? unitPrice,
        DateTimeOffset? periodStartedAt,
        DateTimeOffset? nextInvoiceAt,
        IReadOnlyCollection<UsageRecord> records) =>
        new(subscriptionId, componentHandle, true, periodToDateQuantity, unitPrice, periodStartedAt, nextInvoiceAt, records);

    /// <summary>The usage was recorded but the running total could not be read back (UC2).</summary>
    public static UsageSummary Unavailable(int subscriptionId, string componentHandle) =>
        new(subscriptionId, componentHandle, false, 0m, null, null, null, Array.Empty<UsageRecord>());
}
