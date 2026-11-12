using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.MaxioEndpoints;

internal static class MaxioHttp
{
    public const string ADMIN = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS;
    public const string JWT = JwtBearerDefaults.AuthenticationScheme;

    /// <summary>Runs the handler; a BillingException becomes a 4xx/5xx answer carrying the reason.</summary>
    public static async Task<IResult> Run(Func<Task<IResult>> handler)
    {
        try { return await handler(); }
        catch (BillingException ex) { return Error(ex.Status, ex.Message); }
    }

    public static IResult Error(int status, string message) =>
        Results.Json(new ErrorDetails { StatusCode = status, Message = message }, statusCode: status);

    public static string UserName(ClaimsPrincipal user) =>
        user.FindFirst(ClaimTypes.Name)?.Value ?? user.Identity?.Name ?? "";
}

public record SubscribeRequest(string? PlanHandle);
public record SwitchRequest(string? PlanHandle);
public record CreatePlanRequest(string? Name, decimal? MonthlyPrice, int? BrandId, string? Brand);
public record CurrencyPriceRequest(string? Currency, decimal? Price);
public record NextBillingDateRequest(string? Date);

/// <summary>Lists the plans a shopper can subscribe to.</summary>
public class ListPlansEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) =>
        app.MapGet("api/plans",
            [Authorize(AuthenticationSchemes = MaxioHttp.JWT)] (MaxioBillingService svc, HttpContext http) =>
                MaxioHttp.Run(async () =>
                {
                    var (plans, truncated) = await svc.ListPlansAsync(http.RequestAborted);
                    return Results.Ok(new { plans, truncated });
                }))
            .WithTags("MaxioEndpoints");
}

/// <summary>Subscribes the signed-in shopper to a plan (no card details).</summary>
public class SubscribeEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) =>
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = MaxioHttp.JWT)] (SubscribeRequest request, ClaimsPrincipal user, MaxioBillingService svc, HttpContext http) =>
                MaxioHttp.Run(async () =>
                {
                    var dto = await svc.SubscribeAsync(MaxioHttp.UserName(user), request?.PlanHandle, http.RequestAborted);
                    return Results.Created($"api/subscriptions/{dto.Id}", dto);
                }))
            .WithTags("MaxioEndpoints");
}

/// <summary>Shows one of the shopper's subscriptions.</summary>
public class GetSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) =>
        app.MapGet("api/subscriptions/{subscriptionId:int}",
            [Authorize(AuthenticationSchemes = MaxioHttp.JWT)] (int subscriptionId, ClaimsPrincipal user, MaxioBillingService svc, HttpContext http) =>
                MaxioHttp.Run(async () => Results.Ok(await svc.GetSubscriptionAsync(MaxioHttp.UserName(user), subscriptionId, http.RequestAborted))))
            .WithTags("MaxioEndpoints");
}

/// <summary>Pause, resume, cancel and restart a subscription.</summary>
public class SubscriptionStatusEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        Map(app, "pause", (s, u, id, ct) => s.PauseAsync(u, id, ct));
        Map(app, "resume", (s, u, id, ct) => s.ResumeAsync(u, id, ct));
        Map(app, "cancel", (s, u, id, ct) => s.CancelAsync(u, id, ct));
        Map(app, "restart", (s, u, id, ct) => s.RestartAsync(u, id, ct));
    }

    private static void Map(IEndpointRouteBuilder app, string action,
        Func<MaxioBillingService, string, int, CancellationToken, Task<MaxioBillingService.SubscriptionDto>> call) =>
        app.MapPost($"api/subscriptions/{{subscriptionId:int}}/{action}",
            [Authorize(AuthenticationSchemes = MaxioHttp.JWT)] (int subscriptionId, ClaimsPrincipal user, MaxioBillingService svc, HttpContext http) =>
                MaxioHttp.Run(async () => Results.Ok(await call(svc, MaxioHttp.UserName(user), subscriptionId, http.RequestAborted))))
            .WithTags("MaxioEndpoints");
}

/// <summary>Switches a subscription (including a cancelled one) to another plan.</summary>
public class SwitchPlanEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) =>
        app.MapPost("api/subscriptions/{subscriptionId:int}/switch",
            [Authorize(AuthenticationSchemes = MaxioHttp.JWT)] (int subscriptionId, SwitchRequest request, ClaimsPrincipal user, MaxioBillingService svc, HttpContext http) =>
                MaxioHttp.Run(async () => Results.Ok(await svc.SwitchAsync(MaxioHttp.UserName(user), subscriptionId, request?.PlanHandle, http.RequestAborted))))
            .WithTags("MaxioEndpoints");
}

/// <summary>Metered usage of a subscription on one day.</summary>
public class SubscriptionUsageEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) =>
        app.MapGet("api/subscriptions/{subscriptionId:int}/usage",
            [Authorize(AuthenticationSchemes = MaxioHttp.JWT)] (int subscriptionId, string? date, ClaimsPrincipal user, MaxioBillingService svc, HttpContext http) =>
                MaxioHttp.Run(async () => Results.Ok(await svc.GetUsageAsync(MaxioHttp.UserName(user), subscriptionId, date, http.RequestAborted))))
            .WithTags("MaxioEndpoints");
}

/// <summary>Staff: add a plan.</summary>
public class CreatePlanEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) =>
        app.MapPost("api/admin/plans",
            [Authorize(Roles = MaxioHttp.ADMIN, AuthenticationSchemes = MaxioHttp.JWT)] (CreatePlanRequest request, MaxioBillingService svc, HttpContext http) =>
                MaxioHttp.Run(async () =>
                {
                    var plan = await svc.CreatePlanAsync(request?.Name, request?.MonthlyPrice, request?.BrandId, request?.Brand, http.RequestAborted);
                    return Results.Created("api/plans", plan);
                }))
            .WithTags("MaxioEndpoints");
}

/// <summary>Staff: price a plan in another currency.</summary>
public class CurrencyPriceEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) =>
        app.MapPost("api/admin/plans/{planId:int}/currency-prices",
            [Authorize(Roles = MaxioHttp.ADMIN, AuthenticationSchemes = MaxioHttp.JWT)] (int planId, CurrencyPriceRequest request, MaxioBillingService svc, HttpContext http) =>
                MaxioHttp.Run(async () => Results.Ok(await svc.SetCurrencyPriceAsync(planId, request?.Currency, request?.Price, http.RequestAborted))))
            .WithTags("MaxioEndpoints");
}

/// <summary>Staff: set the date a subscriber is next billed.</summary>
public class NextBillingDateEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) =>
        app.MapPut("api/admin/subscriptions/{subscriptionId:int}/next-billing-date",
            [Authorize(Roles = MaxioHttp.ADMIN, AuthenticationSchemes = MaxioHttp.JWT)] (int subscriptionId, NextBillingDateRequest request, MaxioBillingService svc, HttpContext http) =>
                MaxioHttp.Run(async () => Results.Ok(await svc.SetNextBillingDateAsync(subscriptionId, request?.Date, http.RequestAborted))))
            .WithTags("MaxioEndpoints");
}
