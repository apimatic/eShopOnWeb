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
/// POST /api/walmart/stock/sync — pushes eShop's own stock level for every catalog item to Walmart in one
/// request, reporting per SKU what Walmart did (<c>updated</c> or <c>failed</c>). Operator action (administrator role).
/// </summary>
public class SyncWalmartStockEndpoint : IEndpoint<IResult, SyncWalmartStockRequest, IWalmartInventoryService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/walmart/stock/sync",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IWalmartInventoryService inventoryService) =>
            {
                return await HandleAsync(new SyncWalmartStockRequest(), inventoryService);
            })
            .Produces<WalmartStockSyncResponse>()
            .WithTags("WalmartStockEndpoints");
    }

    public async Task<IResult> HandleAsync(SyncWalmartStockRequest request, IWalmartInventoryService inventoryService)
    {
        var results = await inventoryService.SyncCatalogAsync(default);
        return Results.Ok(new WalmartStockSyncResponse(results));
    }
}
