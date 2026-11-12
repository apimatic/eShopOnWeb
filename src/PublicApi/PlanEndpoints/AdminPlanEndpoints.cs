using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PlanEndpoints;

/// <summary>Brand is the catalog brand's name, or BrandId its id; one of the two is required.</summary>
public record CreatePlanRequest(string? Name, decimal? MonthlyPrice, string? Brand, int? BrandId);

/// <summary>Price is in whole units of the currency.</summary>
public record CreateCurrencyPriceRequest(string? Currency, decimal? Price);

/// <summary>
/// Staff add a plan with its name, monthly price and catalog brand
/// </summary>
public class CreatePlanEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePlanRequest request, IBillingService billing, CancellationToken ct) => await HandleAsync(request, billing, ct))
            .Produces<PlanDto>(StatusCodes.Status201Created)
            .WithTags("PlanEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePlanRequest request, IBillingService billing, CancellationToken ct)
    {
        var plan = await billing.CreatePlanAsync(request.Name, request.MonthlyPrice, request.Brand, request.BrandId, ct);
        return Results.Created($"api/plans", plan);
    }
}

/// <summary>
/// Staff price a plan in another currency
/// </summary>
public class CreateCurrencyPriceEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans/{planId:int}/currency-prices",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int planId, CreateCurrencyPriceRequest request, IBillingService billing, CancellationToken ct) => await HandleAsync(planId, request, billing, ct))
            .Produces<CurrencyPriceDto>(StatusCodes.Status201Created)
            .WithTags("PlanEndpoints");
    }

    public async Task<IResult> HandleAsync(int planId, CreateCurrencyPriceRequest request, IBillingService billing, CancellationToken ct)
    {
        var price = await billing.AddCurrencyPriceAsync(planId, request.Currency, request.Price, ct);
        return Results.Created($"api/admin/plans/{planId}/currency-prices", price);
    }
}
