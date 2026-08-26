using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsResponse : BaseResponse
{
    public List<CustomerSubscriptionDto> Subscriptions { get; set; } = new();
}
