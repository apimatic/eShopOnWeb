namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansResponse
{
    public System.Collections.Generic.List<SubscriptionPlanDto> Plans { get; set; } = new();
}
