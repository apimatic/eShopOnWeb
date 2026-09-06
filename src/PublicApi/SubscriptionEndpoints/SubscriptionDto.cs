using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = "";
    public DateTime? NextBillingAt { get; set; }
    public decimal? Balance { get; set; }
    public string? ProductHandle { get; set; }
}

public class CreateSubscriptionRequest : BaseRequest
{
    public string ProductHandle { get; set; } = "";
}

public class CreateSubscriptionResponse : BaseResponse
{
    public SubscriptionDto? Subscription { get; set; }
}

public class ListSubscriptionsResponse : BaseResponse
{
    public SubscriptionDto[] Subscriptions { get; set; } = Array.Empty<SubscriptionDto>();
}
