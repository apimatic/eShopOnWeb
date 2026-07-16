using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class RecordUsageResponse : BaseResponse
{
    public RecordUsageResponse(Guid correlationId) : base(correlationId) { }
    public RecordUsageResponse() { }

    public UsageRecord? Usage { get; set; }
}
