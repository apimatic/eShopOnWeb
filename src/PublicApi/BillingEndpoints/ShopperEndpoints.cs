using System;
using System.Globalization;
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

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

/// <summary>Identity helpers shared by the billing endpoints: the caller always comes from the JWT.</summary>
internal static class Caller
{
    public static string UserName(ClaimsPrincipal user)
    {
        var name = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
            throw new BillingException(401, "The token does not identify a user.");
        return name;
    }
}

public class PlanRequestBody
{
    public string? PlanHandle { get; set; }

    /// <summary>Alias of <see cref="PlanHandle"/>.</summary>
    public string? Handle { get; set; }

    internal string? Resolve() => PlanHandle ?? Handle;
}

/// <summary>Lists the plans a shopper can subscribe to.</summary>
public class ListPlansEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (BillingService billing, CancellationToken ct) =>
                Results.Ok(new { plans = await billing.ListPlansAsync(ct) }))
            .Produces(StatusCodes.Status200OK)
            .WithTags("BillingEndpoints");
    }
}

/// <summary>Subscribes the signed-in shopper to a plan (no card details).</summary>
public class SubscribeEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlanRequestBody request, ClaimsPrincipal user, BillingService billing, CancellationToken ct) =>
            {
                var subscription = await billing.SubscribeAsync(Caller.UserName(user), request.Resolve(), ct);
                return Results.Created($"api/subscriptions/{subscription.Id}", subscription);
            })
            .Produces<SubscriptionDto>(StatusCodes.Status201Created)
            .WithTags("BillingEndpoints");
    }
}

/// <summary>Shows one of the shopper's subscriptions.</summary>
public class GetSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, ClaimsPrincipal user, BillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.GetSubscriptionAsync(Caller.UserName(user), subscriptionId, ct)))
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");
    }
}

/// <summary>Pauses and resumes a subscription.</summary>
public class PauseResumeSubscriptionEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/pause",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, ClaimsPrincipal user, BillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.PauseAsync(Caller.UserName(user), subscriptionId, ct)))
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");

        app.MapPost("api/subscriptions/{subscriptionId:int}/resume",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, ClaimsPrincipal user, BillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.ResumeAsync(Caller.UserName(user), subscriptionId, ct)))
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");
    }
}

/// <summary>Cancels and restarts a subscription.</summary>
public class CancelRestartSubscriptionEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/cancel",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, ClaimsPrincipal user, BillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.CancelAsync(Caller.UserName(user), subscriptionId, ct)))
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");

        app.MapPost("api/subscriptions/{subscriptionId:int}/restart",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, ClaimsPrincipal user, BillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.RestartAsync(Caller.UserName(user), subscriptionId, ct)))
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");
    }
}

/// <summary>Switches a subscription to another plan.</summary>
public class SwitchPlanEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/switch",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, PlanRequestBody request, ClaimsPrincipal user, BillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.SwitchPlanAsync(Caller.UserName(user), subscriptionId, request.Resolve(), ct)))
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");
    }
}

/// <summary>Shows the metered usage recorded on a single day.</summary>
public class DailyUsageEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId:int}/usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, string? date, ClaimsPrincipal user, BillingService billing, CancellationToken ct) =>
            {
                if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                    throw new BillingException(400, "The 'date' query parameter is required, formatted YYYY-MM-DD.");
                return Results.Ok(await billing.GetUsageAsync(Caller.UserName(user), subscriptionId, day, ct));
            })
            .Produces<DailyUsageDto>()
            .WithTags("BillingEndpoints");
    }
}
