namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The outcome of reporting usage: the accepted record plus, when the read-back succeeded, the running
/// period-to-date totals. A failed read-back never fails the operation — the usage still stands and the
/// totals are simply reported as unavailable (UC2 failure scenario).
/// </summary>
public class UsageReport
{
    public UsageReport(UsageRecord record,
        int subscriptionId,
        decimal? periodToDateQuantity,
        int? currentUnitBalance,
        decimal unitPrice,
        bool totalsAvailable)
    {
        Record = record;
        SubscriptionId = subscriptionId;
        PeriodToDateQuantity = periodToDateQuantity;
        CurrentUnitBalance = currentUnitBalance;
        UnitPrice = unitPrice;
        TotalsAvailable = totalsAvailable;
    }

    public UsageRecord Record { get; }

    public int SubscriptionId { get; }

    /// <summary>Sum of every usage reported since the current billing period started. Null when unavailable.</summary>
    public decimal? PeriodToDateQuantity { get; }

    /// <summary>The provider's running unit balance for the component. Null when unavailable.</summary>
    public int? CurrentUnitBalance { get; }

    /// <summary>Price per unit, in whole currency units.</summary>
    public decimal UnitPrice { get; }

    /// <summary>False when the read-back of the running totals failed after the usage was accepted.</summary>
    public bool TotalsAvailable { get; }

    /// <summary>Amount the period-to-date usage will add to the next renewal invoice, when known.</summary>
    public decimal? PeriodToDateCharge => PeriodToDateQuantity.HasValue
        ? decimal.Round(PeriodToDateQuantity.Value * UnitPrice, 2)
        : null;
}
