using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId) { }
    public ListSubscriptionPlansResponse() { }

    public List<SubscriptionPlan> Plans { get; set; } = new();

    /// <summary>True when the plan list was cut short and may be incomplete.</summary>
    public bool Truncated { get; set; }
}

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of one of the plans returned by GET /api/subscription-plans.</summary>
    public string? PlanHandle { get; set; }
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId) { }
    public CreateSubscriptionResponse() { }

    public SubscriptionInfo? Subscription { get; set; }

    /// <summary>True when an earlier request had already created this subscription (nothing new was created).</summary>
    public bool AlreadySubscribed { get; set; }
}

public class ListMySubscriptionsResponse : BaseResponse
{
    public ListMySubscriptionsResponse(Guid correlationId) : base(correlationId) { }
    public ListMySubscriptionsResponse() { }

    public List<SubscriptionInfo> Subscriptions { get; set; } = new();

    /// <summary>True when the list was cut short and may be incomplete.</summary>
    public bool Truncated { get; set; }
}
