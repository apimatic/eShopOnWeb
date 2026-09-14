using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionPlansResponse
{
    public IReadOnlyList<SubscriptionPlanDto> SubscriptionPlans { get; init; } = new List<SubscriptionPlanDto>();
}

public sealed class SubscriptionResponse
{
    public SubscriptionDto Subscription { get; init; } = new();
}

public sealed class MySubscriptionsResponse
{
    public IReadOnlyList<SubscriptionDto> Subscriptions { get; init; } = new List<SubscriptionDto>();
}

public sealed class CreateSubscriptionRequest
{
    public string ProductHandle { get; init; } = string.Empty;
}
