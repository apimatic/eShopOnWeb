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
/// Staff: adds a plan (a monthly price within one of the catalog brands)
/// </summary>
public class CreatePlanEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePlanRequest request, CancellationToken ct, BillingService billing) =>
            {
                var plan = await billing.CreatePlanAsync(request, ct);
                return Results.Created($"api/admin/plans/{plan.Id}", plan);
            })
            .Produces<PlanDto>()
            .WithTags("PlanEndpoints");
    }
}
