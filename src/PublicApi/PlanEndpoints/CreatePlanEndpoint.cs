using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PlanEndpoints;

/// <summary>
/// Staff: adds a plan with its name, monthly price and catalog brand
/// </summary>
public class CreatePlanEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePlanRequest? request, MaxioBillingService billing, CancellationToken ct) =>
            {
                if (request is null) throw BillingException.BadRequest("A request body is required.");
                var plan = await billing.CreatePlanAsync(request.Name, request.Price, request.Brand, request.CatalogBrandId, ct);
                return Results.Created("api/plans", plan);
            })
            .Produces<PlanDto>(StatusCodes.Status201Created)
            .WithTags("PlanEndpoints");
    }
}
