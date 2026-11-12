using System;
using System.Linq;
using System.Threading;
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

public class CreatePlanRequest : BaseRequest
{
    public string? Name { get; set; }
    /// <summary>Monthly price in the site's default currency, e.g. 9.99</summary>
    public decimal? MonthlyPrice { get; set; }
    public decimal? Price { get; set; }
    /// <summary>Id of a brand of the shop's catalog.</summary>
    public int? BrandId { get; set; }
    /// <summary>Name of a brand of the shop's catalog (alternative to BrandId).</summary>
    public string? Brand { get; set; }
    public string? Description { get; set; }
}

/// <summary>
/// Staff: adds a plan
/// </summary>
public class AdminCreatePlanEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePlanRequest? request, IReadRepository<CatalogBrand> brands, IMaxioBillingService billing, CancellationToken ct) =>
            {
                if (request == null || string.IsNullOrWhiteSpace(request.Name))
                {
                    throw new BillingException(400, "A plan name is required.");
                }

                var price = request.MonthlyPrice ?? request.Price
                    ?? throw new BillingException(400, "A monthly price is required.");
                if (price < 0 || price != decimal.Round(price, 2))
                {
                    throw new BillingException(400, "The monthly price must not be negative and has at most two decimals.");
                }

                if (request.BrandId == null && string.IsNullOrWhiteSpace(request.Brand))
                {
                    throw new BillingException(400, "A brand (brandId or brand name) is required.");
                }

                var all = await brands.ListAsync(ct);
                var brand = request.BrandId != null
                    ? all.FirstOrDefault(b => b.Id == request.BrandId)
                    : all.FirstOrDefault(b => string.Equals(b.Brand.Trim(), request.Brand!.Trim(), StringComparison.OrdinalIgnoreCase));
                if (brand == null)
                {
                    throw new BillingException(400, "The brand is not one of the brands of the shop's catalog.");
                }

                var plan = await billing.CreatePlanAsync(request.Name, price, brand.Id, brand.Brand, request.Description, ct);
                return Results.Created("api/plans", plan);
            })
            .Produces<PlanDto>(StatusCodes.Status201Created)
            .WithTags("BillingEndpoints");
    }
}

public class CurrencyPriceRequest : BaseRequest
{
    public string? Currency { get; set; }
    public decimal? Price { get; set; }
}

/// <summary>
/// Staff: prices a plan in another currency
/// </summary>
public class AdminCurrencyPriceEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans/{planId:int}/currency-prices",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int planId, CurrencyPriceRequest? request, IMaxioBillingService billing, CancellationToken ct) =>
            {
                var currency = request?.Currency?.Trim().ToUpperInvariant();
                if (currency == null || currency.Length != 3 || !currency.All(char.IsLetter))
                {
                    throw new BillingException(400, "A three-letter currency code (e.g. EUR) is required.");
                }

                if (request?.Price == null || request.Price <= 0)
                {
                    throw new BillingException(400, "A price greater than zero is required.");
                }

                return Results.Ok(await billing.SetCurrencyPriceAsync(planId, currency, request.Price.Value, ct));
            })
            .Produces<CurrencyPriceDto>()
            .WithTags("BillingEndpoints");
    }
}

public class NextBillingDateRequest : BaseRequest
{
    /// <summary>YYYY-MM-DD</summary>
    public string? Date { get; set; }
    public string? NextBillingDate { get; set; }
}

/// <summary>
/// Staff: sets the date a subscriber is next billed
/// </summary>
public class AdminNextBillingDateEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/admin/subscriptions/{subscriptionId:int}/next-billing-date",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, NextBillingDateRequest? request, IMaxioBillingService billing, CancellationToken ct) =>
            {
                var date = BillingEndpointSupport.ParseDate(request?.Date ?? request?.NextBillingDate, "The date");
                return Results.Ok(await billing.SetNextBillingDateAsync(subscriptionId, date, ct));
            })
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");
    }
}
