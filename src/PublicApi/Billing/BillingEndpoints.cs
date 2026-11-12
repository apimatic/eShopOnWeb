using System;
using System.Globalization;
using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

internal static class BillingAuth
{
    public const string SCHEME = JwtBearerDefaults.AuthenticationScheme;

    /// <summary>The identity of the caller comes from the JWT (name claim), never from the request.</summary>
    public static string Username(ClaimsPrincipal user) =>
        user.Identity?.Name is { Length: > 0 } name ? name : throw new BillingException(401, "The token carries no user name.");
}

public class ListPlansEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) =>
        app.MapGet("api/plans",
            [Authorize(AuthenticationSchemes = BillingAuth.SCHEME)] async (BillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.ListPlans(ct)))
            .Produces<ListPlansResponse>().WithTags("BillingEndpoints");
}

public class SubscribeEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) =>
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = BillingAuth.SCHEME)] async (SubscribeRequest request, ClaimsPrincipal user, BillingService billing, CancellationToken ct) =>
            {
                var created = await billing.Subscribe(BillingAuth.Username(user), request.PlanHandle, ct);
                return Results.Created($"api/subscriptions/{created.Id}", created);
            })
            .Produces<SubscriptionDto>(201).WithTags("BillingEndpoints");
}

public class GetSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) =>
        app.MapGet("api/subscriptions/{subscriptionId:int}",
            [Authorize(AuthenticationSchemes = BillingAuth.SCHEME)] async (int subscriptionId, ClaimsPrincipal user, BillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.GetSubscription(BillingAuth.Username(user), subscriptionId, ct)))
            .Produces<SubscriptionDto>().WithTags("BillingEndpoints");
}

public class PauseSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) =>
        app.MapPost("api/subscriptions/{subscriptionId:int}/pause",
            [Authorize(AuthenticationSchemes = BillingAuth.SCHEME)] async (int subscriptionId, ClaimsPrincipal user, BillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.Pause(BillingAuth.Username(user), subscriptionId, ct)))
            .Produces<SubscriptionDto>().WithTags("BillingEndpoints");
}

public class ResumeSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) =>
        app.MapPost("api/subscriptions/{subscriptionId:int}/resume",
            [Authorize(AuthenticationSchemes = BillingAuth.SCHEME)] async (int subscriptionId, ClaimsPrincipal user, BillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.Resume(BillingAuth.Username(user), subscriptionId, ct)))
            .Produces<SubscriptionDto>().WithTags("BillingEndpoints");
}

public class CancelSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) =>
        app.MapPost("api/subscriptions/{subscriptionId:int}/cancel",
            [Authorize(AuthenticationSchemes = BillingAuth.SCHEME)] async (int subscriptionId, ClaimsPrincipal user, BillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.Cancel(BillingAuth.Username(user), subscriptionId, ct)))
            .Produces<SubscriptionDto>().WithTags("BillingEndpoints");
}

public class RestartSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) =>
        app.MapPost("api/subscriptions/{subscriptionId:int}/restart",
            [Authorize(AuthenticationSchemes = BillingAuth.SCHEME)] async (int subscriptionId, ClaimsPrincipal user, BillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.Restart(BillingAuth.Username(user), subscriptionId, ct)))
            .Produces<SubscriptionDto>().WithTags("BillingEndpoints");
}

public class SwitchPlanEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) =>
        app.MapPost("api/subscriptions/{subscriptionId:int}/switch",
            [Authorize(AuthenticationSchemes = BillingAuth.SCHEME)] async (int subscriptionId, SwitchPlanRequest request, ClaimsPrincipal user, BillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.Switch(BillingAuth.Username(user), subscriptionId, request.PlanHandle, ct)))
            .Produces<SubscriptionDto>().WithTags("BillingEndpoints");
}

public class SubscriptionUsageEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) =>
        app.MapGet("api/subscriptions/{subscriptionId:int}/usage",
            [Authorize(AuthenticationSchemes = BillingAuth.SCHEME)] async (int subscriptionId, string? date, ClaimsPrincipal user, BillingService billing, CancellationToken ct) =>
            {
                if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                {
                    throw new BillingException(400, "date is required in the form YYYY-MM-DD.");
                }
                return Results.Ok(await billing.UsageForDay(BillingAuth.Username(user), subscriptionId, day, ct));
            })
            .Produces<DayUsageResponse>().WithTags("BillingEndpoints");
}

public class CreatePlanEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) =>
        app.MapPost("api/admin/plans",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = BillingAuth.SCHEME)] async (CreatePlanRequest request, BillingService billing, CancellationToken ct) =>
            {
                var plan = await billing.CreatePlan(request, ct);
                return Results.Created("api/plans", plan);
            })
            .Produces<PlanDto>(201).WithTags("BillingEndpoints");
}

public class AddCurrencyPriceEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) =>
        app.MapPost("api/admin/plans/{planId:int}/currency-prices",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = BillingAuth.SCHEME)] async (int planId, CurrencyPriceRequest request, BillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.AddCurrencyPrice(planId, request, ct)))
            .Produces<CurrencyPriceDto>().WithTags("BillingEndpoints");
}

public class SetNextBillingDateEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) =>
        app.MapPut("api/admin/subscriptions/{subscriptionId:int}/next-billing-date",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = BillingAuth.SCHEME)] async (int subscriptionId, NextBillingDateRequest request, BillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.SetNextBillingDate(subscriptionId, request.Date, ct)))
            .Produces<NextBillingDateDto>().WithTags("BillingEndpoints");
}
