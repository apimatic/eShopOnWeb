using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetMySubscriptionsResponse : BaseResponse
{
    public GetMySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public GetMySubscriptionsResponse()
    {
    }

    public List<UserSubscriptionDto> Subscriptions { get; set; } = new List<UserSubscriptionDto>();
}

public class UserSubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTime? ActivatedAt { get; set; }
    public DateTime? NextBillingDate { get; set; }
}
