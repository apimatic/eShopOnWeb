using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

public class SubscribeRequest
{
    public string? PlanHandle { get; set; }
}

public class CreatePlanRequest
{
    public string? Name { get; set; }
    public decimal? MonthlyPrice { get; set; }
    public string? Brand { get; set; }
}

public class CurrencyPriceRequest
{
    public string? Currency { get; set; }
    public decimal? Price { get; set; }
}

public class NextBillingDateRequest
{
    public string? Date { get; set; }
}

internal static class BillingAuth
{
    public const string ADMIN = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS;
    public const string SCHEME = JwtBearerDefaults.AuthenticationScheme;

    public static string UserName(ClaimsPrincipal user) =>
        user.Identity?.Name is { Length: > 0 } name
            ? name
            : throw new BillingException(HttpStatusCode.Unauthorized, "The token carries no user name.");
}

/// <summary>Maps all billing routes. Shopper routes need any signed-in user; /api/admin routes need the administrator role.</summary>
public class BillingRoutes : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        // 1. Plans
        app.MapGet("api/plans",
                [Authorize(AuthenticationSchemes = BillingAuth.SCHEME)] async (MaxioBilling billing, CancellationToken ct) =>
                    Results.Ok(await billing.ListPlansAsync(ct)))
            .WithTags("BillingEndpoints");

        // 2. Subscribe
        app.MapPost("api/subscriptions",
                [Authorize(AuthenticationSchemes = BillingAuth.SCHEME)] async (SubscribeRequest request, ClaimsPrincipal user, MaxioBilling billing, CancellationToken ct) =>
                {
                    var sub = await billing.SubscribeAsync(BillingAuth.UserName(user), request.PlanHandle, ct);
                    return Results.Created($"api/subscriptions/{sub.Id}", sub);
                })
            .WithTags("BillingEndpoints");

        // 3. Subscription page
        app.MapGet("api/subscriptions/{subscriptionId:int}",
                [Authorize(AuthenticationSchemes = BillingAuth.SCHEME)] async (int subscriptionId, ClaimsPrincipal user, MaxioBilling billing, CancellationToken ct) =>
                    Results.Ok(await billing.GetAsync(BillingAuth.UserName(user), subscriptionId, ct)))
            .WithTags("BillingEndpoints");

        // 4. Pause and resume
        app.MapPost("api/subscriptions/{subscriptionId:int}/pause",
                [Authorize(AuthenticationSchemes = BillingAuth.SCHEME)] async (int subscriptionId, ClaimsPrincipal user, MaxioBilling billing, CancellationToken ct) =>
                    Results.Ok(await billing.PauseAsync(BillingAuth.UserName(user), subscriptionId, ct)))
            .WithTags("BillingEndpoints");
        app.MapPost("api/subscriptions/{subscriptionId:int}/resume",
                [Authorize(AuthenticationSchemes = BillingAuth.SCHEME)] async (int subscriptionId, ClaimsPrincipal user, MaxioBilling billing, CancellationToken ct) =>
                    Results.Ok(await billing.ResumeAsync(BillingAuth.UserName(user), subscriptionId, ct)))
            .WithTags("BillingEndpoints");

        // 5. Cancel and restart
        app.MapPost("api/subscriptions/{subscriptionId:int}/cancel",
                [Authorize(AuthenticationSchemes = BillingAuth.SCHEME)] async (int subscriptionId, ClaimsPrincipal user, MaxioBilling billing, CancellationToken ct) =>
                    Results.Ok(await billing.CancelAsync(BillingAuth.UserName(user), subscriptionId, ct)))
            .WithTags("BillingEndpoints");
        app.MapPost("api/subscriptions/{subscriptionId:int}/restart",
                [Authorize(AuthenticationSchemes = BillingAuth.SCHEME)] async (int subscriptionId, ClaimsPrincipal user, MaxioBilling billing, CancellationToken ct) =>
                    Results.Ok(await billing.RestartAsync(BillingAuth.UserName(user), subscriptionId, ct)))
            .WithTags("BillingEndpoints");

        // 6. Switch plan
        app.MapPost("api/subscriptions/{subscriptionId:int}/switch",
                [Authorize(AuthenticationSchemes = BillingAuth.SCHEME)] async (int subscriptionId, SubscribeRequest request, ClaimsPrincipal user, MaxioBilling billing, CancellationToken ct) =>
                    Results.Ok(await billing.SwitchAsync(BillingAuth.UserName(user), subscriptionId, request.PlanHandle, ct)))
            .WithTags("BillingEndpoints");

        // 7. Usage for a day
        app.MapGet("api/subscriptions/{subscriptionId:int}/usage",
                [Authorize(AuthenticationSchemes = BillingAuth.SCHEME)] async (int subscriptionId, string? date, ClaimsPrincipal user, MaxioBilling billing, CancellationToken ct) =>
                {
                    if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                    {
                        throw new BillingException(HttpStatusCode.BadRequest, "Query parameter 'date' is required and must look like YYYY-MM-DD.");
                    }

                    return Results.Ok(await billing.GetUsageAsync(BillingAuth.UserName(user), subscriptionId, day, ct));
                })
            .WithTags("BillingEndpoints");

        // 8. New plans (staff)
        app.MapPost("api/admin/plans",
                [Authorize(Roles = BillingAuth.ADMIN, AuthenticationSchemes = BillingAuth.SCHEME)] async (CreatePlanRequest request, IRepository<CatalogBrand> brands, MaxioBilling billing, CancellationToken ct) =>
                {
                    if (string.IsNullOrWhiteSpace(request.Name))
                    {
                        throw BillingException.Invalid("A plan name is required.");
                    }

                    if (request.MonthlyPrice == null)
                    {
                        throw BillingException.Invalid("A monthly price is required.");
                    }

                    if (string.IsNullOrWhiteSpace(request.Brand))
                    {
                        throw BillingException.Invalid("A brand is required.");
                    }

                    var catalogBrand = (await brands.ListAsync(ct))
                        .FirstOrDefault(b => string.Equals(b.Brand, request.Brand.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (catalogBrand == null)
                    {
                        throw BillingException.Invalid($"'{request.Brand}' is not a brand in the catalog.");
                    }

                    var plan = await billing.CreatePlanAsync(request.Name, request.MonthlyPrice.Value, catalogBrand.Brand, ct);
                    return Results.Created($"api/plans/{plan.Id}", plan);
                })
            .WithTags("BillingEndpoints");

        // 9. Price in another currency (staff)
        app.MapPost("api/admin/plans/{planId:int}/currency-prices",
                [Authorize(Roles = BillingAuth.ADMIN, AuthenticationSchemes = BillingAuth.SCHEME)] async (int planId, CurrencyPriceRequest request, MaxioBilling billing, CancellationToken ct) =>
                {
                    if (request.Price == null)
                    {
                        throw BillingException.Invalid("A price is required.");
                    }

                    var created = await billing.AddCurrencyPriceAsync(planId, request.Currency, request.Price.Value, ct);
                    return Results.Created($"api/admin/plans/{planId}/currency-prices", created);
                })
            .WithTags("BillingEndpoints");

        // 10. Next billing date (staff)
        app.MapPut("api/admin/subscriptions/{subscriptionId:int}/next-billing-date",
                [Authorize(Roles = BillingAuth.ADMIN, AuthenticationSchemes = BillingAuth.SCHEME)] async (int subscriptionId, NextBillingDateRequest request, MaxioBilling billing, CancellationToken ct) =>
                {
                    DateTimeOffset when;
                    if (DateOnly.TryParseExact(request.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                    {
                        // A bare date is billed at noon UTC so that it falls on that calendar day in every time zone.
                        when = new DateTimeOffset(day.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
                    }
                    else if (!DateTimeOffset.TryParse(request.Date, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out when))
                    {
                        throw new BillingException(HttpStatusCode.BadRequest, "'date' is required and must be a YYYY-MM-DD date or an ISO 8601 date and time.");
                    }

                    return Results.Ok(await billing.SetNextBillingDateAsync(subscriptionId, when, ct));
                })
            .WithTags("BillingEndpoints");
    }
}
