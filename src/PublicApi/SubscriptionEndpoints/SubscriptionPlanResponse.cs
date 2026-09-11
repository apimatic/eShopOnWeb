namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanResponse : BaseResponse
{
    public SubscriptionPlanDto[] Plans { get; set; } = System.Array.Empty<SubscriptionPlanDto>();
}
