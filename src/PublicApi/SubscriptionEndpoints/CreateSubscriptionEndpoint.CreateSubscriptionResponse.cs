using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    public int SubscriptionId { get; set; }
    public int CustomerId { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset ActivatedAt { get; set; }
}
