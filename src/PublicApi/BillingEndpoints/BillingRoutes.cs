using System;
using System.Security.Claims;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

/// <summary>
/// Shared plumbing of the billing endpoints: the JWT's identity and the 4xx answer for requests the app cannot carry out.
/// </summary>
internal static class BillingHttp
{
    public const string JWT = JwtBearerDefaults.AuthenticationScheme;
    public const string ADMINISTRATORS = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS;

    public static async Task<IResult> RunAsync(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (BillingException ex)
        {
            return Failure(ex.StatusCode, ex.Message);
        }
    }

    public static IResult Failure(int status, string message) =>
        Results.Json(new ErrorDetails { StatusCode = status, Message = message }, statusCode: status);

    public static string? UserName(ClaimsPrincipal user) => user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.Name);
}

/// <summary>Lists the plans a shopper can subscribe to.</summary>
public class ListPlansEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/plans",
            [Authorize(AuthenticationSchemes = BillingHttp.JWT)] (BillingService billing) =>
                BillingHttp.RunAsync(async () => Results.Ok(new ListPlansResponse { Plans = await billing.ListPlansAsync() })))
            .Produces<ListPlansResponse>()
            .WithTags("BillingEndpoints");
    }
}

/// <summary>Subscribes the signed-in shopper to a plan, without card details.</summary>
public class SubscribeEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = BillingHttp.JWT)] (SubscribeRequest request, ClaimsPrincipal user, BillingService billing) =>
                BillingHttp.RunAsync(async () =>
                {
                    var subscription = await billing.SubscribeAsync(BillingHttp.UserName(user), request?.PlanHandle);
                    return Results.Created($"api/subscriptions/{subscription.Id}", subscription);
                }))
            .Produces<SubscriptionDto>(StatusCodes.Status201Created)
            .WithTags("BillingEndpoints");
    }
}

/// <summary>Shows one of the shopper's subscriptions.</summary>
public class GetSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId}",
            [Authorize(AuthenticationSchemes = BillingHttp.JWT)] (int subscriptionId, ClaimsPrincipal user, BillingService billing) =>
                BillingHttp.RunAsync(async () => Results.Ok(await billing.GetSubscriptionPageAsync(BillingHttp.UserName(user), subscriptionId))))
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");
    }
}

/// <summary>Pauses and resumes a subscription.</summary>
public class PauseResumeSubscriptionEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/pause",
            [Authorize(AuthenticationSchemes = BillingHttp.JWT)] (int subscriptionId, ClaimsPrincipal user, BillingService billing) =>
                BillingHttp.RunAsync(async () => Results.Ok(await billing.PauseAsync(BillingHttp.UserName(user), subscriptionId))))
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");

        app.MapPost("api/subscriptions/{subscriptionId}/resume",
            [Authorize(AuthenticationSchemes = BillingHttp.JWT)] (int subscriptionId, ClaimsPrincipal user, BillingService billing) =>
                BillingHttp.RunAsync(async () => Results.Ok(await billing.ResumeAsync(BillingHttp.UserName(user), subscriptionId))))
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");
    }
}

/// <summary>Cancels and restarts a subscription.</summary>
public class CancelRestartSubscriptionEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/cancel",
            [Authorize(AuthenticationSchemes = BillingHttp.JWT)] (int subscriptionId, ClaimsPrincipal user, BillingService billing) =>
                BillingHttp.RunAsync(async () => Results.Ok(await billing.CancelAsync(BillingHttp.UserName(user), subscriptionId))))
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");

        app.MapPost("api/subscriptions/{subscriptionId}/restart",
            [Authorize(AuthenticationSchemes = BillingHttp.JWT)] (int subscriptionId, ClaimsPrincipal user, BillingService billing) =>
                BillingHttp.RunAsync(async () => Results.Ok(await billing.RestartAsync(BillingHttp.UserName(user), subscriptionId))))
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");
    }
}

/// <summary>Switches a subscription to another plan.</summary>
public class SwitchPlanEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/switch",
            [Authorize(AuthenticationSchemes = BillingHttp.JWT)] (int subscriptionId, SwitchPlanRequest request, ClaimsPrincipal user, BillingService billing) =>
                BillingHttp.RunAsync(async () => Results.Ok(await billing.SwitchPlanAsync(BillingHttp.UserName(user), subscriptionId, request?.PlanHandle))))
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");
    }
}

/// <summary>Shows the metered usage of a subscription on one day.</summary>
public class SubscriptionUsageEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId}/usage",
            [Authorize(AuthenticationSchemes = BillingHttp.JWT)] (int subscriptionId, string? date, ClaimsPrincipal user, BillingService billing) =>
                BillingHttp.RunAsync(async () => Results.Ok(await billing.GetUsageAsync(BillingHttp.UserName(user), subscriptionId, date))))
            .Produces<DailyUsageResponse>()
            .WithTags("BillingEndpoints");
    }
}

/// <summary>Staff: adds a plan.</summary>
public class CreatePlanEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans",
            [Authorize(Roles = BillingHttp.ADMINISTRATORS, AuthenticationSchemes = BillingHttp.JWT)] (CreatePlanRequest request, BillingService billing) =>
                BillingHttp.RunAsync(async () =>
                {
                    var plan = await billing.CreatePlanAsync(request?.Name, request?.Price, request?.BrandId);
                    return Results.Created("api/plans", plan);
                }))
            .Produces<PlanDto>(StatusCodes.Status201Created)
            .WithTags("BillingEndpoints");
    }
}

/// <summary>Staff: prices a plan in another currency.</summary>
public class CreateCurrencyPriceEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans/{planId}/currency-prices",
            [Authorize(Roles = BillingHttp.ADMINISTRATORS, AuthenticationSchemes = BillingHttp.JWT)] (int planId, CurrencyPriceRequest request, BillingService billing) =>
                BillingHttp.RunAsync(async () =>
                {
                    var prices = await billing.AddCurrencyPriceAsync(planId, request?.Currency, request?.Price);
                    return Results.Created($"api/admin/plans/{planId}/currency-prices", prices);
                }))
            .Produces<CurrencyPriceResponse>(StatusCodes.Status201Created)
            .WithTags("BillingEndpoints");
    }
}

/// <summary>Staff: sets the date a subscriber is next billed.</summary>
public class SetNextBillingDateEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/admin/subscriptions/{subscriptionId}/next-billing-date",
            [Authorize(Roles = BillingHttp.ADMINISTRATORS, AuthenticationSchemes = BillingHttp.JWT)] (int subscriptionId, NextBillingDateRequest request, BillingService billing) =>
                BillingHttp.RunAsync(async () => Results.Ok(await billing.SetNextBillingDateAsync(subscriptionId, request?.Date))))
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");
    }
}
