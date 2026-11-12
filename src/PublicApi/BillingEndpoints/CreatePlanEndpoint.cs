using System;
using System.Security.Claims;
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

public class CreatePlanRequest : BaseRequest
{
    public string? Name { get; set; }

    /// <summary>The monthly price in major currency units (for example 12.50).</summary>
    public decimal? Price { get; set; }
    public decimal? MonthlyPrice { get; set; }

    /// <summary>The catalog brand's id.</summary>
    public int? BrandId { get; set; }

    /// <summary>The catalog brand, by id or by name, when BrandId is not given.</summary>
    public string? Brand { get; set; }
}

/// <summary>
/// Adds a plan under one of the catalog brands
/// </summary>
public class CreatePlanEndpoint : IEndpoint<IResult, CreatePlanRequest, IReadRepository<CatalogBrand>>
{
    private readonly PlanService _plans;

    public CreatePlanEndpoint(PlanService plans)
    {
        _plans = plans;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (CreatePlanRequest request, IReadRepository<CatalogBrand> brands) =>
            {
                return await HandleAsync(request, brands);
            })
            .Produces<PlanDto>(StatusCodes.Status201Created)
            .WithTags("BillingEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePlanRequest request, IReadRepository<CatalogBrand> brands)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new BillingException(400, "The plan needs a name.");
        }

        var price = request.Price ?? request.MonthlyPrice;
        if (price is null || price < 0 || price != decimal.Round(price.Value, 2))
        {
            throw new BillingException(400, "The plan needs a monthly price: zero or more, with at most two decimals.");
        }

        // Each plan belongs to one of the brands in the shop's catalog.
        var brand = await FindBrandAsync(request, brands);

        var plan = await _plans.CreatePlanAsync(request.Name.Trim(), (long)decimal.Round(price.Value * 100m), brand);
        return Results.Created($"api/plans/{plan.Handle}", plan);
    }

    private static async Task<CatalogBrand> FindBrandAsync(CreatePlanRequest request, IReadRepository<CatalogBrand> brands)
    {
        var id = request.BrandId;
        if (id is null && int.TryParse(request.Brand, out var parsed))
        {
            id = parsed;
        }

        CatalogBrand? brand;
        if (id is not null)
        {
            brand = await brands.GetByIdAsync(id.Value);
        }
        else if (!string.IsNullOrWhiteSpace(request.Brand))
        {
            var wanted = request.Brand.Trim();
            brand = (await brands.ListAsync()).Find(b => string.Equals(b.Brand, wanted, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            throw new BillingException(400, "The plan needs a brand from the catalog.");
        }

        return brand ?? throw new BillingException(400, "That brand is not in the catalog.");
    }
}
