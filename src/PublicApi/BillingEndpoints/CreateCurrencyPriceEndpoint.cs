using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

public class CreateCurrencyPriceRequest : BaseRequest
{
    /// <summary>ISO 4217 code, e.g. EUR.</summary>
    public string? Currency { get; set; }
    /// <summary>Monthly price in that currency.</summary>
    public decimal? Price { get; set; }
}

/// <summary>
/// Prices a plan in another currency (staff only)
/// </summary>
public class CreateCurrencyPriceEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans/{planId}/currency-prices",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int planId, CreateCurrencyPriceRequest request, MaxioBillingService billing) =>
            {
                return await HandleAsync(planId, request, billing);
            })
            .Produces<CurrencyPriceDto>(StatusCodes.Status201Created)
            .WithTags(BillingEndpointHelpers.TAG);
    }

    public async Task<IResult> HandleAsync(int planId, CreateCurrencyPriceRequest request, MaxioBillingService billing)
    {
        var price = await billing.AddCurrencyPriceAsync(planId, request.Currency, request.Price);
        return Results.Created($"api/admin/plans/{planId}/currency-prices", price);
    }
}
