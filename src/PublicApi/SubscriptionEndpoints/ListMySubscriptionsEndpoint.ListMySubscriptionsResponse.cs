using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Response for listing the signed-in shopper's subscriptions.
/// </summary>
public class ListMySubscriptionsResponse : BaseResponse
{
    public ListMySubscriptionsResponse(System.Guid correlationId) : base(correlationId) { }

    public List<SubscriptionDto> Subscriptions { get; set; } = new List<SubscriptionDto>();
}
