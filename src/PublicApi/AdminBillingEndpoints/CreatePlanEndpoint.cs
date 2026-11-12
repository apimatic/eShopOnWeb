using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.PlanEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.AdminBillingEndpoints;

public class CreatePlanRequest : BaseRequest
{
    public string? Name { get; set; }

    /// <summary>Monthly price, in the site's currency (for example 12.50).</summary>
    public decimal? Price { get; set; }

    /// <summary>The catalog brand the plan belongs to.</summary>
    public int? CatalogBrandId { get; set; }

    /// <summary>Alias of <see cref="CatalogBrandId"/>.</summary>
    public int? BrandId { get; set; }
}

public class CreatePlanResponse : BaseResponse
{
    public CreatePlanResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreatePlanResponse()
    {
    }

    public PlanDto? Plan { get; set; }
}

/// <summary>
/// Creates a new plan for a catalog brand (staff only)
/// </summary>
public class CreatePlanEndpoint : IEndpoint<IResult, CreatePlanRequest, PlanCatalog>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePlanRequest request, PlanCatalog catalog, HttpContext http) =>
            {
                return await HandleAsync(request, catalog, http.RequestAborted);
            })
            .Produces<CreatePlanResponse>(StatusCodes.Status201Created)
            .WithTags("AdminBillingEndpoints");
    }

    public Task<IResult> HandleAsync(CreatePlanRequest request, PlanCatalog catalog)
        => HandleAsync(request, catalog, default);

    public async Task<IResult> HandleAsync(CreatePlanRequest request, PlanCatalog catalog, System.Threading.CancellationToken ct)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name)) throw MaxioProblemException.BadRequest("name is required.");
        if (name.Length > 100) throw MaxioProblemException.BadRequest("name must be at most 100 characters.");

        if (request.Price is null) throw MaxioProblemException.BadRequest("price is required.");
        var price = request.Price.Value;
        if (price < 0 || price > 1_000_000) throw MaxioProblemException.BadRequest("price must be between 0 and 1000000.");
        if (decimal.Round(price, 2) != price) throw MaxioProblemException.BadRequest("price can have at most 2 decimals.");

        var brandId = request.CatalogBrandId ?? request.BrandId;
        if (brandId is null) throw MaxioProblemException.BadRequest("catalogBrandId is required.");

        var plan = await catalog.CreateAsync(name, (long)decimal.Round(price * 100m), brandId.Value, ct);
        var response = new CreatePlanResponse(request.CorrelationId()) { Plan = PlanDto.From(plan) };
        return Results.Created($"api/plans#{plan.Id}", response);
    }
}
