using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class UsageReportDto
{
    public long UsageId { get; set; }
    public int SubscriptionId { get; set; }
    public string? ComponentHandle { get; set; }
    public decimal Quantity { get; set; }
    public string? Memo { get; set; }
    public DateTimeOffset? RecordedAt { get; set; }
    public int? PeriodToDateUnits { get; set; }
    public bool PeriodToDateAvailable { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal? PeriodToDateAmount { get; set; }
}
