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

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

public record CreatePlanRequest(string? Name, decimal MonthlyPrice, int BrandId);

/// <summary>
/// Adds a plan (staff only)
/// </summary>
public class CreatePlanEndpoint : IEndpoint<IResult, CreatePlanRequest, MaxioGateway>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePlanRequest request, MaxioGateway billing) =>
            {
                return await HandleAsync(request, billing);
            })
            .Produces<PlanDto>(StatusCodes.Status201Created)
            .WithTags("AdminBillingEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePlanRequest request, MaxioGateway billing)
    {
        var plan = await billing.CreatePlanAsync(request.Name, request.MonthlyPrice, request.BrandId);
        return Results.Created("api/plans", plan);
    }
}
