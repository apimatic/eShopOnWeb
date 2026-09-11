using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.MaxioIntegration;

public class SubscriptionListResponse : BaseResponse
{
    public SubscriptionListResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscriptionListResponse()
    {
    }

    public bool IsSuccess { get; set; }
    public List<SubscriptionDto> Subscriptions { get; set; } = new();
    public string? ErrorMessage { get; set; }
}
