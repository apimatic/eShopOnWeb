using System;
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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.WalmartIntegration;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.WalmartStockEndpoints;

/// <summary>
/// GET /api/walmart/stock — operator action: every SKU Walmart holds stock for, lined up against
/// eShop's own level, plus any eShop SKU Walmart does not hold. A SKU present on only one side shows
/// null on the other. Admin only.
/// </summary>
public class WalmartStockReportEndpoint : IEndpoint<IResult, IWalmartInventoryService, IReadRepository<CatalogItem>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/walmart/stock",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IWalmartInventoryService walmart, IReadRepository<CatalogItem> catalog, CancellationToken ct) =>
            {
                return await HandleAsync(walmart, catalog, ct);
            })
            .Produces<WalmartStockReportResponse>()
            .WithTags("WalmartStockEndpoints");
    }

    public Task<IResult> HandleAsync(IWalmartInventoryService walmart, IReadRepository<CatalogItem> catalog)
        => HandleAsync(walmart, catalog, default);

    public async Task<IResult> HandleAsync(IWalmartInventoryService walmart, IReadRepository<CatalogItem> catalog, CancellationToken ct)
    {
        var walmartStock = await walmart.ListWalmartStockAsync(ct);
        var walmartBySku = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var entry in walmartStock)
        {
            // Last write wins if Walmart ever returns a SKU twice (e.g. multiple ship nodes).
            walmartBySku[entry.Sku] = entry.Quantity;
        }

        var catalogItems = await catalog.ListAsync(ct);
        var eshopBySku = catalogItems.ToDictionary(
            ci => WalmartSku.ForCatalogItem(ci.Id),
            ci => ci.UnitsInStock,
            StringComparer.Ordinal);

        var allSkus = new SortedSet<string>(StringComparer.Ordinal);
        allSkus.UnionWith(walmartBySku.Keys);
        allSkus.UnionWith(eshopBySku.Keys);

        var items = allSkus.Select(sku => new WalmartStockReportItem
        {
            Sku = sku,
            WalmartQuantity = walmartBySku.TryGetValue(sku, out var w) ? w : (int?)null,
            EshopQuantity = eshopBySku.TryGetValue(sku, out var e) ? e : (int?)null,
        }).ToList();

        return Results.Ok(new WalmartStockReportResponse { Items = items });
    }
}
