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
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Shared plumbing for the billing endpoints: identity comes from the JWT only, and every failure the app
/// cannot recover from is answered with a status and a reason.
/// </summary>
internal static class BillingEndpointSupport
{
    public const string Jwt = JwtBearerDefaults.AuthenticationScheme;
    public const string Admins = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS;

    public static string? UserName(ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;

    public static IResult Fail(int status, string message) =>
        Results.Json(new { statusCode = status, message }, statusCode: status);

    public static async Task<IResult> Execute(ClaimsPrincipal? user, bool needsUser, Func<string, Task<IResult>> action)
    {
        var name = user is null ? null : UserName(user);
        if (needsUser && string.IsNullOrWhiteSpace(name))
            return Fail(401, "The token does not identify a user.");
        try
        {
            return await action(name ?? string.Empty);
        }
        catch (BillingException ex)
        {
            return Fail(ex.Status, ex.Message);
        }
    }

    public static bool TryDate(string? text, out DateOnly date) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}

/// <summary>GET /api/plans</summary>
public class ListPlansEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/plans",
            [Authorize(AuthenticationSchemes = BillingEndpointSupport.Jwt)] (ClaimsPrincipal user, MaxioBillingService billing, CancellationToken ct) =>
                BillingEndpointSupport.Execute(user, true, async _ => Results.Ok(await billing.ListPlans(ct))))
            .Produces<ListPlansResponse>()
            .WithTags("BillingEndpoints");
    }
}

/// <summary>POST /api/subscriptions</summary>
public class SubscribeEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = BillingEndpointSupport.Jwt)] (ClaimsPrincipal user, SubscribeRequest request, MaxioBillingService billing, CancellationToken ct) =>
                BillingEndpointSupport.Execute(user, true, async name =>
                {
                    var sub = await billing.Subscribe(name, request.PlanHandle, ct);
                    return Results.Created($"api/subscriptions/{sub.Id}", sub);
                }))
            .Produces<SubscriptionDto>(StatusCodes.Status201Created)
            .WithTags("BillingEndpoints");
    }
}

/// <summary>GET /api/subscriptions/{subscriptionId}</summary>
public class GetSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId}",
            [Authorize(AuthenticationSchemes = BillingEndpointSupport.Jwt)] (ClaimsPrincipal user, int subscriptionId, MaxioBillingService billing, CancellationToken ct) =>
                BillingEndpointSupport.Execute(user, true, async name => Results.Ok(await billing.GetSubscription(name, subscriptionId, ct))))
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");
    }
}

/// <summary>POST /api/subscriptions/{subscriptionId}/pause|resume|cancel|restart|switch</summary>
public class SubscriptionStatusEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/pause",
            [Authorize(AuthenticationSchemes = BillingEndpointSupport.Jwt)] (ClaimsPrincipal user, int subscriptionId, MaxioBillingService billing, CancellationToken ct) =>
                BillingEndpointSupport.Execute(user, true, async name => Results.Ok(await billing.Pause(name, subscriptionId, ct))))
            .Produces<SubscriptionDto>().WithTags("BillingEndpoints");

        app.MapPost("api/subscriptions/{subscriptionId}/resume",
            [Authorize(AuthenticationSchemes = BillingEndpointSupport.Jwt)] (ClaimsPrincipal user, int subscriptionId, MaxioBillingService billing, CancellationToken ct) =>
                BillingEndpointSupport.Execute(user, true, async name => Results.Ok(await billing.Resume(name, subscriptionId, ct))))
            .Produces<SubscriptionDto>().WithTags("BillingEndpoints");

        app.MapPost("api/subscriptions/{subscriptionId}/cancel",
            [Authorize(AuthenticationSchemes = BillingEndpointSupport.Jwt)] (ClaimsPrincipal user, int subscriptionId, MaxioBillingService billing, CancellationToken ct) =>
                BillingEndpointSupport.Execute(user, true, async name => Results.Ok(await billing.Cancel(name, subscriptionId, ct))))
            .Produces<SubscriptionDto>().WithTags("BillingEndpoints");

        app.MapPost("api/subscriptions/{subscriptionId}/restart",
            [Authorize(AuthenticationSchemes = BillingEndpointSupport.Jwt)] (ClaimsPrincipal user, int subscriptionId, MaxioBillingService billing, CancellationToken ct) =>
                BillingEndpointSupport.Execute(user, true, async name => Results.Ok(await billing.Restart(name, subscriptionId, ct))))
            .Produces<SubscriptionDto>().WithTags("BillingEndpoints");

        app.MapPost("api/subscriptions/{subscriptionId}/switch",
            [Authorize(AuthenticationSchemes = BillingEndpointSupport.Jwt)] (ClaimsPrincipal user, int subscriptionId, SwitchPlanRequest request, MaxioBillingService billing, CancellationToken ct) =>
                BillingEndpointSupport.Execute(user, true, async name => Results.Ok(await billing.Switch(name, subscriptionId, request.PlanHandle, ct))))
            .Produces<SubscriptionDto>().WithTags("BillingEndpoints");
    }
}

/// <summary>GET /api/subscriptions/{subscriptionId}/usage?date=YYYY-MM-DD</summary>
public class SubscriptionUsageEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId}/usage",
            [Authorize(AuthenticationSchemes = BillingEndpointSupport.Jwt)] (ClaimsPrincipal user, int subscriptionId, string? date, MaxioBillingService billing, CancellationToken ct) =>
                BillingEndpointSupport.Execute(user, true, async name =>
                {
                    if (!BillingEndpointSupport.TryDate(date, out var day))
                        return BillingEndpointSupport.Fail(400, "date is required and must look like YYYY-MM-DD.");
                    return Results.Ok(await billing.Usage(name, subscriptionId, day, ct));
                }))
            .Produces<UsageResponse>()
            .WithTags("BillingEndpoints");
    }
}

/// <summary>POST /api/admin/plans and POST /api/admin/plans/{planId}/currency-prices</summary>
public class AdminPlanEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans",
            [Authorize(Roles = BillingEndpointSupport.Admins, AuthenticationSchemes = BillingEndpointSupport.Jwt)] (ClaimsPrincipal user, CreatePlanRequest request, MaxioBillingService billing, CancellationToken ct) =>
                BillingEndpointSupport.Execute(user, false, async _ =>
                {
                    var plan = await billing.CreatePlan(request.Name, request.MonthlyPrice, request.Brand, ct);
                    return Results.Created($"api/plans/{plan.Handle}", plan);
                }))
            .Produces<PlanDto>(StatusCodes.Status201Created)
            .WithTags("BillingEndpoints");

        app.MapPost("api/admin/plans/{planId}/currency-prices",
            [Authorize(Roles = BillingEndpointSupport.Admins, AuthenticationSchemes = BillingEndpointSupport.Jwt)] (ClaimsPrincipal user, int planId, CurrencyPriceRequest request, MaxioBillingService billing, CancellationToken ct) =>
                BillingEndpointSupport.Execute(user, false, async _ =>
                {
                    var price = await billing.AddCurrencyPrice(planId, request.Currency, request.Price, ct);
                    return Results.Created($"api/admin/plans/{planId}/currency-prices/{price.Currency}", price);
                }))
            .Produces<CurrencyPriceDto>(StatusCodes.Status201Created)
            .WithTags("BillingEndpoints");
    }
}

/// <summary>PUT /api/admin/subscriptions/{subscriptionId}/next-billing-date</summary>
public class SetNextBillingDateEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/admin/subscriptions/{subscriptionId}/next-billing-date",
            [Authorize(Roles = BillingEndpointSupport.Admins, AuthenticationSchemes = BillingEndpointSupport.Jwt)] (ClaimsPrincipal user, int subscriptionId, NextBillingDateRequest request, MaxioBillingService billing, CancellationToken ct) =>
                BillingEndpointSupport.Execute(user, false, async _ =>
                {
                    if (!BillingEndpointSupport.TryDate(request.Date, out var day))
                        return BillingEndpointSupport.Fail(400, "date is required and must look like YYYY-MM-DD.");
                    return Results.Ok(await billing.SetNextBillingDate(subscriptionId, day, ct));
                }))
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");
    }
}
