using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi.Subscriptions.Models;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions.Endpoints;

public class ListMySubscriptionsResponse : BaseResponse
{
    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}
