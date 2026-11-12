using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

// Shopper endpoints: the caller is the JWT's name claim. Staff endpoints: the administrator role from the JWT.
// Refusals are MaxioBillingException, turned into a 4xx/5xx with a reason by ExceptionMiddleware.

internal static class MaxioAuth
{
    public const string Scheme = JwtBearerDefaults.AuthenticationScheme;
    public const string Admin = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS;
    public static string? Name(ClaimsPrincipal p) => p.FindFirstValue(ClaimTypes.Name);
}

/// <summary>Lists the plans a shopper can subscribe to</summary>
public class ListPlansEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/plans",
            [Authorize(AuthenticationSchemes = MaxioAuth.Scheme)] async (MaxioBillingService s, CancellationToken ct) => await HandleAsync(s, ct))
            .WithTags("MaxioEndpoints");
    }

    public async Task<IResult> HandleAsync(MaxioBillingService s, CancellationToken ct) => Results.Ok(await s.ListPlans(ct));
}

/// <summary>Subscribes the signed-in shopper to a plan</summary>
public class SubscribeEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = MaxioAuth.Scheme)] async (SubscribeRequest r, ClaimsPrincipal u, MaxioBillingService s, CancellationToken ct) => await HandleAsync(r, u, s, ct))
            .WithTags("MaxioEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest r, ClaimsPrincipal u, MaxioBillingService s, CancellationToken ct)
    {
        var sub = await s.Subscribe(MaxioAuth.Name(u), r, ct);
        return Results.Created($"api/subscriptions/{sub.SubscriptionId}", sub);
    }
}

/// <summary>Shows one of the shopper's subscriptions</summary>
public class GetSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId:int}",
            [Authorize(AuthenticationSchemes = MaxioAuth.Scheme)] async (int subscriptionId, ClaimsPrincipal u, MaxioBillingService s, CancellationToken ct) => await HandleAsync(subscriptionId, u, s, ct))
            .WithTags("MaxioEndpoints");
    }

    public async Task<IResult> HandleAsync(int id, ClaimsPrincipal u, MaxioBillingService s, CancellationToken ct) =>
        Results.Ok(await s.GetSubscription(MaxioAuth.Name(u), id, ct));
}

/// <summary>Pauses a subscription</summary>
public class PauseSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/pause",
            [Authorize(AuthenticationSchemes = MaxioAuth.Scheme)] async (int subscriptionId, ClaimsPrincipal u, MaxioBillingService s, CancellationToken ct) => await HandleAsync(subscriptionId, u, s, ct))
            .WithTags("MaxioEndpoints");
    }

    public async Task<IResult> HandleAsync(int id, ClaimsPrincipal u, MaxioBillingService s, CancellationToken ct) =>
        Results.Ok(await s.Pause(MaxioAuth.Name(u), id, ct));
}

/// <summary>Resumes a paused subscription</summary>
public class ResumeSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/resume",
            [Authorize(AuthenticationSchemes = MaxioAuth.Scheme)] async (int subscriptionId, ClaimsPrincipal u, MaxioBillingService s, CancellationToken ct) => await HandleAsync(subscriptionId, u, s, ct))
            .WithTags("MaxioEndpoints");
    }

    public async Task<IResult> HandleAsync(int id, ClaimsPrincipal u, MaxioBillingService s, CancellationToken ct) =>
        Results.Ok(await s.Resume(MaxioAuth.Name(u), id, ct));
}

/// <summary>Cancels a subscription</summary>
public class CancelSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/cancel",
            [Authorize(AuthenticationSchemes = MaxioAuth.Scheme)] async (int subscriptionId, ClaimsPrincipal u, MaxioBillingService s, CancellationToken ct) => await HandleAsync(subscriptionId, u, s, ct))
            .WithTags("MaxioEndpoints");
    }

    public async Task<IResult> HandleAsync(int id, ClaimsPrincipal u, MaxioBillingService s, CancellationToken ct) =>
        Results.Ok(await s.Cancel(MaxioAuth.Name(u), id, ct));
}

/// <summary>Restarts a cancelled subscription</summary>
public class RestartSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/restart",
            [Authorize(AuthenticationSchemes = MaxioAuth.Scheme)] async (int subscriptionId, ClaimsPrincipal u, MaxioBillingService s, CancellationToken ct) => await HandleAsync(subscriptionId, u, s, ct))
            .WithTags("MaxioEndpoints");
    }

    public async Task<IResult> HandleAsync(int id, ClaimsPrincipal u, MaxioBillingService s, CancellationToken ct) =>
        Results.Ok(await s.Restart(MaxioAuth.Name(u), id, ct));
}

/// <summary>Switches a subscription to another plan</summary>
public class SwitchSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/switch",
            [Authorize(AuthenticationSchemes = MaxioAuth.Scheme)] async (int subscriptionId, SwitchRequest r, ClaimsPrincipal u, MaxioBillingService s, CancellationToken ct) => await HandleAsync(subscriptionId, r, u, s, ct))
            .WithTags("MaxioEndpoints");
    }

    public async Task<IResult> HandleAsync(int id, SwitchRequest r, ClaimsPrincipal u, MaxioBillingService s, CancellationToken ct) =>
        Results.Ok(await s.Switch(MaxioAuth.Name(u), id, r, ct));
}

/// <summary>Shows a subscription's metered usage for one day</summary>
public class SubscriptionUsageEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId:int}/usage",
            [Authorize(AuthenticationSchemes = MaxioAuth.Scheme)] async (int subscriptionId, string? date, ClaimsPrincipal u, MaxioBillingService s, CancellationToken ct) => await HandleAsync(subscriptionId, date, u, s, ct))
            .WithTags("MaxioEndpoints");
    }

    public async Task<IResult> HandleAsync(int id, string? date, ClaimsPrincipal u, MaxioBillingService s, CancellationToken ct) =>
        Results.Ok(await s.UsageForDay(MaxioAuth.Name(u), id, date, ct));
}

/// <summary>Staff: adds a plan</summary>
public class AdminCreatePlanEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans",
            [Authorize(Roles = MaxioAuth.Admin, AuthenticationSchemes = MaxioAuth.Scheme)] async (CreatePlanRequest r, MaxioBillingService s, CancellationToken ct) => await HandleAsync(r, s, ct))
            .WithTags("MaxioEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePlanRequest r, MaxioBillingService s, CancellationToken ct)
    {
        var plan = await s.CreatePlan(r, ct);
        return Results.Created($"api/plans", plan);
    }
}

/// <summary>Staff: prices a plan in another currency</summary>
public class AdminCurrencyPriceEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans/{planId:int}/currency-prices",
            [Authorize(Roles = MaxioAuth.Admin, AuthenticationSchemes = MaxioAuth.Scheme)] async (int planId, CurrencyPriceRequest r, MaxioBillingService s, CancellationToken ct) => await HandleAsync(planId, r, s, ct))
            .WithTags("MaxioEndpoints");
    }

    public async Task<IResult> HandleAsync(int planId, CurrencyPriceRequest r, MaxioBillingService s, CancellationToken ct) =>
        Results.Ok(await s.SetCurrencyPrice(planId, r, ct));
}

/// <summary>Staff: sets the date a subscriber is next billed</summary>
public class AdminNextBillingDateEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/admin/subscriptions/{subscriptionId:int}/next-billing-date",
            [Authorize(Roles = MaxioAuth.Admin, AuthenticationSchemes = MaxioAuth.Scheme)] async (int subscriptionId, NextBillingDateRequest r, MaxioBillingService s, CancellationToken ct) => await HandleAsync(subscriptionId, r, s, ct))
            .WithTags("MaxioEndpoints");
    }

    public async Task<IResult> HandleAsync(int id, NextBillingDateRequest r, MaxioBillingService s, CancellationToken ct) =>
        Results.Ok(await s.SetNextBillingDate(id, r.Date, ct));
}
