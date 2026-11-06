using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.AdminEndpoints;

public class AddCurrencyPriceRequest
{
    /// <summary>Three-letter currency code, for example EUR.</summary>
    public string? Currency { get; set; }
    public decimal? Price { get; set; }
}

/// <summary>
/// Prices a plan in another currency (staff)
/// </summary>
public class AddCurrencyPriceEndpoint : IEndpoint<IResult, int, AddCurrencyPriceRequest, BillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans/{planId}/currency-prices",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int planId, AddCurrencyPriceRequest request, BillingService billing) =>
            {
                return await HandleAsync(planId, request, billing);
            })
            .Produces<CurrencyPriceDto>(StatusCodes.Status201Created)
            .WithTags("AdminEndpoints");
    }

    public async Task<IResult> HandleAsync(int planId, AddCurrencyPriceRequest request, BillingService billing)
    {
        var price = await billing.AddCurrencyPriceAsync(planId, request?.Currency, request?.Price);
        return Results.Created($"api/admin/plans/{planId}/currency-prices", price);
    }
}
