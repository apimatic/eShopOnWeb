using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>The handle of the plan, as listed by GET api/plans.</summary>
    public string? PlanHandle { get; set; }
    /// <summary>Alias of PlanHandle.</summary>
    public string? Handle { get; set; }
    public string? ResolvedHandle => PlanHandle ?? Handle;
}

/// <summary>
/// Subscribes the signed-in shopper to a plan (no card details)
/// </summary>
public class SubscribeEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest? request, ClaimsPrincipal user, IMaxioBillingService billing, CancellationToken ct) =>
            {
                var sub = await billing.SubscribeAsync(BillingEndpointSupport.UserName(user), request?.ResolvedHandle ?? string.Empty, ct);
                return Results.Created($"api/subscriptions/{sub.Id}", sub);
            })
            .Produces<SubscriptionDto>(StatusCodes.Status201Created)
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
            (int subscriptionId, ClaimsPrincipal user, IMaxioBillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.GetSubscriptionAsync(BillingEndpointSupport.UserName(user), subscriptionId, ct)))
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
            (int subscriptionId, ClaimsPrincipal user, IMaxioBillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.PauseAsync(BillingEndpointSupport.UserName(user), subscriptionId, ct)))
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
            (int subscriptionId, ClaimsPrincipal user, IMaxioBillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.ResumeAsync(BillingEndpointSupport.UserName(user), subscriptionId, ct)))
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
            (int subscriptionId, ClaimsPrincipal user, IMaxioBillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.CancelAsync(BillingEndpointSupport.UserName(user), subscriptionId, ct)))
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
            (int subscriptionId, ClaimsPrincipal user, IMaxioBillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.RestartAsync(BillingEndpointSupport.UserName(user), subscriptionId, ct)))
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
            (int subscriptionId, SubscribeRequest? request, ClaimsPrincipal user, IMaxioBillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.SwitchPlanAsync(BillingEndpointSupport.UserName(user), subscriptionId, request?.ResolvedHandle ?? string.Empty, ct)))
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");
    }
}

/// <summary>
/// Metered usage of a subscription on a single day
/// </summary>
public class GetSubscriptionUsageEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId:int}/usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, string? date, ClaimsPrincipal user, IMaxioBillingService billing, CancellationToken ct) =>
            {
                var day = BillingEndpointSupport.ParseDate(date, "The date");
                return Results.Ok(await billing.GetUsageAsync(BillingEndpointSupport.UserName(user), subscriptionId, day, ct));
            })
            .Produces<UsageDto>()
            .WithTags("BillingEndpoints");
    }
}
