using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionErrorResponse : BaseResponse
{
    public SubscriptionErrorResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscriptionErrorResponse()
    {
    }

    public int StatusCode { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> Errors { get; set; } = new List<string>();

    /// <summary>Set on a 409 when the shopper already holds a different plan.</summary>
    public SubscriptionDto? ExistingSubscription { get; set; }
}
