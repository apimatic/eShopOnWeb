using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangeRequest : BaseRequest
{
    public int SubscriptionId { get; set; }
    public Guid PreviewToken { get; set; }
}
