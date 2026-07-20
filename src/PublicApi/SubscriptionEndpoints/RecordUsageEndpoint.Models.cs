using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class RecordUsageRequest : BaseRequest
{
    public int SubscriptionId { get; set; }
    public decimal Quantity { get; set; }
    public string? Memo { get; set; }

    /// <summary>Resolved server-side: the caller's own reference, or null for an Administrator acting on any subscription.</summary>
    public string? OwnerReference { get; set; }
}

public class RecordUsageResponse : BaseResponse
{
    public RecordUsageResponse(Guid correlationId) : base(correlationId)
    {
    }

    public RecordUsageResponse()
    {
    }

    public decimal Quantity { get; set; }
    public int? PeriodToDateBalance { get; set; }
}
