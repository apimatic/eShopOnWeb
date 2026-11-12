using System;
using System.Globalization;
using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest
{
    /// <summary>Handle of the plan to subscribe to.</summary>
    public string PlanHandle { get; set; } = string.Empty;
}

public class SwitchPlanRequest
{
    /// <summary>Handle of the plan to switch to.</summary>
    public string PlanHandle { get; set; } = string.Empty;
}

/// <summary>
/// Subscribes the signed-in shopper to a plan (no card details needed)
/// </summary>
public class SubscribeEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ClaimsPrincipal user, IBillingService billing, CancellationToken ct) =>
            {
                var created = await billing.SubscribeAsync(user.RequireUserName(), request.PlanHandle, ct);
                return Results.Created($"api/subscriptions/{created.Id}", created);
            })
            .Produces<SubscriptionDto>(StatusCodes.Status201Created)
            .WithTags("SubscriptionEndpoints");
    }
}

/// <summary>
/// Shows one of the shopper's subscriptions, including whether it is paid
/// </summary>
public class GetSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, ClaimsPrincipal user, IBillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.GetSubscriptionAsync(user.RequireUserName(), subscriptionId, ct)))
            .Produces<SubscriptionDto>()
            .WithTags("SubscriptionEndpoints");
    }
}

/// <summary>
/// Pauses one of the shopper's subscriptions
/// </summary>
public class PauseSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/pause",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, ClaimsPrincipal user, IBillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.PauseAsync(user.RequireUserName(), subscriptionId, ct)))
            .Produces<SubscriptionDto>()
            .WithTags("SubscriptionEndpoints");
    }
}

/// <summary>
/// Resumes one of the shopper's paused subscriptions
/// </summary>
public class ResumeSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/resume",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, ClaimsPrincipal user, IBillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.ResumeAsync(user.RequireUserName(), subscriptionId, ct)))
            .Produces<SubscriptionDto>()
            .WithTags("SubscriptionEndpoints");
    }
}

/// <summary>
/// Cancels one of the shopper's subscriptions
/// </summary>
public class CancelSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/cancel",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, ClaimsPrincipal user, IBillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.CancelAsync(user.RequireUserName(), subscriptionId, ct)))
            .Produces<SubscriptionDto>()
            .WithTags("SubscriptionEndpoints");
    }
}

/// <summary>
/// Restarts one of the shopper's canceled subscriptions
/// </summary>
public class RestartSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/restart",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, ClaimsPrincipal user, IBillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.RestartAsync(user.RequireUserName(), subscriptionId, ct)))
            .Produces<SubscriptionDto>()
            .WithTags("SubscriptionEndpoints");
    }
}

/// <summary>
/// Switches one of the shopper's subscriptions (including a canceled one) to another plan
/// </summary>
public class SwitchPlanEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/switch",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, SwitchPlanRequest request, ClaimsPrincipal user, IBillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.SwitchPlanAsync(user.RequireUserName(), subscriptionId, request.PlanHandle, ct)))
            .Produces<SubscriptionDto>()
            .WithTags("SubscriptionEndpoints");
    }
}

/// <summary>
/// Shows the shopper's metered usage on one day
/// </summary>
public class GetUsageEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId:int}/usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, string? date, ClaimsPrincipal user, IBillingService billing, CancellationToken ct) =>
            {
                if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                {
                    throw BillingException.BadRequest("Query parameter 'date' is required, in the form YYYY-MM-DD.");
                }
                return Results.Ok(await billing.GetUsageAsync(user.RequireUserName(), subscriptionId, day, ct));
            })
            .Produces<UsageDto>()
            .WithTags("SubscriptionEndpoints");
    }
}
