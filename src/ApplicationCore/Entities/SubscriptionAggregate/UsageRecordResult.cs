namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The outcome of recording metered usage (UC2).
/// </summary>
public sealed class UsageRecordResult
{
    public UsageRecordResult(long usageId,
        long subscriptionId,
        string componentHandle,
        int quantity,
        string? memo,
        int? periodToDateUnits,
        decimal? unitPrice)
    {
        UsageId = usageId;
        SubscriptionId = subscriptionId;
        ComponentHandle = componentHandle;
        Quantity = quantity;
        Memo = memo;
        PeriodToDateUnits = periodToDateUnits;
        UnitPrice = unitPrice;
    }

    public long UsageId { get; }

    public long SubscriptionId { get; }

    public string ComponentHandle { get; }

    /// <summary>Units recorded by this call.</summary>
    public int Quantity { get; }

    public string? Memo { get; }

    /// <summary>
    /// Running total for the current billing period. Null when the read-back failed: UC2 requires
    /// the recorded usage to stand and the total to be reported as unavailable, rather than
    /// failing the whole operation.
    /// </summary>
    public int? PeriodToDateUnits { get; }

    /// <summary>Price per unit in major units, when known.</summary>
    public decimal? UnitPrice { get; }

    /// <summary>True when the period-to-date read-back succeeded.</summary>
    public bool PeriodToDateAvailable => PeriodToDateUnits.HasValue;

    /// <summary>Estimated period-to-date charge in major units; null when either input is unknown.</summary>
    public decimal? PeriodToDateEstimatedCharge =>
        PeriodToDateUnits.HasValue && UnitPrice.HasValue ? PeriodToDateUnits.Value * UnitPrice.Value : null;
}
