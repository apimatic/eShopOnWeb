using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

/// <summary>
/// Staff: adds a plan
/// </summary>
public class CreatePlanEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (CreatePlanRequest request, BillingService billing, CancellationToken ct) =>
                await HandleAsync(request, billing, ct))
            .Produces<PlanDto>(StatusCodes.Status201Created)
            .WithTags("BillingEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePlanRequest request, BillingService billing, CancellationToken ct)
    {
        var plan = await billing.CreatePlanAsync(request?.Name, request?.MonthlyPrice, request?.Brand, ct);
        return Results.Created("api/plans", plan);
    }
}

public class CreatePlanRequest
{
    public string? Name { get; set; }

    /// <summary>The monthly price in the site's currency, e.g. 19.99.</summary>
    public decimal? MonthlyPrice { get; set; }

    /// <summary>One of the brands of the shop's catalog.</summary>
    public string? Brand { get; set; }
}
