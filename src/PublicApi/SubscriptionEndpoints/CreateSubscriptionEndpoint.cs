using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    public string ProductHandle { get; set; } = "";
}

public class CreateSubscriptionResponse : BaseResponse
{
    public SubscriptionDto Subscription { get; set; } = new();
}

public class SubscriptionDto
{
    public string Id { get; set; } = "";
    public string State { get; set; } = "";
    public string ProductHandle { get; set; } = "";
    public decimal Price { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public string CustomerReference { get; set; } = "";
}
