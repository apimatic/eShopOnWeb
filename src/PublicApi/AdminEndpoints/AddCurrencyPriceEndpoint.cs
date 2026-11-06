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

public class AddCurrencyPriceRequest : BaseRequest
{
    /// <summary>Three-letter ISO currency code.</summary>
    public string? Currency { get; set; }
    public decimal? Price { get; set; }
}

/// <summary>
/// Prices a plan in another currency (staff only)
/// </summary>
public class AddCurrencyPriceEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans/{planId:int}/currency-prices",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int planId, AddCurrencyPriceRequest request, IBillingService billing, CancellationToken ct) =>
            {
                return await HandleAsync(planId, request, billing, ct);
            })
            .Produces<CurrencyPricesDto>(StatusCodes.Status201Created)
            .WithTags("AdminEndpoints");
    }

    public async Task<IResult> HandleAsync(int planId, AddCurrencyPriceRequest request, IBillingService billing, CancellationToken ct)
    {
        var result = await billing.AddCurrencyPriceAsync(planId, request.Currency, request.Price, ct);
        return Results.Created($"api/admin/plans/{planId}/currency-prices", result);
    }
}
