using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequestDto : BaseRequest
{
    public string PlanHandle { get; set; } = null!;
}
