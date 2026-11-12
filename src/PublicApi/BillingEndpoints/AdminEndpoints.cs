using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

public class CreatePlanRequestBody
{
    public string? Name { get; set; }
    /// <summary>Monthly price, in the billing site's currency.</summary>
    public decimal? Price { get; set; }
    public decimal? MonthlyPrice { get; set; }
    /// <summary>Id of the catalog brand the plan belongs to.</summary>
    public int? BrandId { get; set; }
    /// <summary>Name of the catalog brand, as an alternative to its id.</summary>
    public string? Brand { get; set; }
}

public class CurrencyPriceRequestBody
{
    public string? Currency { get; set; }
    public decimal? Price { get; set; }
}

public class NextBillingDateRequestBody
{
    /// <summary>YYYY-MM-DD</summary>
    public string? Date { get; set; }
    public string? NextBillingDate { get; set; }
}

public class AdminRequest : BaseRequest
{
    public int Id { get; set; }
    public CreatePlanRequestBody? Plan { get; set; }
    public CurrencyPriceRequestBody? CurrencyPrice { get; set; }
    public string? Date { get; set; }
    public CancellationToken Cancellation { get; set; }
}

/// <summary>Staff: add a plan, belonging to one of the catalog's brands</summary>
public class CreatePlanEndpoint : IEndpoint<IResult, AdminRequest, MaxioBillingService, IRepository<CatalogBrand>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePlanRequestBody body, MaxioBillingService billing, IRepository<CatalogBrand> brands, CancellationToken ct) =>
                await HandleAsync(new AdminRequest { Plan = body, Cancellation = ct }, billing, brands))
            .Produces<PlanDto>(StatusCodes.Status201Created)
            .WithTags("SubscriptionAdminEndpoints");
    }

    public async Task<IResult> HandleAsync(AdminRequest request, MaxioBillingService billing, IRepository<CatalogBrand> brands)
    {
        var body = request.Plan ?? new CreatePlanRequestBody();
        var price = body.Price ?? body.MonthlyPrice
            ?? throw new BillingException(HttpStatusCode.BadRequest, "A monthly price is required.");

        // the brand must be one of the shop's catalog brands
        var all = await brands.ListAsync(request.Cancellation);
        CatalogBrand? brand = null;
        if (body.BrandId is { } brandId)
            brand = all.FirstOrDefault(b => b.Id == brandId);
        else if (!string.IsNullOrWhiteSpace(body.Brand))
            brand = all.FirstOrDefault(b => string.Equals(b.Brand, body.Brand.Trim(), StringComparison.OrdinalIgnoreCase))
                    ?? (int.TryParse(body.Brand, out var numeric) ? all.FirstOrDefault(b => b.Id == numeric) : null);
        else
            throw new BillingException(HttpStatusCode.BadRequest, "A brand is required.");
        if (brand is null)
            throw new BillingException(HttpStatusCode.UnprocessableEntity, "The brand is not one of the catalog's brands.");

        var plan = await billing.CreatePlanAsync(body.Name, price, brand.Id, brand.Brand, request.Cancellation);
        return Results.Created($"api/plans/{plan.Handle}", plan);
    }
}

/// <summary>Staff: price a plan in another currency</summary>
public class AddCurrencyPriceEndpoint : IEndpoint<IResult, AdminRequest, MaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans/{planId}/currency-prices",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int planId, CurrencyPriceRequestBody body, MaxioBillingService billing, CancellationToken ct) =>
                await HandleAsync(new AdminRequest { Id = planId, CurrencyPrice = body, Cancellation = ct }, billing))
            .Produces<CurrencyPriceDto>(StatusCodes.Status201Created)
            .WithTags("SubscriptionAdminEndpoints");
    }

    public async Task<IResult> HandleAsync(AdminRequest request, MaxioBillingService billing)
    {
        var body = request.CurrencyPrice ?? new CurrencyPriceRequestBody();
        var price = body.Price ?? throw new BillingException(HttpStatusCode.BadRequest, "A price is required.");
        var result = await billing.AddCurrencyPriceAsync(request.Id, body.Currency, price, request.Cancellation);
        return Results.Created($"api/admin/plans/{result.PlanId}/currency-prices/{result.Currency}", result);
    }
}

/// <summary>Staff: set the date a subscriber is next billed</summary>
public class SetNextBillingDateEndpoint : IEndpoint<IResult, AdminRequest, MaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/admin/subscriptions/{subscriptionId}/next-billing-date",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, NextBillingDateRequestBody body, MaxioBillingService billing, CancellationToken ct) =>
                await HandleAsync(new AdminRequest
                {
                    Id = subscriptionId,
                    Date = body?.Date ?? body?.NextBillingDate,
                    Cancellation = ct,
                }, billing))
            .Produces<SubscriptionDto>()
            .WithTags("SubscriptionAdminEndpoints");
    }

    public async Task<IResult> HandleAsync(AdminRequest request, MaxioBillingService billing)
    {
        if (!DateOnly.TryParseExact(request.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new BillingException(HttpStatusCode.BadRequest, "The 'date' is required, as YYYY-MM-DD.");
        return Results.Ok(await billing.SetNextBillingDateAsync(request.Id, date, request.Cancellation));
    }
}
