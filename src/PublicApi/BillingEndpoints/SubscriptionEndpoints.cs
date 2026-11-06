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

internal static class Caller
{
    /// <summary>The signed-in shop user, from the JWT.</summary>
    public static string UserName(ClaimsPrincipal user) =>
        user.Identity?.Name is { Length: > 0 } name
            ? name
            : throw new BillingException(401, "The token does not identify a user.");
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
                var sub = await billing.SubscribeAsync(Caller.UserName(user), request.PlanHandle);
                return Results.Created($"api/subscriptions/{sub.Id}", sub);
            })
            .WithTags("SubscriptionEndpoints");
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
                Results.Ok(await billing.GetSubscriptionAsync(Caller.UserName(user), subscriptionId)))
            .WithTags("SubscriptionEndpoints");
    }
}

public class PauseSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/pause",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, ClaimsPrincipal user, BillingService billing) =>
                Results.Ok(await billing.PauseAsync(Caller.UserName(user), subscriptionId)))
            .WithTags("SubscriptionEndpoints");
    }
}

public class ResumeSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/resume",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, ClaimsPrincipal user, BillingService billing) =>
                Results.Ok(await billing.ResumeAsync(Caller.UserName(user), subscriptionId)))
            .WithTags("SubscriptionEndpoints");
    }
}

public class CancelSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/cancel",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, ClaimsPrincipal user, BillingService billing) =>
                Results.Ok(await billing.CancelAsync(Caller.UserName(user), subscriptionId)))
            .WithTags("SubscriptionEndpoints");
    }
}

public class RestartSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/restart",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, ClaimsPrincipal user, BillingService billing) =>
                Results.Ok(await billing.RestartAsync(Caller.UserName(user), subscriptionId)))
            .WithTags("SubscriptionEndpoints");
    }
}

/// <summary>
/// Switches a subscription (even a cancelled one) to another plan
/// </summary>
public class SwitchSubscriptionPlanEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/switch",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, SwitchPlanRequest request, ClaimsPrincipal user, BillingService billing) =>
                Results.Ok(await billing.SwitchPlanAsync(Caller.UserName(user), subscriptionId, request.PlanHandle)))
            .WithTags("SubscriptionEndpoints");
    }
}

/// <summary>
/// Metered usage recorded on a single day
/// </summary>
public class SubscriptionUsageEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId:int}/usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, string? date, ClaimsPrincipal user, BillingService billing) =>
            {
                if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                    throw new BillingException(400, "The date query parameter is required and must be given as yyyy-MM-dd.");
                return Results.Ok(await billing.GetUsageAsync(Caller.UserName(user), subscriptionId, day));
            })
            .WithTags("SubscriptionEndpoints");
    }
}

/// <summary>
/// Sets the date a subscriber is next billed (staff)
/// </summary>
public class SetNextBillingDateEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/admin/subscriptions/{subscriptionId:int}/next-billing-date",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, NextBillingDateRequest request, BillingService billing) =>
                Results.Ok(await billing.SetNextBillingDateAsync(subscriptionId, request.Date)))
            .WithTags("SubscriptionEndpoints");
    }
}
