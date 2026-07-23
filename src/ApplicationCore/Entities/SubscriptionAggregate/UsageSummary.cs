namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The pay-as-you-go position on a subscription: the usage record just accepted (when the summary was
/// produced by a report) plus the running period-to-date total.
/// Per plan.md UC2, a failed read-back of the total must not fail the whole operation — in that case
/// <see cref="PeriodToDateQuantity"/> is null and <see cref="TotalUnavailable"/> is true, while any
/// recorded usage still stands.
/// </summary>
public class UsageSummary
{
    public UsageSummary(string componentHandle, UsageRecord? record, int? periodToDateQuantity, decimal? unitPrice)
    {
        ComponentHandle = componentHandle;
        Record = record;
        PeriodToDateQuantity = periodToDateQuantity;
        UnitPrice = unitPrice;
    }

    public string ComponentHandle { get; }

    /// <summary>The record accepted by this operation; null when the summary is a read-only query.</summary>
    public UsageRecord? Record { get; }

    /// <summary>Total units recorded in the current billing period, or null when the read-back failed.</summary>
    public int? PeriodToDateQuantity { get; }

    /// <summary>Price per unit in dollars, when the component publishes one.</summary>
    public decimal? UnitPrice { get; }

    /// <summary>Period-to-date charge in dollars; null when either the total or the unit price is unknown.</summary>
    public decimal? PeriodToDateCharge =>
        PeriodToDateQuantity.HasValue && UnitPrice.HasValue
            ? PeriodToDateQuantity.Value * UnitPrice.Value
            : null;

    public bool TotalUnavailable => PeriodToDateQuantity is null;
}
