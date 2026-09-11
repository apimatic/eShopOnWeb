using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public class GetMySubscriptionsResponse : BaseResponse
{
    public GetMySubscriptionsResponse() : base()
    {
    }

    public GetMySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public List<UserSubscriptionDto> Subscriptions { get; set; } = new();
}

public class UserSubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
}
