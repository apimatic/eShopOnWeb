using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

public class CreatePlanRequest
{
    public string? Name { get; set; }
    /// <summary>The price per month.</summary>
    public decimal? MonthlyPrice { get; set; }
    /// <summary>The id of one of the brands in the shop's catalog.</summary>
    public int? BrandId { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
}

/// <summary>
/// Adds a plan for a catalog brand
/// </summary>
public class CreatePlanEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePlanRequest request, IBillingService billing, IRepository<CatalogBrand> brands, CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(request?.Name))
                    throw BillingException.BadRequest("name is required.");
                if (request.Name.Length > 100)
                    throw BillingException.BadRequest("name must be at most 100 characters.");
                if (request.MonthlyPrice is null || request.MonthlyPrice <= 0)
                    throw BillingException.BadRequest("monthlyPrice is required and must be greater than zero.");
                if (request.BrandId is null)
                    throw BillingException.BadRequest("brandId is required.");
                var brand = await brands.GetByIdAsync(request.BrandId.Value, ct);
                if (brand is null)
                    throw BillingException.BadRequest($"Brand {request.BrandId} is not a brand in the catalog.");

                var plan = await billing.CreatePlanAsync(request.Name, request.MonthlyPrice.Value, brand.Brand, request.Handle, request.Description, ct);
                return Results.Created($"api/plans/{plan.Handle}", plan);
            })
            .Produces<PlanDto>(StatusCodes.Status201Created)
            .WithTags("BillingEndpoints");
    }
}
