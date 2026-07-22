namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The outcome of reporting usage: what was recorded, plus the running period-to-date balance.
/// The balance is optional — per UC2, a failed read-back must not fail the whole operation.
/// </summary>
public class UsageReport
{
    public UsageReport(UsageRecord recorded, decimal? periodToDateTotal, decimal? unitPrice)
    {
        Recorded = recorded;
        PeriodToDateTotal = periodToDateTotal;
        UnitPrice = unitPrice;
    }

    public UsageRecord Recorded { get; }

    /// <summary>
    /// Units accrued so far in the current billing period, or <c>null</c> when the read-back
    /// was unavailable.
    /// </summary>
    public decimal? PeriodToDateTotal { get; }

    /// <summary>Price per unit in major units (dollars), when known.</summary>
    public decimal? UnitPrice { get; }

    /// <summary>
    /// The amount the accrued usage will add to the next renewal invoice, when both the
    /// running total and the unit price are known.
    /// </summary>
    public decimal? EstimatedPeriodToDateCharge =>
        PeriodToDateTotal.HasValue && UnitPrice.HasValue
            ? PeriodToDateTotal.Value * UnitPrice.Value
            : null;
}
