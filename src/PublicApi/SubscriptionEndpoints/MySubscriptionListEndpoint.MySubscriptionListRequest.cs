using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionListRequest : BaseRequest
{
    public string? AuthenticatedUsername { get; set; }
}
