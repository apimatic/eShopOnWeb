using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Response for listing the subscription plans available to the signed-in shopper.
/// </summary>
public class GetSubscriptionPlansResponse : BaseResponse
{
    public GetSubscriptionPlansResponse(System.Guid correlationId) : base(correlationId) { }

    public List<SubscriptionPlanDto> Plans { get; set; } = new List<SubscriptionPlanDto>();
}
