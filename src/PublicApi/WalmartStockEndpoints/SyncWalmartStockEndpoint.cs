using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Marketplace;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.WalmartStockEndpoints;

/// <summary>
/// Operator action. Pushes the eShop stock level of every catalog item to Walmart in one request.
/// Levels are the ones eShop holds; an item whose level was never set is treated as 0. Reports, per
/// SKU, what Walmart did with it.
/// </summary>
public class SyncWalmartStockEndpoint : IEndpoint<IResult, IWalmartInventoryService>
{
    private readonly IReadRepository<CatalogItem> _catalogRepository;

    public SyncWalmartStockEndpoint(IReadRepository<CatalogItem> catalogRepository)
    {
        _catalogRepository = catalogRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/walmart/stock/sync",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IWalmartInventoryService walmart) =>
            {
                return await HandleAsync(walmart);
            })
            .Produces<SyncWalmartStockResponse>()
            .WithTags("WalmartStockEndpoints");
    }

    public async Task<IResult> HandleAsync(IWalmartInventoryService walmart)
    {
        var catalogItems = await _catalogRepository.ListAsync();

        var updates = new List<WalmartInventoryUpdate>(catalogItems.Count);
        foreach (var item in catalogItems)
        {
            updates.Add(new WalmartInventoryUpdate(WalmartSku.ForCatalogItem(item.Id), item.StockQuantity));
        }

        var bulkResult = await walmart.SyncStockAsync(updates);

        var response = new SyncWalmartStockResponse();
        foreach (var result in bulkResult.Results)
        {
            response.Results.Add(new SyncResultItem
            {
                Sku = result.Sku,
                Status = result.Succeeded ? "updated" : "failed"
            });
        }

        return Results.Ok(response);
    }
}
