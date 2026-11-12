using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public int IntervalCount { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public bool TrialOffered { get; set; }
    public decimal SetupFee { get; set; }
    public bool RequiresPaymentMethod { get; set; }
}

public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId) { }
    public ListSubscriptionPlansResponse() { }

    public List<SubscriptionPlanDto> Plans { get; set; } = new();
    /// <summary>False when the plan list was cut short (page cap); the list may be missing plans.</summary>
    public bool IsComplete { get; set; }
}

public class SubscriptionDto
{
    public string PlanHandle { get; set; } = string.Empty;
    public string? PlanName { get; set; }
    public decimal? Price { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingDate { get; set; }
    public int? SubscriptionId { get; set; }

    public static SubscriptionDto From(SubscriptionView v) => new()
    {
        PlanHandle = v.PlanHandle,
        PlanName = v.PlanName,
        Price = v.Price,
        State = v.State,
        NextBillingDate = v.NextBillingDate,
        SubscriptionId = v.SubscriptionId
    };
}

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of a plan returned by GET /api/subscription-plans.</summary>
    public string? PlanHandle { get; set; }
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId) { }
    public CreateSubscriptionResponse() { }

    public SubscriptionDto? Subscription { get; set; }
    /// <summary>True when this call created it; false when the user already had it.</summary>
    public bool Created { get; set; }
}

public class ListMySubscriptionsResponse : BaseResponse
{
    public ListMySubscriptionsResponse(Guid correlationId) : base(correlationId) { }
    public ListMySubscriptionsResponse() { }

    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}

public class SubscriptionError
{
    public int StatusCode { get; set; }
    public string Message { get; set; } = string.Empty;
}

internal static class CallerIdentity
{
    /// <summary>Resolves the caller from the JWT (name claim -> ApplicationUser); null when unknown.</summary>
    public static async Task<BillingUser?> ResolveAsync(HttpContext http, UserManager<ApplicationUser> users)
    {
        var name = http.User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(name)) return null;
        var user = await users.FindByNameAsync(name);
        return user is null ? null : new BillingUser(user.Id, user.Email, user.UserName);
    }

    public static IResult Fail(int status, string message) =>
        Results.Json(new SubscriptionError { StatusCode = status, Message = message }, statusCode: status);
}
