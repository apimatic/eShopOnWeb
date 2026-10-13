using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.WalmartStockEndpoints;

/// <summary>
/// Operator action. Sets how many units of a SKU the shop offers on Walmart Marketplace. Afterwards
/// Walmart's own record of the SKU's quantity is that number. Does not change eShop's own level.
/// </summary>
public class SetWalmartStockEndpoint : IEndpoint<IResult, SetWalmartStockRequest, IWalmartInventoryService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/walmart/stock/{sku}",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string sku, SetWalmartStockRequest request, IWalmartInventoryService walmart) =>
            {
                request ??= new SetWalmartStockRequest();
                request.Sku = sku;
                return await HandleAsync(request, walmart);
            })
            .Produces<WalmartSkuStockResponse>()
            .WithTags("WalmartStockEndpoints");
    }

    public async Task<IResult> HandleAsync(SetWalmartStockRequest request, IWalmartInventoryService walmart)
    {
        if (request.Quantity < 0)
        {
            return Results.BadRequest(new { message = "quantity must be a non-negative integer." });
        }

        var record = await walmart.SetStockAsync(request.Sku, request.Quantity);
        return Results.Ok(new WalmartSkuStockResponse { Sku = record.Sku, Quantity = record.Quantity });
    }
}
