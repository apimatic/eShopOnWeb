using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(System.Guid correlationId) : base(correlationId) { }
    public ListSubscriptionPlansResponse() { }
    public List<PlanDto> Plans { get; set; } = new();
}
