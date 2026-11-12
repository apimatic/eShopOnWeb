using System;
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
    /// <summary>The caller's user name, taken from the JWT.</summary>
    public static string UserName(ClaimsPrincipal user)
        => user.Identity?.Name is { Length: > 0 } name
            ? name
            : throw new BillingException(401, "The token carries no user name.");
}

public class PlanChoiceRequest
{
    /// <summary>The plan's handle.</summary>
    public string? PlanHandle { get; set; }

    /// <summary>Alias of <see cref="PlanHandle"/>.</summary>
    public string? Handle { get; set; }
}

/// <summary>
/// A signed-in shopper subscribes to a plan
/// </summary>
public class SubscribeEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (PlanChoiceRequest? request, ClaimsPrincipal user, BillingService billing) =>
            {
                var subscription = await billing.SubscribeAsync(CallerIdentity.UserName(user), request?.PlanHandle ?? request?.Handle);
                return Results.Created($"api/subscriptions/{subscription.Id}", subscription);
            })
            .Produces<SubscriptionDto>(StatusCodes.Status201Created)
            .WithTags("BillingEndpoints");
    }
}

/// <summary>
/// A shopper sees one of their subscriptions
/// </summary>
public class GetSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int subscriptionId, ClaimsPrincipal user, BillingService billing) =>
                Results.Ok(await billing.GetSubscriptionAsync(CallerIdentity.UserName(user), subscriptionId)))
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");
    }
}

/// <summary>
/// A shopper pauses a subscription
/// </summary>
public class PauseSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/pause",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int subscriptionId, ClaimsPrincipal user, BillingService billing) =>
                Results.Ok(await billing.PauseAsync(CallerIdentity.UserName(user), subscriptionId)))
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");
    }
}

/// <summary>
/// A shopper resumes a paused subscription
/// </summary>
public class ResumeSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/resume",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int subscriptionId, ClaimsPrincipal user, BillingService billing) =>
                Results.Ok(await billing.ResumeAsync(CallerIdentity.UserName(user), subscriptionId)))
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");
    }
}

/// <summary>
/// A shopper cancels a subscription
/// </summary>
public class CancelSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/cancel",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int subscriptionId, ClaimsPrincipal user, BillingService billing) =>
                Results.Ok(await billing.CancelAsync(CallerIdentity.UserName(user), subscriptionId)))
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");
    }
}

/// <summary>
/// A shopper restarts a canceled subscription
/// </summary>
public class RestartSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/restart",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int subscriptionId, ClaimsPrincipal user, BillingService billing) =>
                Results.Ok(await billing.RestartAsync(CallerIdentity.UserName(user), subscriptionId)))
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");
    }
}

/// <summary>
/// A shopper switches a subscription to another plan
/// </summary>
public class SwitchPlanEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/switch",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int subscriptionId, PlanChoiceRequest? request, ClaimsPrincipal user, BillingService billing) =>
                Results.Ok(await billing.SwitchPlanAsync(CallerIdentity.UserName(user), subscriptionId, request?.PlanHandle ?? request?.Handle)))
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");
    }
}

/// <summary>
/// A shopper sees their metered usage on one day
/// </summary>
public class GetUsageEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId:int}/usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int subscriptionId, string? date, ClaimsPrincipal user, BillingService billing) =>
                Results.Ok(await billing.GetUsageAsync(CallerIdentity.UserName(user), subscriptionId, date)))
            .Produces<UsageDayDto>()
            .WithTags("BillingEndpoints");
    }
}
