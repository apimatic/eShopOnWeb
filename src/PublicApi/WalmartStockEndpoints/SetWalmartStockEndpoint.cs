using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Walmart;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.WalmartStockEndpoints;

/// <summary>
/// PUT /api/walmart/stock/{sku} — sets how many of the SKU the shop offers on Walmart. Operator action
/// (administrator role). Does not change eShop's own level for the item.
/// </summary>
public class SetWalmartStockEndpoint : IEndpoint<IResult, SetWalmartStockRequest, IWalmartInventoryService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/walmart/stock/{sku}",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string sku, SetWalmartStockRequest request, IWalmartInventoryService inventoryService) =>
            {
                return await HandleAsync(request with { Sku = sku }, inventoryService);
            })
            .Produces<WalmartStockResponse>()
            .WithTags("WalmartStockEndpoints");
    }

    public async Task<IResult> HandleAsync(SetWalmartStockRequest request, IWalmartInventoryService inventoryService)
    {
        if (request.Quantity < 0)
        {
            return Results.BadRequest("quantity must be zero or greater.");
        }

        var stock = await inventoryService.SetStockAsync(request.Sku, request.Quantity, default);
        return Results.Ok(new WalmartStockResponse(stock.Sku, stock.Quantity));
    }
}
