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

    public SubscriptionResultDto? Subscription { get; set; }
}

public class SubscriptionResultDto
{
    public int SubscriptionId { get; set; }
    public string State { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string ProductHandle { get; set; } = "";
    public long PriceInCents { get; set; }
    public decimal PriceInDollars { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}
