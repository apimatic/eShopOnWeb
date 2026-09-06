using BlazorShared;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class EmptyRequest : BaseRequest
{
    public string UserId { get; set; } = "";
}
