using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public partial class CreateSubscriptionEndpoint
{
    public class CreateSubscriptionResponse : BaseResponse
    {
        public CreateSubscriptionResponse(Guid correlationId) : base(correlationId) { }

        public SubscriptionDto? Subscription { get; set; }
    }
}
