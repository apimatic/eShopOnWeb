using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListSubscriptionPlansResponse()
    {
    }

    public System.Collections.Generic.List<Microsoft.eShopWeb.PublicApi.Maxio.MaxioPlanDto> Plans { get; set; } = new();
}