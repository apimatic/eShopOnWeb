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

// The shopper is always the caller identified by the JWT; none of these routes takes a user from the request.

/// <summary>
/// Subscribes the signed-in shopper to a plan (no card details)
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ClaimsPrincipal user, SubscriptionService subscriptions, CancellationToken ct) =>
            {
                var subscription = await subscriptions.SubscribeAsync(user, request.PlanHandle ?? request.Handle, ct);
                return Results.Created($"api/subscriptions/{subscription.Id}", subscription);
            })
            .Produces<SubscriptionDto>(StatusCodes.Status201Created)
            .WithTags("BillingEndpoints");
    }
}

public class SubscribeRequest
{
    /// <summary>Handle of the plan to subscribe to.</summary>
    public string? PlanHandle { get; set; }
    /// <summary>Alias of <see cref="PlanHandle"/>.</summary>
    public string? Handle { get; set; }
}

/// <summary>
/// Shows one of the signed-in shopper's subscriptions
/// </summary>
public class GetSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, ClaimsPrincipal user, SubscriptionService subscriptions, CancellationToken ct) =>
                Results.Ok(await subscriptions.GetAsync(user, subscriptionId, ct)))
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");
    }
}

/// <summary>
/// Pauses a subscription
/// </summary>
public class PauseSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/pause",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, ClaimsPrincipal user, SubscriptionService subscriptions, CancellationToken ct) =>
                Results.Ok(await subscriptions.PauseAsync(user, subscriptionId, ct)))
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");
    }
}

/// <summary>
/// Resumes a paused subscription
/// </summary>
public class ResumeSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/resume",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, ClaimsPrincipal user, SubscriptionService subscriptions, CancellationToken ct) =>
                Results.Ok(await subscriptions.ResumeAsync(user, subscriptionId, ct)))
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");
    }
}

/// <summary>
/// Cancels a subscription
/// </summary>
public class CancelSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/cancel",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, ClaimsPrincipal user, SubscriptionService subscriptions, CancellationToken ct) =>
                Results.Ok(await subscriptions.CancelAsync(user, subscriptionId, ct)))
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");
    }
}

/// <summary>
/// Restarts a cancelled subscription
/// </summary>
public class RestartSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/restart",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, ClaimsPrincipal user, SubscriptionService subscriptions, CancellationToken ct) =>
                Results.Ok(await subscriptions.RestartAsync(user, subscriptionId, ct)))
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");
    }
}

/// <summary>
/// Switches a subscription to another plan
/// </summary>
public class SwitchSubscriptionPlanEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/switch",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, SwitchPlanRequest request, ClaimsPrincipal user, SubscriptionService subscriptions, CancellationToken ct) =>
                Results.Ok(await subscriptions.SwitchPlanAsync(user, subscriptionId, request.PlanHandle ?? request.Handle, ct)))
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");
    }
}

public class SwitchPlanRequest
{
    /// <summary>Handle of the new plan.</summary>
    public string? PlanHandle { get; set; }
    /// <summary>Alias of <see cref="PlanHandle"/>.</summary>
    public string? Handle { get; set; }
}

/// <summary>
/// Shows a subscription's metered usage on one day
/// </summary>
public class GetSubscriptionUsageEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId:int}/usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, string? date, ClaimsPrincipal user, SubscriptionService subscriptions, CancellationToken ct) =>
                Results.Ok(await subscriptions.GetUsageAsync(user, subscriptionId, date, ct)))
            .Produces<SubscriptionUsageDto>()
            .WithTags("BillingEndpoints");
    }
}
