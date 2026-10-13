using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Marketplace;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.WalmartStockEndpoints;

/// <summary>
/// Pushes the eShop stock level of every catalog item to Walmart in one request. Operator action
/// (administrators only). The response reports, per SKU, what Walmart did with it.
/// </summary>
public class SyncWalmartStockEndpoint : IEndpoint<IResult, IWalmartInventoryService, IReadRepository<CatalogItem>, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/walmart/stock/sync",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IWalmartInventoryService service, IReadRepository<CatalogItem> itemRepository, CancellationToken ct) =>
            {
                return await HandleAsync(service, itemRepository, ct);
            })
            .Produces<SyncWalmartStockResponse>()
            .WithTags("WalmartStockEndpoints");
    }

    public async Task<IResult> HandleAsync(IWalmartInventoryService service, IReadRepository<CatalogItem> itemRepository, CancellationToken ct)
    {
        var catalogItems = await itemRepository.ListAsync(ct);
        var levels = catalogItems
            .Select(c => (c.Id, c.StockQuantity))
            .ToList();

        try
        {
            var results = await service.SyncAsync(levels, ct);
            return Results.Ok(new SyncWalmartStockResponse
            {
                Results = results
                    .Select(r => new SyncResultItem
                    {
                        Sku = r.Sku,
                        Status = r.Status == WalmartSyncStatus.Updated ? "updated" : "failed"
                    })
                    .ToList()
            });
        }
        catch (WalmartInventoryException ex)
        {
            return WalmartResults.ToResult(ex);
        }
    }
}
