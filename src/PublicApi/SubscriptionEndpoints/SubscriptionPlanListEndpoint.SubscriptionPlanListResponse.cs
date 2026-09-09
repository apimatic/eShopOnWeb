namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanListResponse : BaseResponse
{
    public System.Collections.Generic.List<SubscriptionPlanDto> Plans { get; set; } = new();
}
