using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class UsageReportDto
{
    public long UsageId { get; set; }
    public int SubscriptionId { get; set; }
    public string ComponentHandle { get; set; }
    public decimal Quantity { get; set; }
    public string? Memo { get; set; }
    public DateTimeOffset? RecordedAt { get; set; }

    /// <summary>Units accrued so far this period, or null when the total could not be read back.</summary>
    public int? PeriodToDateUnits { get; set; }
    public bool PeriodToDateUnitsAvailable { get; set; }
    public decimal? UnitPrice { get; set; }

    /// <summary>What the accrued units will add to the next renewal invoice.</summary>
    public decimal? PeriodToDateCharge { get; set; }
}
