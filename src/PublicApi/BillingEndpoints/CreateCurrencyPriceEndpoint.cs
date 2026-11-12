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

public record CreateCurrencyPriceRequest(string? Currency, decimal Price);

/// <summary>
/// Prices a plan in another currency (staff only)
/// </summary>
public class CreateCurrencyPriceEndpoint : IEndpoint<IResult, int, CreateCurrencyPriceRequest, MaxioGateway>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans/{planId}/currency-prices",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int planId, CreateCurrencyPriceRequest request, MaxioGateway billing) =>
            {
                return await HandleAsync(planId, request, billing);
            })
            .Produces<CurrencyPriceDto>(StatusCodes.Status201Created)
            .WithTags("AdminBillingEndpoints");
    }

    public async Task<IResult> HandleAsync(int planId, CreateCurrencyPriceRequest request, MaxioGateway billing)
    {
        var price = await billing.AddCurrencyPriceAsync(planId, request.Currency, request.Price);
        return Results.Created($"api/admin/plans/{planId}/currency-prices", price);
    }
}
