using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpointResponse : BaseResponse
{
    public CreateSubscriptionEndpointResponse() : base() { }
    public CreateSubscriptionEndpointResponse(System.Guid correlationId) : base(correlationId) { }

    public CreateSubscriptionResult Subscription { get; set; } = new();
}
