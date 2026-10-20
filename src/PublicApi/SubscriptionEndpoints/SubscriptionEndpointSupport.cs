using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(System.Guid correlationId) : base(correlationId) { }

    public List<SubscriptionPlan> Plans { get; set; } = new();

    /// <summary>false when the plan listing was cut short by the page cap.</summary>
    public bool IsComplete { get; set; } = true;
}

public class SubscribeRequest : BaseRequest
{
    /// <summary>Handle of a plan returned by GET /api/subscription-plans. Required.</summary>
    public string? PlanHandle { get; set; }
}

public class SubscribeResponse : BaseResponse
{
    public SubscribeResponse(System.Guid correlationId) : base(correlationId) { }

    public SubscriptionView Subscription { get; set; } = new();
}

public class MySubscriptionsResponse : BaseResponse
{
    public MySubscriptionsResponse(System.Guid correlationId) : base(correlationId) { }

    public List<SubscriptionView> Subscriptions { get; set; } = new();
}

public class SubscriptionErrorResponse : BaseResponse
{
    public SubscriptionErrorResponse(System.Guid correlationId, string code, string message) : base(correlationId)
    {
        Code = code;
        Message = message;
    }

    public string Code { get; set; }
    public string Message { get; set; }
}

internal static class SubscriptionEndpointSupport
{
    /// <summary>The caller is whoever the JWT names; the token carries the user name only, so the stable id is looked up.</summary>
    public static async Task<ApplicationUser?> ResolveCallerAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
    {
        var name = principal.Identity?.Name ?? principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(name)) return null;
        return await userManager.FindByNameAsync(name);
    }

    public static IResult Error(System.Guid correlationId, SubscriptionFlowException ex)
        => Results.Json(new SubscriptionErrorResponse(correlationId, ex.Code, ex.Message), statusCode: ex.StatusCode);
}
