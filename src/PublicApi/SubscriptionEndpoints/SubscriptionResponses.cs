using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionPlanListResponse : BaseResponse
{
    public SubscriptionPlanListResponse(IReadOnlyList<SubscriptionPlan> plans)
    {
        Plans = plans;
    }

    public IReadOnlyList<SubscriptionPlan> Plans { get; }
}

public sealed class UserSubscriptionListResponse : BaseResponse
{
    public UserSubscriptionListResponse(IReadOnlyList<UserSubscription> subscriptions)
    {
        Subscriptions = subscriptions;
    }

    public IReadOnlyList<UserSubscription> Subscriptions { get; }
}

public sealed class CreateSubscriptionRequest : BaseRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

public sealed class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId, UserSubscription subscription)
        : base(correlationId)
    {
        Subscription = subscription;
    }

    public UserSubscription Subscription { get; }
}
