using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the signed-in shopper to a plan
/// </summary>
public class SubscribeEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ClaimsPrincipal user, BillingService billing, CancellationToken ct) =>
            {
                return await HandleAsync(request, user, billing, ct);
            })
            .Produces<SubscriptionResponse>(StatusCodes.Status201Created)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ClaimsPrincipal user, BillingService billing, CancellationToken ct)
    {
        if (request == null) throw BillingException.BadRequest("A request body is required.");
        var dto = await billing.SubscribeAsync(user.Identity?.Name, request.PlanHandle, ct);
        return Results.Created($"api/subscriptions/{dto.Id}", new SubscriptionResponse(request.CorrelationId()) { Subscription = dto });
    }
}

/// <summary>
/// Gets one of the shopper's subscriptions
/// </summary>
public class GetSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (long subscriptionId, ClaimsPrincipal user, BillingService billing, CancellationToken ct) =>
            {
                return await HandleAsync(subscriptionId, user, billing, ct);
            })
            .Produces<SubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(long subscriptionId, ClaimsPrincipal user, BillingService billing, CancellationToken ct)
    {
        var dto = await billing.GetAsync(user.Identity?.Name, subscriptionId, ct);
        return Results.Ok(new SubscriptionResponse(Guid.NewGuid()) { Subscription = dto });
    }
}

/// <summary>
/// Pauses (puts on hold) a shopper's subscription
/// </summary>
public class PauseSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/pause",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (long subscriptionId, ClaimsPrincipal user, BillingService billing, CancellationToken ct) =>
            {
                return await HandleAsync(subscriptionId, user, billing, ct);
            })
            .Produces<SubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(long subscriptionId, ClaimsPrincipal user, BillingService billing, CancellationToken ct)
    {
        var dto = await billing.PauseAsync(user.Identity?.Name, subscriptionId, ct);
        return Results.Ok(new SubscriptionResponse(Guid.NewGuid()) { Subscription = dto });
    }
}

/// <summary>
/// Resumes a paused subscription
/// </summary>
public class ResumeSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/resume",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (long subscriptionId, ClaimsPrincipal user, BillingService billing, CancellationToken ct) =>
            {
                return await HandleAsync(subscriptionId, user, billing, ct);
            })
            .Produces<SubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(long subscriptionId, ClaimsPrincipal user, BillingService billing, CancellationToken ct)
    {
        var dto = await billing.ResumeAsync(user.Identity?.Name, subscriptionId, ct);
        return Results.Ok(new SubscriptionResponse(Guid.NewGuid()) { Subscription = dto });
    }
}

/// <summary>
/// Cancels a subscription
/// </summary>
public class CancelSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/cancel",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (long subscriptionId, ClaimsPrincipal user, BillingService billing, CancellationToken ct) =>
            {
                return await HandleAsync(subscriptionId, user, billing, ct);
            })
            .Produces<SubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(long subscriptionId, ClaimsPrincipal user, BillingService billing, CancellationToken ct)
    {
        var dto = await billing.CancelAsync(user.Identity?.Name, subscriptionId, ct);
        return Results.Ok(new SubscriptionResponse(Guid.NewGuid()) { Subscription = dto });
    }
}

/// <summary>
/// Restarts a cancelled subscription
/// </summary>
public class RestartSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/restart",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (long subscriptionId, ClaimsPrincipal user, BillingService billing, CancellationToken ct) =>
            {
                return await HandleAsync(subscriptionId, user, billing, ct);
            })
            .Produces<SubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(long subscriptionId, ClaimsPrincipal user, BillingService billing, CancellationToken ct)
    {
        var dto = await billing.RestartAsync(user.Identity?.Name, subscriptionId, ct);
        return Results.Ok(new SubscriptionResponse(Guid.NewGuid()) { Subscription = dto });
    }
}

/// <summary>
/// Switches a subscription to another plan
/// </summary>
public class SwitchPlanEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/switch",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (long subscriptionId, SwitchPlanRequest request, ClaimsPrincipal user, BillingService billing, CancellationToken ct) =>
            {
                return await HandleAsync(subscriptionId, request, user, billing, ct);
            })
            .Produces<SubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(long subscriptionId, SwitchPlanRequest request, ClaimsPrincipal user, BillingService billing, CancellationToken ct)
    {
        if (request == null) throw BillingException.BadRequest("A request body is required.");
        var dto = await billing.SwitchPlanAsync(user.Identity?.Name, subscriptionId, request.PlanHandle, ct);
        return Results.Ok(new SubscriptionResponse(request.CorrelationId()) { Subscription = dto });
    }
}

/// <summary>
/// Gets the metered usage recorded on a subscription for one day
/// </summary>
public class GetUsageEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId}/usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (long subscriptionId, string? date, ClaimsPrincipal user, BillingService billing, CancellationToken ct) =>
            {
                return await HandleAsync(subscriptionId, date, user, billing, ct);
            })
            .Produces<UsageResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(long subscriptionId, string? date, ClaimsPrincipal user, BillingService billing, CancellationToken ct)
    {
        return Results.Ok(await billing.GetUsageAsync(user.Identity?.Name, subscriptionId, date, Guid.NewGuid(), ct));
    }
}
