using System;
using System.Linq;
using System.Threading.Tasks;
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

public class CreatePlanRequest : BaseRequest
{
    public string? Name { get; set; }
    public decimal? MonthlyPrice { get; set; }
    /// <summary>Id of one of the catalog's brands.</summary>
    public int? BrandId { get; set; }
    /// <summary>Name of one of the catalog's brands (alternative to <see cref="BrandId"/>).</summary>
    public string? Brand { get; set; }
}

/// <summary>
/// Adds a plan (staff only)
/// </summary>
public class CreatePlanEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePlanRequest request, MaxioBillingService billing, IRepository<CatalogBrand> brandRepository) =>
            {
                return await HandleAsync(request, billing, brandRepository);
            })
            .Produces<PlanDto>(StatusCodes.Status201Created)
            .WithTags(BillingEndpointHelpers.TAG);
    }

    public async Task<IResult> HandleAsync(CreatePlanRequest request, MaxioBillingService billing, IRepository<CatalogBrand> brandRepository)
    {
        var brands = await brandRepository.ListAsync();
        CatalogBrand? brand;
        if (request.BrandId != null)
            brand = brands.FirstOrDefault(b => b.Id == request.BrandId);
        else if (!string.IsNullOrWhiteSpace(request.Brand))
            brand = brands.FirstOrDefault(b => string.Equals(b.Brand, request.Brand.Trim(), StringComparison.OrdinalIgnoreCase))
                    ?? (int.TryParse(request.Brand, out var id) ? brands.FirstOrDefault(b => b.Id == id) : null);
        else
            throw BillingException.BadRequest("A plan needs a brand from the catalog (brandId or brand).");

        if (brand == null)
            throw BillingException.Unprocessable("The brand is not one of the brands in the catalog.");

        var plan = await billing.CreatePlanAsync(request.Name, request.MonthlyPrice, brand.Brand);
        return Results.Created($"api/plans/{plan.Handle}", plan);
    }
}
