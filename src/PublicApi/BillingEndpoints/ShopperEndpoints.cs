using System;
using System.Globalization;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

internal static class CallerIdentity
{
    /// <summary>The caller's identity always comes from the JWT.</summary>
    public static string UserName(ClaimsPrincipal user)
    {
        var name = user.Identity?.Name ?? user.FindFirst(ClaimTypes.Name)?.Value;
        if (string.IsNullOrWhiteSpace(name))
            throw new BillingException(401, "The token does not identify a user.");
        return name;
    }
}

/// <summary>
/// Lists the plans a shopper can subscribe to
/// </summary>
public class ListPlansEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (BillingService billing) =>
            {
                var response = new ListPlansResponse();
                response.Plans.AddRange(await billing.ListPlansAsync());
                return Results.Ok(response);
            })
            .Produces<ListPlansResponse>()
            .WithTags("BillingEndpoints");
    }
}

/// <summary>
/// Subscribes the signed-in shopper to a plan
/// </summary>
public class SubscribeEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ClaimsPrincipal user, BillingService billing) =>
            {
                var userName = CallerIdentity.UserName(user);
                var subscription = await billing.SubscribeAsync(userName, request.ResolveHandle());
                return Results.Created($"api/subscriptions/{subscription.Id}",
                    new SubscriptionDetailResponse(request.CorrelationId()) { Subscription = subscription });
            })
            .Produces<SubscriptionDetailResponse>(StatusCodes.Status201Created)
            .WithTags("BillingEndpoints");
    }
}

/// <summary>
/// Shows one of the shopper's subscriptions
/// </summary>
public class GetSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, ClaimsPrincipal user, BillingService billing) =>
            {
                var subscription = await billing.GetSubscriptionAsync(CallerIdentity.UserName(user), subscriptionId);
                return Results.Ok(new SubscriptionDetailResponse { Subscription = subscription });
            })
            .Produces<SubscriptionDetailResponse>()
            .WithTags("BillingEndpoints");
    }
}

/// <summary>
/// Pause, resume, cancel, restart and switch the plan of a subscription
/// </summary>
public class SubscriptionLifecycleEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        const string scheme = JwtBearerDefaults.AuthenticationScheme;

        app.MapPost("api/subscriptions/{subscriptionId:int}/pause",
            [Authorize(AuthenticationSchemes = scheme)] async
            (int subscriptionId, ClaimsPrincipal user, BillingService billing) =>
                Results.Ok(new SubscriptionDetailResponse { Subscription = await billing.PauseAsync(CallerIdentity.UserName(user), subscriptionId) }))
            .Produces<SubscriptionDetailResponse>()
            .WithTags("BillingEndpoints");

        app.MapPost("api/subscriptions/{subscriptionId:int}/resume",
            [Authorize(AuthenticationSchemes = scheme)] async
            (int subscriptionId, ClaimsPrincipal user, BillingService billing) =>
                Results.Ok(new SubscriptionDetailResponse { Subscription = await billing.ResumeAsync(CallerIdentity.UserName(user), subscriptionId) }))
            .Produces<SubscriptionDetailResponse>()
            .WithTags("BillingEndpoints");

        app.MapPost("api/subscriptions/{subscriptionId:int}/cancel",
            [Authorize(AuthenticationSchemes = scheme)] async
            (int subscriptionId, ClaimsPrincipal user, BillingService billing) =>
                Results.Ok(new SubscriptionDetailResponse { Subscription = await billing.CancelAsync(CallerIdentity.UserName(user), subscriptionId) }))
            .Produces<SubscriptionDetailResponse>()
            .WithTags("BillingEndpoints");

        app.MapPost("api/subscriptions/{subscriptionId:int}/restart",
            [Authorize(AuthenticationSchemes = scheme)] async
            (int subscriptionId, ClaimsPrincipal user, BillingService billing) =>
                Results.Ok(new SubscriptionDetailResponse { Subscription = await billing.RestartAsync(CallerIdentity.UserName(user), subscriptionId) }))
            .Produces<SubscriptionDetailResponse>()
            .WithTags("BillingEndpoints");

        app.MapPost("api/subscriptions/{subscriptionId:int}/switch",
            [Authorize(AuthenticationSchemes = scheme)] async
            (int subscriptionId, SwitchPlanRequest request, ClaimsPrincipal user, BillingService billing) =>
                Results.Ok(new SubscriptionDetailResponse
                {
                    Subscription = await billing.SwitchPlanAsync(CallerIdentity.UserName(user), subscriptionId, request.ResolveHandle())
                }))
            .Produces<SubscriptionDetailResponse>()
            .WithTags("BillingEndpoints");
    }
}

/// <summary>
/// Shows the metered usage of a subscription on one day
/// </summary>
public class GetUsageEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId:int}/usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, string? date, ClaimsPrincipal user, BillingService billing) =>
            {
                if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                    throw BillingException.BadRequest("The 'date' query parameter is required and must look like 2026-01-31.");
                return Results.Ok(await billing.GetUsageAsync(CallerIdentity.UserName(user), subscriptionId, day));
            })
            .Produces<DailyUsageResponse>()
            .WithTags("BillingEndpoints");
    }
}
