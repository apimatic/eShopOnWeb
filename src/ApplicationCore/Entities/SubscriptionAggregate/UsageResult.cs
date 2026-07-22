namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The outcome of recording metered usage (UC2): the quantity just recorded plus the running
/// period-to-date unit balance read back from the provider. <see cref="PeriodToDateTotal"/> is
/// <c>null</c> when the read-back failed after a successful record (§UC2 failure scenario) — the
/// usage still stands.
/// </summary>
public class UsageResult
{
    public UsageResult(int recordedQuantity, decimal? periodToDateTotal, decimal? unitPrice)
    {
        RecordedQuantity = recordedQuantity;
        PeriodToDateTotal = periodToDateTotal;
        UnitPrice = unitPrice;
    }

    /// <summary>The quantity of units recorded by this call.</summary>
    public int RecordedQuantity { get; }

    /// <summary>The running unit balance for the metered component this billing period, if known.</summary>
    public decimal? PeriodToDateTotal { get; }

    /// <summary>The per-unit price in whole currency units (dollars), e.g. 0.01.</summary>
    public decimal? UnitPrice { get; }

    /// <summary>The estimated charge accrued so far this period, when both total and price are known.</summary>
    public decimal? EstimatedPeriodCharge =>
        PeriodToDateTotal.HasValue && UnitPrice.HasValue ? PeriodToDateTotal.Value * UnitPrice.Value : null;
}
