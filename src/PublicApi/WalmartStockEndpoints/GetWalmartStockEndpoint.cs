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
/// GET /api/walmart/stock/{sku} — the SKU's stock as Walmart currently records it (a live read from Walmart,
/// not a local copy). Operator action (administrator role).
/// </summary>
public class GetWalmartStockEndpoint : IEndpoint<IResult, GetWalmartStockRequest, IWalmartInventoryService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/walmart/stock/{sku}",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string sku, IWalmartInventoryService inventoryService) =>
            {
                return await HandleAsync(new GetWalmartStockRequest(sku), inventoryService);
            })
            .Produces<WalmartStockResponse>()
            .WithTags("WalmartStockEndpoints");
    }

    public async Task<IResult> HandleAsync(GetWalmartStockRequest request, IWalmartInventoryService inventoryService)
    {
        var stock = await inventoryService.GetStockAsync(request.Sku, default);
        return Results.Ok(new WalmartStockResponse(stock.Sku, stock.Quantity));
    }
}
