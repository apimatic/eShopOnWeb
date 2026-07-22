using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The outcome of a UC2 usage report: the receipt for the units just recorded, plus the running
/// period-to-date total. Per UC2's failure scenarios, a failed read-back of the running total does not
/// fail the operation — <see cref="PeriodToDateUnits"/> is simply left null.
/// </summary>
public sealed record UsageSummary
{
    public UsageSummary(UsageReceipt receipt, int? periodToDateUnits, decimal unitPrice)
    {
        Receipt = receipt ?? throw new ArgumentNullException(nameof(receipt));
        PeriodToDateUnits = periodToDateUnits;
        UnitPrice = unitPrice;
    }

    public UsageReceipt Receipt { get; init; }

    /// <summary>Running billable unit balance for the current period, or null when the read-back was unavailable.</summary>
    public int? PeriodToDateUnits { get; init; }

    /// <summary>Price per unit in dollars, so the caller can show the accruing charge.</summary>
    public decimal UnitPrice { get; init; }

    /// <summary>Period-to-date charge in dollars, or null when the running total is unavailable.</summary>
    public decimal? PeriodToDateCharge => PeriodToDateUnits.HasValue ? PeriodToDateUnits.Value * UnitPrice : null;
}
