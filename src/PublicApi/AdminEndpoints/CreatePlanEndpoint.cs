using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.AdminEndpoints;

public class CreatePlanRequest
{
    public string? Name { get; set; }
    /// <summary>Monthly price in the site's currency, e.g. 19.99.</summary>
    public decimal? Price { get; set; }
    public decimal? MonthlyPrice { get; set; }
    /// <summary>Name of one of the catalog's brands.</summary>
    public string? Brand { get; set; }
    public int? BrandId { get; set; }
}

/// <summary>
/// Adds a plan (staff only)
/// </summary>
public class CreatePlanEndpoint : IEndpoint<IResult, CreatePlanRequest, MaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (CreatePlanRequest request, MaxioBillingService billing) =>
                await HandleAsync(request, billing))
            .Produces<PlanDto>(StatusCodes.Status201Created)
            .WithTags("AdminEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePlanRequest request, MaxioBillingService billing)
    {
        var plan = await billing.CreatePlanAsync(request.Name, request.Price ?? request.MonthlyPrice, request.Brand, request.BrandId);
        return Results.Created("api/plans", plan);
    }
}
