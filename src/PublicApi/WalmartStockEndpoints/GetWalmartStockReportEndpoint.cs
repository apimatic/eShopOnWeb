using System.Collections.Generic;
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
/// Reconciliation report: every SKU Walmart holds stock for, lined up against eShop's own level for
/// the matching catalog item, plus every eShop SKU Walmart does not hold. Covers all SKUs. Operator
/// action (administrators only).
/// </summary>
public class GetWalmartStockReportEndpoint : IEndpoint<IResult, IWalmartInventoryService, IReadRepository<CatalogItem>, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/walmart/stock",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IWalmartInventoryService service, IReadRepository<CatalogItem> itemRepository, CancellationToken ct) =>
            {
                return await HandleAsync(service, itemRepository, ct);
            })
            .Produces<WalmartStockReportResponse>()
            .WithTags("WalmartStockEndpoints");
    }

    public async Task<IResult> HandleAsync(IWalmartInventoryService service, IReadRepository<CatalogItem> itemRepository, CancellationToken ct)
    {
        var catalogItems = await itemRepository.ListAsync(ct);
        // eShop side, keyed by SKU.
        var eshopBySku = catalogItems.ToDictionary(
            c => WalmartSku.ForCatalogItem(c.Id),
            c => c.StockQuantity,
            System.StringComparer.Ordinal);

        IReadOnlyList<WalmartSkuStock> walmartStock;
        try
        {
            walmartStock = await service.ListAllStockAsync(ct);
        }
        catch (WalmartInventoryException ex)
        {
            return WalmartResults.ToResult(ex);
        }

        var items = new List<WalmartStockReportItem>();
        var seen = new HashSet<string>(System.StringComparer.Ordinal);

        // Every SKU Walmart holds, lined up with eShop's level (null if eShop has no such SKU).
        foreach (var w in walmartStock)
        {
            seen.Add(w.Sku);
            items.Add(new WalmartStockReportItem
            {
                Sku = w.Sku,
                WalmartQuantity = w.Quantity,
                EshopQuantity = eshopBySku.TryGetValue(w.Sku, out var level) ? level : null
            });
        }

        // Every eShop SKU Walmart does not hold (walmartQuantity null).
        foreach (var kvp in eshopBySku)
        {
            if (seen.Contains(kvp.Key))
            {
                continue;
            }
            items.Add(new WalmartStockReportItem
            {
                Sku = kvp.Key,
                WalmartQuantity = null,
                EshopQuantity = kvp.Value
            });
        }

        return Results.Ok(new WalmartStockReportResponse { Items = items });
    }
}
