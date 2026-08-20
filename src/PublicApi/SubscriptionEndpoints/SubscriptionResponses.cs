using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed record ListSubscriptionPlansResponse(IReadOnlyList<SubscriptionPlanDto> Plans);

public sealed record CreateSubscriptionRequest
{
    public string ProductHandle { get; init; } = string.Empty;
}

public sealed record CreateSubscriptionResponse(SubscriptionDto Subscription);

public sealed record ListMySubscriptionsResponse(IReadOnlyList<SubscriptionDto> Subscriptions);
