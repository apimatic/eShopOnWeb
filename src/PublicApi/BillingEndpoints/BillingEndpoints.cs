using System;
using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

public record SubscribeRequest(string? PlanHandle);
public record SwitchPlanRequest(string? PlanHandle);
public record CreatePlanRequest(string? Name, decimal? MonthlyPrice, string? Brand);
public record CurrencyPriceRequest(string? Currency, decimal? Price);
public record NextBillingDateRequest(string? Date);

internal static class BillingAuth
{
    public const string SHOPPER = "";
    public const string SCHEME = JwtBearerDefaults.AuthenticationScheme;

    /// <summary>The caller's identity always comes from the JWT.</summary>
    public static string UserName(ClaimsPrincipal user)
    {
        var name = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new BillingException(401, "The token does not identify a user.");
        }
        return name;
    }

    public static DateTime ParseDate(string? value)
    {
        if (!DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw new BillingException(400, "The date must be given as YYYY-MM-DD.");
        }
        return date;
    }
}

/// <summary>Lists the plans a shopper can subscribe to</summary>
public class ListPlansEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/plans",
            [Authorize(AuthenticationSchemes = BillingAuth.SCHEME)] async (BillingService billing) =>
                Results.Ok(new { plans = await billing.ListPlansAsync() }))
            .WithTags("BillingEndpoints");
    }
}

/// <summary>Subscribes the signed-in shopper to a plan</summary>
public class SubscribeEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = BillingAuth.SCHEME)] async (SubscribeRequest request, ClaimsPrincipal user, BillingService billing, UserManager<ApplicationUser> users) =>
            {
                var name = BillingAuth.UserName(user);
                var email = (await users.FindByNameAsync(name))?.Email;
                var sub = await billing.SubscribeAsync(name, email, request.PlanHandle);
                return Results.Created("api/subscriptions/" + sub.Id, sub);
            })
            .WithTags("BillingEndpoints");
    }
}

/// <summary>Shows one of the shopper's subscriptions</summary>
public class GetSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId}",
            [Authorize(AuthenticationSchemes = BillingAuth.SCHEME)] async (long subscriptionId, ClaimsPrincipal user, BillingService billing) =>
                Results.Ok(await billing.GetSubscriptionAsync(subscriptionId, BillingAuth.UserName(user))))
            .WithTags("BillingEndpoints");
    }
}

/// <summary>Pause, resume, cancel and restart a subscription</summary>
public class SubscriptionLifecycleEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/pause",
            [Authorize(AuthenticationSchemes = BillingAuth.SCHEME)] async (long subscriptionId, ClaimsPrincipal user, BillingService billing) =>
                Results.Ok(await billing.PauseAsync(subscriptionId, BillingAuth.UserName(user))))
            .WithTags("BillingEndpoints");

        app.MapPost("api/subscriptions/{subscriptionId}/resume",
            [Authorize(AuthenticationSchemes = BillingAuth.SCHEME)] async (long subscriptionId, ClaimsPrincipal user, BillingService billing) =>
                Results.Ok(await billing.ResumeAsync(subscriptionId, BillingAuth.UserName(user))))
            .WithTags("BillingEndpoints");

        app.MapPost("api/subscriptions/{subscriptionId}/cancel",
            [Authorize(AuthenticationSchemes = BillingAuth.SCHEME)] async (long subscriptionId, ClaimsPrincipal user, BillingService billing) =>
                Results.Ok(await billing.CancelAsync(subscriptionId, BillingAuth.UserName(user))))
            .WithTags("BillingEndpoints");

        app.MapPost("api/subscriptions/{subscriptionId}/restart",
            [Authorize(AuthenticationSchemes = BillingAuth.SCHEME)] async (long subscriptionId, ClaimsPrincipal user, BillingService billing) =>
                Results.Ok(await billing.RestartAsync(subscriptionId, BillingAuth.UserName(user))))
            .WithTags("BillingEndpoints");

        app.MapPost("api/subscriptions/{subscriptionId}/switch",
            [Authorize(AuthenticationSchemes = BillingAuth.SCHEME)] async (long subscriptionId, SwitchPlanRequest request, ClaimsPrincipal user, BillingService billing) =>
                Results.Ok(await billing.SwitchAsync(subscriptionId, BillingAuth.UserName(user), request.PlanHandle)))
            .WithTags("BillingEndpoints");
    }
}

/// <summary>Shows metered usage for a single day</summary>
public class SubscriptionUsageEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId}/usage",
            [Authorize(AuthenticationSchemes = BillingAuth.SCHEME)] async (long subscriptionId, string? date, ClaimsPrincipal user, BillingService billing) =>
                Results.Ok(await billing.GetUsageAsync(subscriptionId, BillingAuth.UserName(user), BillingAuth.ParseDate(date))))
            .WithTags("BillingEndpoints");
    }
}

/// <summary>Staff endpoints for plans and billing dates</summary>
public class AdminBillingEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = BillingAuth.SCHEME)] async (CreatePlanRequest request, BillingService billing) =>
            {
                var plan = await billing.CreatePlanAsync(request.Name, request.MonthlyPrice, request.Brand);
                return Results.Created("api/plans", plan);
            })
            .WithTags("BillingEndpoints");

        app.MapPost("api/admin/plans/{planId}/currency-prices",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = BillingAuth.SCHEME)] async (long planId, CurrencyPriceRequest request, BillingService billing) =>
                Results.Created("api/plans", await billing.AddCurrencyPriceAsync(planId, request.Currency, request.Price)))
            .WithTags("BillingEndpoints");

        app.MapPut("api/admin/subscriptions/{subscriptionId}/next-billing-date",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = BillingAuth.SCHEME)] async (long subscriptionId, NextBillingDateRequest request, BillingService billing) =>
                Results.Ok(await billing.SetNextBillingDateAsync(subscriptionId, BillingAuth.ParseDate(request.Date))))
            .WithTags("BillingEndpoints");
    }
}
