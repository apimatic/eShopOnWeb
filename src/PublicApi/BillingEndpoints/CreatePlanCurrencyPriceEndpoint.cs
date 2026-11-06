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
/// Staff: prices a plan in another currency
/// </summary>
public class CreatePlanCurrencyPriceEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans/{planId}/currency-prices",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (int planId, CurrencyPriceRequest request, BillingService billing, CancellationToken ct) =>
                await HandleAsync(planId, request, billing, ct))
            .Produces<CurrencyPriceDto>(StatusCodes.Status201Created)
            .WithTags("BillingEndpoints");
    }

    public async Task<IResult> HandleAsync(int planId, CurrencyPriceRequest request, BillingService billing, CancellationToken ct)
    {
        var price = await billing.AddCurrencyPriceAsync(planId, request?.Currency, request?.Price, ct);
        return Results.Created($"api/admin/plans/{planId}/currency-prices", price);
    }
}

public class CurrencyPriceRequest
{
    /// <summary>Three-letter ISO currency code.</summary>
    public string? Currency { get; set; }

    /// <summary>The monthly price in that currency.</summary>
    public decimal? Price { get; set; }
}
