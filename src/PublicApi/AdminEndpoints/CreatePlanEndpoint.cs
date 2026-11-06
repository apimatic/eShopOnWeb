using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.AdminEndpoints;

public class CreatePlanRequest : BaseRequest
{
    public string? Name { get; set; }
    public decimal? MonthlyPrice { get; set; }

    /// <summary>The id of one of the shop's catalog brands (or use <see cref="Brand"/>).</summary>
    public int? BrandId { get; set; }

    /// <summary>The name of one of the shop's catalog brands (or use <see cref="BrandId"/>).</summary>
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
            (CreatePlanRequest request, IBillingService billing, CancellationToken ct) =>
            {
                return await HandleAsync(request, billing, ct);
            })
            .Produces<PlanDto>(StatusCodes.Status201Created)
            .WithTags("AdminEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePlanRequest request, IBillingService billing, CancellationToken ct)
    {
        var plan = await billing.CreatePlanAsync(request.Name, request.MonthlyPrice, request.BrandId, request.Brand, ct);
        return Results.Created($"api/plans/{plan.Handle}", plan);
    }
}
