using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class UsageRecordResultDto
{
    public long UsageId { get; set; }
    public double Quantity { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public int? PeriodToDateBalance { get; set; }
}
