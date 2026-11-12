using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Billing.Endpoints;

/// <summary>
/// Staff: adds a plan
/// </summary>
public class CreatePlanEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePlanRequest request, ClaimsPrincipal user, BillingService service, CancellationToken ct) =>
            {
                return await HandleAsync(request, user, service, ct);
            })
            .Produces<PlanDto>()
            .WithTags("BillingEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePlanRequest request, ClaimsPrincipal user, BillingService service, CancellationToken ct)
    {
        var dto = await service.CreatePlanAsync(request.Name, request.MonthlyPrice, request.Brand, ct);
        return Results.Created($"api/plans", dto);
    }
}
