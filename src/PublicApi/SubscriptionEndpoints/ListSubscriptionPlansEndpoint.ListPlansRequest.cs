using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListPlansRequest : BaseMessage
{
    public string? UserId { get; set; }

    public ListPlansRequest(string? userId = null)
    {
        UserId = userId;
    }

    public ListPlansRequest()
    {
    }
}
