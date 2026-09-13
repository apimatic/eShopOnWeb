using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseMessage
{
    public string ProductHandle { get; set; } = "eshop-pro";
}
