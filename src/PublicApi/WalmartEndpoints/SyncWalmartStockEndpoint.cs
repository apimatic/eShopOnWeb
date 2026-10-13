using System.Collections.Generic;
using System.Linq;
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

namespace Microsoft.eShopWeb.PublicApi.WalmartEndpoints;

/// <summary>
/// Pushes the stock level of every item in the eShop catalog to Walmart in one operation. The
/// levels are the ones eShop holds (an item whose level was never set is 0). Reports, per SKU,
/// what Walmart did with it. Operator action.
/// </summary>
public class SyncWalmartStockEndpoint : IEndpoint<IResult, IWalmartInventoryService, IReadRepository<CatalogItem>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/walmart/stock/sync",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IWalmartInventoryService walmart, IReadRepository<CatalogItem> itemRepository) =>
            {
                return await HandleAsync(walmart, itemRepository);
            })
            .Produces<SyncWalmartStockResponse>()
            .WithTags("WalmartEndpoints");
    }

    public async Task<IResult> HandleAsync(IWalmartInventoryService walmart, IReadRepository<CatalogItem> itemRepository)
    {
        var catalogItems = await itemRepository.ListAsync();

        var toPush = catalogItems
            .Select(item => new WalmartSkuStock(WalmartSku.ForCatalogItem(item.Id), item.StockQuantity))
            .ToList();

        var results = await walmart.SyncStockAsync(toPush);

        var response = new SyncWalmartStockResponse
        {
            Results = results
                .Select(r => new SyncWalmartStockResultDto
                {
                    Sku = r.Sku,
                    Status = r.Success ? "updated" : "failed"
                })
                .ToList()
        };

        return Results.Ok(response);
    }
}

public class SyncWalmartStockResponse : BaseResponse
{
    public List<SyncWalmartStockResultDto> Results { get; set; } = new();
}

public class SyncWalmartStockResultDto
{
    public string Sku { get; set; } = string.Empty;

    /// <summary><c>updated</c> or <c>failed</c>.</summary>
    public string Status { get; set; } = string.Empty;
}
