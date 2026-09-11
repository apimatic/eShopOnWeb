using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public class PostSubscriptionResponse : BaseResponse
{
    public PostSubscriptionResponse() : base()
    {
    }

    public PostSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public string ProductName { get; set; } = string.Empty;
}
