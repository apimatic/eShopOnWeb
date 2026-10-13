using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Walmart;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.WalmartStockEndpoints;

/// <summary>
/// Pushes the eShop stock level of every catalog item to Walmart in one request. The levels pushed are
/// the ones eShop holds (an item whose level was never set has a level of 0). Reports, per SKU, what
/// Walmart did with it. Operator-only.
/// </summary>
public class SyncWalmartStockEndpoint : IEndpoint<IResult, IReadRepository<CatalogItem>, IWalmartInventoryService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/walmart/stock/sync",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IReadRepository<CatalogItem> itemRepository, IWalmartInventoryService walmart) =>
            {
                return await HandleAsync(itemRepository, walmart);
            })
            .Produces<SyncWalmartStockResponse>()
            .WithTags("WalmartStockEndpoints");
    }

    public async Task<IResult> HandleAsync(IReadRepository<CatalogItem> itemRepository, IWalmartInventoryService walmart)
    {
        var catalogItems = await itemRepository.ListAsync();

        var updates = catalogItems
            .Select(item => new WalmartInventoryUpdate(WalmartSku.ForCatalogItem(item.Id), item.StockQuantity))
            .ToList();

        var results = await walmart.SyncInventoryAsync(updates);

        var response = new SyncWalmartStockResponse();
        response.Results.AddRange(results.Select(r => new WalmartSyncResultDto
        {
            Sku = r.Sku,
            Status = r.Status == WalmartSyncStatus.Updated ? "updated" : "failed"
        }));
        return Results.Ok(response);
    }
}
