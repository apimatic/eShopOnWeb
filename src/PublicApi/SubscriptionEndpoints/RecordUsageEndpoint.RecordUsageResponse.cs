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

    public UsageRecordDto Usage { get; set; } = null!;
    public UsagePeriodSummaryDto Summary { get; set; } = null!;
}
