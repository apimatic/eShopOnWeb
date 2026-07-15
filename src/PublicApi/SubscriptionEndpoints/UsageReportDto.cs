namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class UsageReportDto
{
    public int SubscriptionId { get; set; }
    public int RecordedQuantity { get; set; }
    public int? PeriodToDateTotal { get; set; }
    public bool TotalAvailable { get; set; }
}
