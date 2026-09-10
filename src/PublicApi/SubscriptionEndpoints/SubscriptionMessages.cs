using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.PublicApi.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Request body for <c>POST /api/subscriptions</c>.</summary>
public class SubscribeRequest : BaseRequest
{
    /// <summary>API handle of the plan to subscribe to (a product in the configured family).</summary>
    public string? PlanHandle { get; set; }
}

/// <summary>Response for <c>GET /api/subscription-plans</c>.</summary>
public class SubscriptionPlansResponse : BaseResponse
{
    public SubscriptionPlansResponse(Guid correlationId) : base(correlationId) { }
    public SubscriptionPlansResponse() { }

    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}

/// <summary>Response for <c>POST /api/subscriptions</c>.</summary>
public class SubscribeResponse : BaseResponse
{
    public SubscribeResponse(Guid correlationId) : base(correlationId) { }
    public SubscribeResponse() { }

    public SubscriptionDto? Subscription { get; set; }

    /// <summary>True when the caller already had a live subscription to this plan (idempotent no-op).</summary>
    public bool AlreadySubscribed { get; set; }
}

/// <summary>Response for <c>GET /api/my-subscriptions</c>.</summary>
public class MySubscriptionsResponse : BaseResponse
{
    public MySubscriptionsResponse(Guid correlationId) : base(correlationId) { }
    public MySubscriptionsResponse() { }

    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}

/// <summary>Maps the integration's failure type onto an HTTP problem response.</summary>
internal static class SubscriptionResults
{
    public static IResult Problem(MaxioApiException ex) =>
        Results.Problem(detail: ex.Message, statusCode: ex.StatusCode, title: "Subscription billing error");
}
