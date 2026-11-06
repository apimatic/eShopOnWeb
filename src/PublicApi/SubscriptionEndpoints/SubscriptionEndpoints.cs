using System;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanHandleRequest : BaseRequest
{
    public string? PlanHandle { get; set; }
    public string? NewPlanHandle { get; set; }
    public string? Handle { get; set; }
    public string? Resolve() => PlanHandle ?? NewPlanHandle ?? Handle;
}

public class CreatePlanRequest : BaseRequest
{
    public string? Name { get; set; }
    public decimal? Price { get; set; }
    public decimal? MonthlyPrice { get; set; }
    public string? Brand { get; set; }
}

public class CurrencyPriceRequest : BaseRequest
{
    public string? Currency { get; set; }
    public decimal? Price { get; set; }
}

public class NextBillingDateRequest : BaseRequest
{
    public string? Date { get; set; }
    public string? NextBillingDate { get; set; }
}

internal static class SubscriptionEndpointHelpers
{
    public const string SCHEME = JwtBearerDefaults.AuthenticationScheme;
    public const string TAG = "SubscriptionEndpoints";

    /// <summary>The caller's identity comes from the JWT, never from the request.</summary>
    public static string UserName(HttpContext context)
    {
        var user = context.User;
        var name = user.Identity?.Name
            ?? user.FindFirstValue(ClaimTypes.Name)
            ?? user.FindFirstValue("unique_name")
            ?? user.FindFirstValue("name");
        if (string.IsNullOrWhiteSpace(name))
            throw new ApiException(401, "The token does not identify a user.");
        return name;
    }
}

/// <summary>Lists the plans a shopper can subscribe to</summary>
public class PlanListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/plans",
            [Authorize(AuthenticationSchemes = SubscriptionEndpointHelpers.SCHEME)] async (BillingService billing) =>
                Results.Ok(new { Plans = await billing.ListPlansAsync() }))
            .Produces(200)
            .WithTags(SubscriptionEndpointHelpers.TAG);
    }
}

/// <summary>Subscribes the signed-in shopper to a plan</summary>
public class SubscribeEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = SubscriptionEndpointHelpers.SCHEME)] async (PlanHandleRequest request, HttpContext http, BillingService billing) =>
            {
                var sub = await billing.SubscribeAsync(SubscriptionEndpointHelpers.UserName(http), request.Resolve());
                return Results.Created($"api/subscriptions/{sub.Id}", sub);
            })
            .Produces(201)
            .WithTags(SubscriptionEndpointHelpers.TAG);
    }
}

/// <summary>Shows one of the shopper's subscriptions</summary>
public class SubscriptionGetEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId}",
            [Authorize(AuthenticationSchemes = SubscriptionEndpointHelpers.SCHEME)] async (long subscriptionId, HttpContext http, BillingService billing) =>
                Results.Ok(await billing.GetSubscriptionAsync(SubscriptionEndpointHelpers.UserName(http), subscriptionId)))
            .Produces(200)
            .WithTags(SubscriptionEndpointHelpers.TAG);
    }
}

/// <summary>Pauses, resumes, cancels, restarts or switches the plan of a subscription</summary>
public class SubscriptionActionEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        Map(app, "pause", (b, u, id, _) => b.PauseAsync(u, id));
        Map(app, "resume", (b, u, id, _) => b.ResumeAsync(u, id));
        Map(app, "cancel", (b, u, id, _) => b.CancelAsync(u, id));
        Map(app, "restart", (b, u, id, _) => b.RestartAsync(u, id));

        app.MapPost("api/subscriptions/{subscriptionId}/switch",
            [Authorize(AuthenticationSchemes = SubscriptionEndpointHelpers.SCHEME)] async (long subscriptionId, PlanHandleRequest request, HttpContext http, BillingService billing) =>
                Results.Ok(await billing.SwitchPlanAsync(SubscriptionEndpointHelpers.UserName(http), subscriptionId, request.Resolve())))
            .Produces(200)
            .WithTags(SubscriptionEndpointHelpers.TAG);
    }

    private static void Map(IEndpointRouteBuilder app, string action, Func<BillingService, string, long, object?, Task<SubscriptionDto>> run)
    {
        app.MapPost($"api/subscriptions/{{subscriptionId}}/{action}",
            [Authorize(AuthenticationSchemes = SubscriptionEndpointHelpers.SCHEME)] async (long subscriptionId, HttpContext http, BillingService billing) =>
                Results.Ok(await run(billing, SubscriptionEndpointHelpers.UserName(http), subscriptionId, null)))
            .Produces(200)
            .WithTags(SubscriptionEndpointHelpers.TAG);
    }
}

/// <summary>Metered usage on a single day</summary>
public class SubscriptionUsageEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId}/usage",
            [Authorize(AuthenticationSchemes = SubscriptionEndpointHelpers.SCHEME)] async (long subscriptionId, string? date, HttpContext http, BillingService billing) =>
            {
                if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                    throw new ApiException(400, "The 'date' query parameter is required in the form YYYY-MM-DD.");
                return Results.Ok(await billing.GetUsageAsync(SubscriptionEndpointHelpers.UserName(http), subscriptionId, day));
            })
            .Produces(200)
            .WithTags(SubscriptionEndpointHelpers.TAG);
    }
}

/// <summary>Staff: adds a plan</summary>
public class CreatePlanEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = SubscriptionEndpointHelpers.SCHEME)] async (CreatePlanRequest request, BillingService billing) =>
            {
                var price = request.Price ?? request.MonthlyPrice
                    ?? throw new ApiException(400, "A monthly price is required.");
                var plan = await billing.CreatePlanAsync(request.Name, price, request.Brand);
                return Results.Created($"api/plans/{plan.Handle}", plan);
            })
            .Produces(201)
            .WithTags(SubscriptionEndpointHelpers.TAG);
    }
}

/// <summary>Staff: prices a plan in another currency</summary>
public class CreateCurrencyPriceEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans/{planId}/currency-prices",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = SubscriptionEndpointHelpers.SCHEME)] async (long planId, CurrencyPriceRequest request, BillingService billing) =>
            {
                var price = request.Price ?? throw new ApiException(400, "A price is required.");
                return Results.Created($"api/admin/plans/{planId}/currency-prices",
                    await billing.AddCurrencyPriceAsync(planId, request.Currency, price));
            })
            .Produces(201)
            .WithTags(SubscriptionEndpointHelpers.TAG);
    }
}

/// <summary>Staff: sets the date a subscriber is next billed</summary>
public class SetNextBillingDateEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/admin/subscriptions/{subscriptionId}/next-billing-date",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = SubscriptionEndpointHelpers.SCHEME)] async (long subscriptionId, NextBillingDateRequest request, BillingService billing) =>
            {
                var text = request.Date ?? request.NextBillingDate;
                if (!DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    throw new ApiException(400, "A date in the form YYYY-MM-DD is required.");
                return Results.Ok(await billing.SetNextBillingDateAsync(subscriptionId, date));
            })
            .Produces(200)
            .WithTags(SubscriptionEndpointHelpers.TAG);
    }
}
