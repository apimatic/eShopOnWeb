namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The outcome of reporting usage, including the running period-to-date totals when they could be read.
/// </summary>
public class UsageReportDto
{
    public UsageRecordDto Record { get; set; }
    public int SubscriptionId { get; set; }
    public decimal? PeriodToDateQuantity { get; set; }
    public int? CurrentUnitBalance { get; set; }

    /// <summary>Price of a single unit, in whole currency units.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>What the period-to-date usage will add to the next renewal invoice.</summary>
    public decimal? PeriodToDateCharge { get; set; }

    /// <summary>False when the totals could not be read back; the usage itself still stands.</summary>
    public bool TotalsAvailable { get; set; }
}
