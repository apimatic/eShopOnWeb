using System;
using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

internal static class Caller
{
    /// <summary>The signed-in user's name, from the JWT.</summary>
    public static string Name(ClaimsPrincipal user) =>
        user.FindFirst(ClaimTypes.Name)?.Value is { Length: > 0 } n ? n : throw new BillingException(401, "The token carries no user name.");
}

/// <summary>Lists the plans a shopper can subscribe to.</summary>
public class ListPlansEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (MaxioBillingService billing, CancellationToken ct) =>
            {
                var (plans, truncated) = await billing.ListPlansAsync(ct);
                return Results.Ok(new ListPlansResponse(Guid.NewGuid()) { Plans = plans, Truncated = truncated });
            })
            .Produces<ListPlansResponse>()
            .WithTags("BillingEndpoints");
    }
}

/// <summary>Subscribes the signed-in shopper to a plan.</summary>
public class SubscribeEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (SubscribeRequest request, ClaimsPrincipal user, MaxioBillingService billing, CancellationToken ct) =>
            {
                var sub = await billing.SubscribeAsync(Caller.Name(user), request.PlanHandle, ct);
                return Results.Created($"api/subscriptions/{sub.Id}", sub);
            })
            .Produces<SubscriptionDto>(201)
            .WithTags("BillingEndpoints");
    }
}

/// <summary>Shows one of the shopper's subscriptions, with whether it is paid.</summary>
public class GetSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (int subscriptionId, ClaimsPrincipal user, MaxioBillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.GetAsync(Caller.Name(user), subscriptionId, ct)))
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");
    }
}

public class PauseSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/pause",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (int subscriptionId, ClaimsPrincipal user, MaxioBillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.PauseAsync(Caller.Name(user), subscriptionId, ct)))
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");
    }
}

public class ResumeSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/resume",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (int subscriptionId, ClaimsPrincipal user, MaxioBillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.ResumeAsync(Caller.Name(user), subscriptionId, ct)))
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");
    }
}

public class CancelSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/cancel",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (int subscriptionId, ClaimsPrincipal user, MaxioBillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.CancelAsync(Caller.Name(user), subscriptionId, ct)))
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");
    }
}

public class RestartSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/restart",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (int subscriptionId, ClaimsPrincipal user, MaxioBillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.RestartAsync(Caller.Name(user), subscriptionId, ct)))
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");
    }
}

public class SwitchSubscriptionPlanEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/switch",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (int subscriptionId, SwitchPlanRequest request, ClaimsPrincipal user, MaxioBillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.SwitchAsync(Caller.Name(user), subscriptionId, request.PlanHandle, ct)))
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");
    }
}

/// <summary>Metered usage on one day.</summary>
public class SubscriptionUsageEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId:int}/usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (int subscriptionId, string? date, ClaimsPrincipal user, MaxioBillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.UsageAsync(Caller.Name(user), subscriptionId, date, ct)))
            .Produces<UsageResponse>()
            .WithTags("BillingEndpoints");
    }
}
