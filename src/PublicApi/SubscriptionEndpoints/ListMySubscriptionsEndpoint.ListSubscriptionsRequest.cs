using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionsRequest : BaseMessage
{
    public string? UserId { get; set; }

    public ListSubscriptionsRequest(string? userId = null)
    {
        UserId = userId;
    }

    public ListSubscriptionsRequest()
    {
    }
}
