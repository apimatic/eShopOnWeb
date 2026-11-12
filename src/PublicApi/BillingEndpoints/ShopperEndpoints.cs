using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

/// <summary>Request carrying what the endpoint lambda bound, plus the caller's token for cancellation.</summary>
public class BillingRequest : BaseRequest
{
    public int SubscriptionId { get; set; }
    public string? PlanHandle { get; set; }
    public string? Date { get; set; }
    public CancellationToken Cancellation { get; set; }
}

public class SubscribeRequestBody
{
    /// <summary>The handle of the plan to subscribe to.</summary>
    public string? PlanHandle { get; set; }
    public string? Handle { get; set; }
}

public class SwitchPlanRequestBody
{
    /// <summary>The handle of the plan to switch to.</summary>
    public string? PlanHandle { get; set; }
    public string? Handle { get; set; }
}

public class ListPlansResponse
{
    public List<PlanDto> Plans { get; set; } = new();
}

internal static class Caller
{
    public const string SCHEME = JwtBearerDefaults.AuthenticationScheme;

    /// <summary>The shopper's identity is the name in the caller's JWT, never a value from the request.</summary>
    public static string UserName(ClaimsPrincipal user)
    {
        var name = user.FindFirst(ClaimTypes.Name)?.Value ?? user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
            throw new BillingException(HttpStatusCode.Unauthorized, "The token does not identify a user.");
        return name;
    }
}

/// <summary>List the plans a shopper can subscribe to</summary>
public class ListPlansEndpoint : IEndpoint<IResult, BillingRequest, MaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/plans",
            [Authorize(AuthenticationSchemes = Caller.SCHEME)] async
            (MaxioBillingService billing, CancellationToken ct) =>
                await HandleAsync(new BillingRequest { Cancellation = ct }, billing))
            .Produces<ListPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(BillingRequest request, MaxioBillingService billing)
    {
        var plans = await billing.ListPlansAsync(request.Cancellation);
        return Results.Ok(new ListPlansResponse { Plans = new List<PlanDto>(plans) });
    }
}

/// <summary>Subscribe the signed-in shopper to a plan, without card details</summary>
public class SubscribeEndpoint : IEndpoint<IResult, BillingRequest, ClaimsPrincipal, MaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = Caller.SCHEME)] async
            (SubscribeRequestBody body, ClaimsPrincipal user, MaxioBillingService billing, CancellationToken ct) =>
                await HandleAsync(new BillingRequest { PlanHandle = body?.PlanHandle ?? body?.Handle, Cancellation = ct }, user, billing))
            .Produces<SubscriptionDto>(StatusCodes.Status201Created)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(BillingRequest request, ClaimsPrincipal user, MaxioBillingService billing)
    {
        var subscription = await billing.SubscribeAsync(Caller.UserName(user), request.PlanHandle, request.Cancellation);
        return Results.Created($"api/subscriptions/{subscription.Id}", subscription);
    }
}

/// <summary>Show one of the shopper's subscriptions</summary>
public class GetSubscriptionEndpoint : IEndpoint<IResult, BillingRequest, ClaimsPrincipal, MaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId}",
            [Authorize(AuthenticationSchemes = Caller.SCHEME)] async
            (int subscriptionId, ClaimsPrincipal user, MaxioBillingService billing, CancellationToken ct) =>
                await HandleAsync(new BillingRequest { SubscriptionId = subscriptionId, Cancellation = ct }, user, billing))
            .Produces<SubscriptionDto>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(BillingRequest request, ClaimsPrincipal user, MaxioBillingService billing) =>
        Results.Ok(await billing.GetAsync(Caller.UserName(user), request.SubscriptionId, request.Cancellation));
}

/// <summary>Pause a subscription</summary>
public class PauseSubscriptionEndpoint : IEndpoint<IResult, BillingRequest, ClaimsPrincipal, MaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/pause",
            [Authorize(AuthenticationSchemes = Caller.SCHEME)] async
            (int subscriptionId, ClaimsPrincipal user, MaxioBillingService billing, CancellationToken ct) =>
                await HandleAsync(new BillingRequest { SubscriptionId = subscriptionId, Cancellation = ct }, user, billing))
            .Produces<SubscriptionDto>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(BillingRequest request, ClaimsPrincipal user, MaxioBillingService billing) =>
        Results.Ok(await billing.PauseAsync(Caller.UserName(user), request.SubscriptionId, request.Cancellation));
}

/// <summary>Resume a paused subscription</summary>
public class ResumeSubscriptionEndpoint : IEndpoint<IResult, BillingRequest, ClaimsPrincipal, MaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/resume",
            [Authorize(AuthenticationSchemes = Caller.SCHEME)] async
            (int subscriptionId, ClaimsPrincipal user, MaxioBillingService billing, CancellationToken ct) =>
                await HandleAsync(new BillingRequest { SubscriptionId = subscriptionId, Cancellation = ct }, user, billing))
            .Produces<SubscriptionDto>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(BillingRequest request, ClaimsPrincipal user, MaxioBillingService billing) =>
        Results.Ok(await billing.ResumeAsync(Caller.UserName(user), request.SubscriptionId, request.Cancellation));
}

/// <summary>Cancel a subscription</summary>
public class CancelSubscriptionEndpoint : IEndpoint<IResult, BillingRequest, ClaimsPrincipal, MaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/cancel",
            [Authorize(AuthenticationSchemes = Caller.SCHEME)] async
            (int subscriptionId, ClaimsPrincipal user, MaxioBillingService billing, CancellationToken ct) =>
                await HandleAsync(new BillingRequest { SubscriptionId = subscriptionId, Cancellation = ct }, user, billing))
            .Produces<SubscriptionDto>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(BillingRequest request, ClaimsPrincipal user, MaxioBillingService billing) =>
        Results.Ok(await billing.CancelAsync(Caller.UserName(user), request.SubscriptionId, request.Cancellation));
}

/// <summary>Restart a cancelled subscription</summary>
public class RestartSubscriptionEndpoint : IEndpoint<IResult, BillingRequest, ClaimsPrincipal, MaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/restart",
            [Authorize(AuthenticationSchemes = Caller.SCHEME)] async
            (int subscriptionId, ClaimsPrincipal user, MaxioBillingService billing, CancellationToken ct) =>
                await HandleAsync(new BillingRequest { SubscriptionId = subscriptionId, Cancellation = ct }, user, billing))
            .Produces<SubscriptionDto>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(BillingRequest request, ClaimsPrincipal user, MaxioBillingService billing) =>
        Results.Ok(await billing.RestartAsync(Caller.UserName(user), request.SubscriptionId, request.Cancellation));
}

/// <summary>Switch a subscription to another plan</summary>
public class SwitchPlanEndpoint : IEndpoint<IResult, BillingRequest, ClaimsPrincipal, MaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/switch",
            [Authorize(AuthenticationSchemes = Caller.SCHEME)] async
            (int subscriptionId, SwitchPlanRequestBody body, ClaimsPrincipal user, MaxioBillingService billing, CancellationToken ct) =>
                await HandleAsync(new BillingRequest
                {
                    SubscriptionId = subscriptionId,
                    PlanHandle = body?.PlanHandle ?? body?.Handle,
                    Cancellation = ct,
                }, user, billing))
            .Produces<SubscriptionDto>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(BillingRequest request, ClaimsPrincipal user, MaxioBillingService billing) =>
        Results.Ok(await billing.SwitchPlanAsync(Caller.UserName(user), request.SubscriptionId, request.PlanHandle, request.Cancellation));
}

/// <summary>Metered usage on one day</summary>
public class SubscriptionUsageEndpoint : IEndpoint<IResult, BillingRequest, ClaimsPrincipal, MaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId}/usage",
            [Authorize(AuthenticationSchemes = Caller.SCHEME)] async
            (int subscriptionId, string? date, ClaimsPrincipal user, MaxioBillingService billing, CancellationToken ct) =>
                await HandleAsync(new BillingRequest { SubscriptionId = subscriptionId, Date = date, Cancellation = ct }, user, billing))
            .Produces<UsageDto>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(BillingRequest request, ClaimsPrincipal user, MaxioBillingService billing)
    {
        if (!DateOnly.TryParseExact(request.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            throw new BillingException(HttpStatusCode.BadRequest, "The 'date' query value is required, as YYYY-MM-DD.");
        return Results.Ok(await billing.UsageForDayAsync(Caller.UserName(user), request.SubscriptionId, day, request.Cancellation));
    }
}
