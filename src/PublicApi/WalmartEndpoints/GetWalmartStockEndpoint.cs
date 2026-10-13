using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.WalmartEndpoints;

/// <summary>
/// Reads the SKU's stock as Walmart currently records it (a live read from Walmart, not a local
/// copy). Operator action.
/// </summary>
public class GetWalmartStockEndpoint : IEndpoint<IResult, GetWalmartStockCommand, IWalmartMarketplaceService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/walmart/stock/{sku}",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string sku, IWalmartMarketplaceService walmart, CancellationToken ct) =>
            {
                return await HandleAsync(new GetWalmartStockCommand(sku), walmart, ct);
            })
            .Produces<WalmartStockResponse>()
            .WithTags("WalmartEndpoints");
    }

    public Task<IResult> HandleAsync(GetWalmartStockCommand request, IWalmartMarketplaceService walmart)
        => HandleAsync(request, walmart, CancellationToken.None);

    public async Task<IResult> HandleAsync(
        GetWalmartStockCommand request, IWalmartMarketplaceService walmart, CancellationToken ct)
    {
        try
        {
            var result = await walmart.GetStockAsync(request.Sku, ct);
            return Results.Ok(new WalmartStockResponse(result.Sku, result.Quantity));
        }
        catch (WalmartIntegrationException ex)
        {
            return WalmartEndpointResults.Error(ex);
        }
    }
}
