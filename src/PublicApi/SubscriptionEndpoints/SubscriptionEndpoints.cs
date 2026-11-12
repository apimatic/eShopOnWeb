using System;
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

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Shared glue: the caller's identity comes from the JWT, and a failure the app cannot carry out is a 4xx with a reason.</summary>
internal static class Api
{
    public const string Admins = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS;
    public const string Scheme = JwtBearerDefaults.AuthenticationScheme;

    public static string UserName(ClaimsPrincipal user) => user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.Name) ?? "";

    public static async Task<IResult> Run(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (MaxioFailure f)
        {
            return Results.Json(new { statusCode = f.Status, message = f.Message }, statusCode: f.Status);
        }
    }
}

public class ListPlansEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) =>
        app.MapGet("api/plans",
            [Authorize(AuthenticationSchemes = Api.Scheme)] (MaxioBillingService svc, CancellationToken ct) =>
                Api.Run(async () => Results.Ok(await svc.ListPlans(ct))))
            .Produces<PlanListDto>().WithTags("SubscriptionEndpoints");
}

public class SubscribeEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) =>
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = Api.Scheme)] (SubscribeRequest request, ClaimsPrincipal user, MaxioBillingService svc, CancellationToken ct) =>
                Api.Run(async () =>
                {
                    var created = await svc.Subscribe(Api.UserName(user), request.PlanHandle, ct);
                    return Results.Created($"api/subscriptions/{created.SubscriptionId}", created);
                }))
            .Produces<SubscriptionDto>(StatusCodes.Status201Created).WithTags("SubscriptionEndpoints");
}

public class GetSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) =>
        app.MapGet("api/subscriptions/{subscriptionId:int}",
            [Authorize(AuthenticationSchemes = Api.Scheme)] (int subscriptionId, ClaimsPrincipal user, MaxioBillingService svc, CancellationToken ct) =>
                Api.Run(async () => Results.Ok(await svc.GetSubscription(Api.UserName(user), subscriptionId, ct))))
            .Produces<SubscriptionDto>().WithTags("SubscriptionEndpoints");
}

public class PauseSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) =>
        app.MapPost("api/subscriptions/{subscriptionId:int}/pause",
            [Authorize(AuthenticationSchemes = Api.Scheme)] (int subscriptionId, ClaimsPrincipal user, MaxioBillingService svc, CancellationToken ct) =>
                Api.Run(async () => Results.Ok(await svc.Pause(Api.UserName(user), subscriptionId, ct))))
            .Produces<SubscriptionDto>().WithTags("SubscriptionEndpoints");
}

public class ResumeSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) =>
        app.MapPost("api/subscriptions/{subscriptionId:int}/resume",
            [Authorize(AuthenticationSchemes = Api.Scheme)] (int subscriptionId, ClaimsPrincipal user, MaxioBillingService svc, CancellationToken ct) =>
                Api.Run(async () => Results.Ok(await svc.Resume(Api.UserName(user), subscriptionId, ct))))
            .Produces<SubscriptionDto>().WithTags("SubscriptionEndpoints");
}

public class CancelSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) =>
        app.MapPost("api/subscriptions/{subscriptionId:int}/cancel",
            [Authorize(AuthenticationSchemes = Api.Scheme)] (int subscriptionId, ClaimsPrincipal user, MaxioBillingService svc, CancellationToken ct) =>
                Api.Run(async () => Results.Ok(await svc.Cancel(Api.UserName(user), subscriptionId, ct))))
            .Produces<SubscriptionDto>().WithTags("SubscriptionEndpoints");
}

public class RestartSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) =>
        app.MapPost("api/subscriptions/{subscriptionId:int}/restart",
            [Authorize(AuthenticationSchemes = Api.Scheme)] (int subscriptionId, ClaimsPrincipal user, MaxioBillingService svc, CancellationToken ct) =>
                Api.Run(async () => Results.Ok(await svc.Restart(Api.UserName(user), subscriptionId, ct))))
            .Produces<SubscriptionDto>().WithTags("SubscriptionEndpoints");
}

public class SwitchPlanEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) =>
        app.MapPost("api/subscriptions/{subscriptionId:int}/switch",
            [Authorize(AuthenticationSchemes = Api.Scheme)] (int subscriptionId, SwitchPlanRequest request, ClaimsPrincipal user, MaxioBillingService svc, CancellationToken ct) =>
                Api.Run(async () => Results.Ok(await svc.Switch(Api.UserName(user), subscriptionId, request.PlanHandle, ct))))
            .Produces<SubscriptionDto>().WithTags("SubscriptionEndpoints");
}

public class SubscriptionUsageEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) =>
        app.MapGet("api/subscriptions/{subscriptionId:int}/usage",
            [Authorize(AuthenticationSchemes = Api.Scheme)] (int subscriptionId, string? date, ClaimsPrincipal user, MaxioBillingService svc, CancellationToken ct) =>
                Api.Run(async () => Results.Ok(await svc.UsageForDay(Api.UserName(user), subscriptionId, date, ct))))
            .Produces<UsageDayDto>().WithTags("SubscriptionEndpoints");
}

public class CreatePlanEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) =>
        app.MapPost("api/admin/plans",
            [Authorize(Roles = Api.Admins, AuthenticationSchemes = Api.Scheme)] (CreatePlanRequest request, MaxioBillingService svc, CancellationToken ct) =>
                Api.Run(async () =>
                {
                    var plan = await svc.CreatePlan(request, ct);
                    return Results.Created($"api/plans/{plan.Id}", plan);
                }))
            .Produces<PlanDto>(StatusCodes.Status201Created).WithTags("SubscriptionEndpoints");
}

public class SetCurrencyPriceEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) =>
        app.MapPost("api/admin/plans/{planId:int}/currency-prices",
            [Authorize(Roles = Api.Admins, AuthenticationSchemes = Api.Scheme)] (int planId, CurrencyPriceRequest request, MaxioBillingService svc, CancellationToken ct) =>
                Api.Run(async () => Results.Ok(await svc.SetCurrencyPrice(planId, request, ct))))
            .Produces<CurrencyPriceDto>().WithTags("SubscriptionEndpoints");
}

public class SetNextBillingDateEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) =>
        app.MapPut("api/admin/subscriptions/{subscriptionId:int}/next-billing-date",
            [Authorize(Roles = Api.Admins, AuthenticationSchemes = Api.Scheme)] (int subscriptionId, NextBillingDateRequest request, MaxioBillingService svc, CancellationToken ct) =>
                Api.Run(async () => Results.Ok(await svc.SetNextBillingDate(subscriptionId, request.Date, ct))))
            .Produces<SubscriptionDto>().WithTags("SubscriptionEndpoints");
}
