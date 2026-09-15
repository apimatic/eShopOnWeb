namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionPlansRequest : BaseRequest
{
}

public sealed class MySubscriptionsRequest : BaseRequest
{
}

public sealed class SubscribeRequest : BaseRequest
{
    public string? PlanHandle { get; set; }
}
