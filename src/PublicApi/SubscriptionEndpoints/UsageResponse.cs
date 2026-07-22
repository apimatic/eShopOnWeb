using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Shared by the record-usage and read-usage endpoints (UC2).</summary>
public class UsageResponse : BaseResponse
{
    public UsageResponse(Guid correlationId) : base(correlationId)
    {
    }

    public UsageResponse()
    {
    }

    public UsageSummaryDto Usage { get; set; } = new();
}
