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
/// Sets how many of an item the shop offers on Walmart (Walmart's own record for the SKU).
/// Operator action. Does not change eShop's own level.
/// </summary>
public class SetWalmartStockEndpoint : IEndpoint<IResult, SetWalmartStockCommand, IWalmartMarketplaceService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/walmart/stock/{sku}",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string sku, PutWalmartStockRequest request, IWalmartMarketplaceService walmart, CancellationToken ct) =>
            {
                return await HandleAsync(new SetWalmartStockCommand(sku, request.Quantity), walmart, ct);
            })
            .Produces<WalmartStockResponse>()
            .WithTags("WalmartEndpoints");
    }

    public Task<IResult> HandleAsync(SetWalmartStockCommand request, IWalmartMarketplaceService walmart)
        => HandleAsync(request, walmart, CancellationToken.None);

    public async Task<IResult> HandleAsync(
        SetWalmartStockCommand request, IWalmartMarketplaceService walmart, CancellationToken ct)
    {
        try
        {
            var result = await walmart.SetStockAsync(request.Sku, request.Quantity, ct);
            return Results.Ok(new WalmartStockResponse(result.Sku, result.Quantity));
        }
        catch (WalmartIntegrationException ex)
        {
            return WalmartEndpointResults.Error(ex);
        }
    }
}
