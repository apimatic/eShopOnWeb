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

    public int QuantityRecorded { get; set; }

    // Null when usage recorded successfully but reading back the period-to-date total failed (§ UC2).
    public int? PeriodToDateTotal { get; set; }
}
