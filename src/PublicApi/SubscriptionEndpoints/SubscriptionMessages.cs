using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansRequest : BaseRequest
{
}

public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId) { }
    public ListSubscriptionPlansResponse() { }

    public List<SubscriptionPlanDto> Plans { get; set; } = new();
    /// <summary>false when the plan list was cut short and may be missing plans.</summary>
    public bool IsComplete { get; set; }
}

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to; optional when a default plan is configured.</summary>
    public string? PlanHandle { get; set; }
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId) { }
    public CreateSubscriptionResponse() { }

    public SubscriptionDto? Subscription { get; set; }
    /// <summary>true when the user already had a subscription and it is returned unchanged.</summary>
    public bool AlreadySubscribed { get; set; }
}

public class ListMySubscriptionsRequest : BaseRequest
{
}

public class ListMySubscriptionsResponse : BaseResponse
{
    public ListMySubscriptionsResponse(Guid correlationId) : base(correlationId) { }
    public ListMySubscriptionsResponse() { }

    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}

internal static class BillingResults
{
    public static IResult From(BillingException ex, Guid correlationId) =>
        Results.Json(new { error = ex.Message, correlationId }, statusCode: ex.Status);
}
