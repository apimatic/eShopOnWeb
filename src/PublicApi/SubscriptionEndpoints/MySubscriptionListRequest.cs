using Microsoft.eShopWeb.PublicApi;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionListRequest : BaseRequest
{
    public string UserName { get; set; } = string.Empty;
}
