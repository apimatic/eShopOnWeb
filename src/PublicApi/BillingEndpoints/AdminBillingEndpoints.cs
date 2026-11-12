using System;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

/// <summary>
/// Adds a plan (staff only)
/// </summary>
public class CreatePlanEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePlanRequest request, PlanAdminService admin, CancellationToken ct) =>
            {
                var plan = await admin.CreatePlanAsync(request.Name, request.Price ?? request.MonthlyPrice, request.Brand, request.BrandId, request.Description, ct);
                return Results.Created($"api/plans#{plan.Handle}", plan);
            })
            .Produces<PlanDto>(StatusCodes.Status201Created)
            .WithTags("AdminBillingEndpoints");
    }
}

public class CreatePlanRequest
{
    public string? Name { get; set; }
    /// <summary>Monthly price in the site's currency.</summary>
    public decimal? Price { get; set; }
    /// <summary>Alias of <see cref="Price"/>.</summary>
    public decimal? MonthlyPrice { get; set; }
    /// <summary>Name of one of the catalog's brands.</summary>
    public string? Brand { get; set; }
    /// <summary>Id of one of the catalog's brands (alternative to <see cref="Brand"/>).</summary>
    public int? BrandId { get; set; }
    public string? Description { get; set; }
}

/// <summary>
/// Prices a plan in another currency (staff only)
/// </summary>
public class CreatePlanCurrencyPriceEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans/{planId:int}/currency-prices",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int planId, CurrencyPriceRequest request, PlanAdminService admin, CancellationToken ct) =>
                Results.Created($"api/admin/plans/{planId}/currency-prices", await admin.AddCurrencyPriceAsync(planId, request.Currency, request.Price, ct)))
            .WithTags("AdminBillingEndpoints");
    }
}

public class CurrencyPriceRequest
{
    public string? Currency { get; set; }
    public decimal? Price { get; set; }
}

/// <summary>
/// Sets the date a subscriber is next billed (staff only)
/// </summary>
public class SetNextBillingDateEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/admin/subscriptions/{subscriptionId:int}/next-billing-date",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, NextBillingDateRequest request, SubscriptionService subscriptions, CancellationToken ct) =>
            {
                var date = request.NextBillingDate ?? request.Date
                    ?? throw BillingException.BadRequest("A next billing date (YYYY-MM-DD) is required.");
                return Results.Ok(await subscriptions.SetNextBillingDateAsync(subscriptionId, date, ct));
            })
            .Produces<SubscriptionDto>()
            .WithTags("AdminBillingEndpoints");
    }
}

public class NextBillingDateRequest
{
    public DateOnly? NextBillingDate { get; set; }
    /// <summary>Alias of <see cref="NextBillingDate"/>.</summary>
    public DateOnly? Date { get; set; }
}
