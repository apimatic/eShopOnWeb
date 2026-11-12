using System;
using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Billing;

// Failures the app cannot carry out are BillingExceptions; ExceptionMiddleware turns them into a 4xx/5xx with the reason.

internal static class Auth
{
    public const string Shopper = JwtBearerDefaults.AuthenticationScheme;
}

/// <summary>Lists the plans a shopper can subscribe to</summary>
public class ListPlansEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (MaxioBillingService billing, CancellationToken ct) => Results.Ok(await billing.ListPlans(ct)))
            .Produces<ListPlansResponse>()
            .WithTags("BillingEndpoints");
    }
}

/// <summary>Subscribes the signed-in shopper to a plan</summary>
public class SubscribeEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ClaimsPrincipal user, MaxioBillingService billing, CancellationToken ct) =>
            {
                var sub = await billing.Subscribe(user, request.PlanHandle, ct);
                return Results.Created($"api/subscriptions/{sub.Id}", sub);
            })
            .Produces<SubscriptionDto>(StatusCodes.Status201Created)
            .WithTags("BillingEndpoints");
    }
}

/// <summary>Shows one of the shopper's subscriptions, with whether it is paid</summary>
public class GetSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, ClaimsPrincipal user, MaxioBillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.GetSubscription(user, subscriptionId, ct)))
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");
    }
}

/// <summary>Pause, resume, cancel, restart and switch a subscription</summary>
public class SubscriptionActionEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/pause",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, ClaimsPrincipal user, MaxioBillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.Pause(user, subscriptionId, ct)))
            .Produces<SubscriptionDto>().WithTags("BillingEndpoints");

        app.MapPost("api/subscriptions/{subscriptionId}/resume",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, ClaimsPrincipal user, MaxioBillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.Resume(user, subscriptionId, ct)))
            .Produces<SubscriptionDto>().WithTags("BillingEndpoints");

        app.MapPost("api/subscriptions/{subscriptionId}/cancel",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, ClaimsPrincipal user, MaxioBillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.Cancel(user, subscriptionId, ct)))
            .Produces<SubscriptionDto>().WithTags("BillingEndpoints");

        app.MapPost("api/subscriptions/{subscriptionId}/restart",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, ClaimsPrincipal user, MaxioBillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.Restart(user, subscriptionId, ct)))
            .Produces<SubscriptionDto>().WithTags("BillingEndpoints");

        app.MapPost("api/subscriptions/{subscriptionId}/switch",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, SwitchPlanRequest request, ClaimsPrincipal user, MaxioBillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.Switch(user, subscriptionId, request.PlanHandle, ct)))
            .Produces<SubscriptionDto>().WithTags("BillingEndpoints");
    }
}

/// <summary>Shows the shopper's metered usage on one day</summary>
public class SubscriptionUsageEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId}/usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, string? date, ClaimsPrincipal user, MaxioBillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.Usage(user, subscriptionId, date, ct)))
            .Produces<UsageResponseDto>()
            .WithTags("BillingEndpoints");
    }
}

/// <summary>Staff: adds a plan</summary>
public class CreatePlanEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePlanRequest request, MaxioBillingService billing, CancellationToken ct) =>
            {
                var plan = await billing.CreatePlan(request.Name, request.MonthlyPrice, request.BrandId, ct);
                return Results.Created($"api/plans", plan);
            })
            .Produces<PlanDto>(StatusCodes.Status201Created)
            .WithTags("BillingEndpoints");
    }
}

/// <summary>Staff: prices a plan in another currency</summary>
public class CreateCurrencyPriceEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans/{planId}/currency-prices",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int planId, CurrencyPriceRequest request, MaxioBillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.AddCurrencyPrice(planId, request.Currency, request.Price, ct)))
            .Produces<System.Collections.Generic.IReadOnlyList<CurrencyPriceDto>>()
            .WithTags("BillingEndpoints");
    }
}

/// <summary>Staff: sets the date a subscriber is next billed</summary>
public class SetNextBillingDateEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/admin/subscriptions/{subscriptionId}/next-billing-date",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, NextBillingDateRequest request, MaxioBillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.SetNextBillingDate(subscriptionId, request.Date, ct)))
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");
    }
}
