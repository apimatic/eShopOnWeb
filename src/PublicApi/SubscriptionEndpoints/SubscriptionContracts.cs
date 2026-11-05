using System;
using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId) { }
    public ListSubscriptionPlansResponse() { }

    public List<PlanDto> Plans { get; set; } = new();
    /// <summary>True when the plan list hit its page cap and may be incomplete.</summary>
    public bool IsPartial { get; set; }
}

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to (one of the handles returned by GET api/subscription-plans).</summary>
    public string? ProductHandle { get; set; }
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId) { }
    public CreateSubscriptionResponse() { }

    public SubscriptionDto? Subscription { get; set; }
}

public class ListMySubscriptionsResponse : BaseResponse
{
    public ListMySubscriptionsResponse(Guid correlationId) : base(correlationId) { }
    public ListMySubscriptionsResponse() { }

    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}

internal static class SubscriptionEndpointHelpers
{
    /// <summary>The caller identity comes only from the JWT.</summary>
    public static string? UserName(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.Name);

    /// <summary>One mapping from application failure to HTTP result, shared by all subscription endpoints.</summary>
    public static IResult Failure(SubscriptionException ex) =>
        Results.Text(new BlazorShared.Models.ErrorDetails { StatusCode = ex.StatusCode, Message = ex.Message }.ToString(),
            "application/json", System.Text.Encoding.UTF8, ex.StatusCode);
}
