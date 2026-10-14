using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsResponse : BaseResponse
{
    public ListMySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListMySubscriptionsResponse()
    {
    }

    public List<SubscriptionDto> Subscriptions { get; set; } = new List<SubscriptionDto>();

    /// <summary>Subscribe requests whose outcome Maxio has not confirmed yet.</summary>
    public List<PendingSubscriptionDto> Pending { get; set; } = new List<PendingSubscriptionDto>();
}

public class PendingSubscriptionDto
{
    public string Reference { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset RequestedAt { get; set; }
}
