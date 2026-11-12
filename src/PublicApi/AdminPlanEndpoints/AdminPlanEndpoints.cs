using System;
using System.Globalization;
using System.Linq;
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

namespace Microsoft.eShopWeb.PublicApi.AdminPlanEndpoints;

public class CreatePlanRequest
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Monthly price, in currency units (e.g. 12.50).</summary>
    public decimal MonthlyPrice { get; set; }

    /// <summary>Id of a brand in the shop's catalog (alternative to <see cref="Brand"/>).</summary>
    public int? BrandId { get; set; }

    /// <summary>Name of a brand in the shop's catalog (alternative to <see cref="BrandId"/>).</summary>
    public string? Brand { get; set; }
}

public class CurrencyPriceRequest
{
    /// <summary>Three-letter ISO currency code.</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>Whole number of currency units.</summary>
    public decimal Price { get; set; }
}

public class NextBillingDateRequest
{
    /// <summary>The date, YYYY-MM-DD.</summary>
    public string Date { get; set; } = string.Empty;
}

/// <summary>
/// Staff add a plan with a name, a monthly price and a catalog brand
/// </summary>
public class CreatePlanEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePlanRequest request, IRepository<CatalogBrand> brands, IBillingService billing, CancellationToken ct) =>
            {
                if (request.BrandId == null && string.IsNullOrWhiteSpace(request.Brand))
                {
                    throw BillingException.BadRequest("A brand (brandId or brand) is required.");
                }
                var catalog = await brands.ListAsync(ct);
                var brand = catalog.FirstOrDefault(b => request.BrandId != null
                    ? b.Id == request.BrandId
                    : string.Equals(b.Brand, request.Brand!.Trim(), StringComparison.OrdinalIgnoreCase));
                if (brand == null)
                {
                    throw BillingException.BadRequest("The brand is not one of the brands in the shop's catalog.");
                }
                var plan = await billing.CreatePlanAsync(request.Name, request.MonthlyPrice, brand.Brand, ct);
                return Results.Created($"api/plans/{plan.Handle}", plan);
            })
            .Produces<PlanDto>(StatusCodes.Status201Created)
            .WithTags("AdminPlanEndpoints");
    }
}

/// <summary>
/// Staff price a plan in another currency
/// </summary>
public class AddCurrencyPriceEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans/{planId:int}/currency-prices",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int planId, CurrencyPriceRequest request, IBillingService billing, CancellationToken ct) =>
                Results.Created($"api/admin/plans/{planId}/currency-prices", await billing.AddCurrencyPriceAsync(planId, request.Currency, request.Price, ct)))
            .Produces<CurrencyPriceDto>(StatusCodes.Status201Created)
            .WithTags("AdminPlanEndpoints");
    }
}

/// <summary>
/// Staff set the date a subscriber is next billed
/// </summary>
public class SetNextBillingDateEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/admin/subscriptions/{subscriptionId:int}/next-billing-date",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, NextBillingDateRequest request, IBillingService billing, CancellationToken ct) =>
            {
                if (!DateOnly.TryParseExact(request.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                {
                    throw BillingException.BadRequest("'date' is required, in the form YYYY-MM-DD.");
                }
                return Results.Ok(await billing.SetNextBillingDateAsync(subscriptionId, day, ct));
            })
            .Produces<NextBillingDateDto>()
            .WithTags("AdminPlanEndpoints");
    }
}
