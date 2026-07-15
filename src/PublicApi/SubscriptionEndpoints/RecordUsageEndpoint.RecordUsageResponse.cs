using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class RecordUsageResponse : BaseResponse
{
    public RecordUsageResponse(Guid correlationId) : base(correlationId)
    {
    }

    public RecordUsageResponse()
    {
    }

    public bool Recorded { get; set; }
    public int? PeriodToDateUnits { get; set; }
    public bool PeriodToDateAvailable { get; set; }
}
