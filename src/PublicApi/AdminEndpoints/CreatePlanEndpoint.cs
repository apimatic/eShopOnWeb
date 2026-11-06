using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.AdminEndpoints;

public class CreatePlanRequest
{
    public string? Name { get; set; }
    /// <summary>Monthly price.</summary>
    public decimal? Price { get; set; }
    /// <summary>Name of one of the brands in the shop catalog.</summary>
    public string? Brand { get; set; }
}

/// <summary>
/// Adds a plan (staff)
/// </summary>
public class CreatePlanEndpoint : IEndpoint<IResult, CreatePlanRequest, BillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePlanRequest request, BillingService billing) =>
            {
                return await HandleAsync(request, billing);
            })
            .Produces<PlanDto>(StatusCodes.Status201Created)
            .WithTags("AdminEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePlanRequest request, BillingService billing)
    {
        var plan = await billing.CreatePlanAsync(request?.Name, request?.Price, request?.Brand);
        return Results.Created($"api/plans/{plan.Handle}", plan);
    }
}
