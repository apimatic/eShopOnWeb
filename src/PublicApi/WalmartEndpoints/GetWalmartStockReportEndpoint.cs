using System;
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
/// Reconciliation report: every SKU Walmart holds stock for, lined up against eShop's own level
/// for the matching catalog item, plus every eShop catalog item SKU Walmart does not hold. A SKU
/// whose two numbers differ — or that exists on one side only (the other side is null) — is
/// visible. Covers all of them. Operator action.
/// </summary>
public class GetWalmartStockReportEndpoint : IEndpoint<IResult, IWalmartInventoryService, IReadRepository<CatalogItem>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/walmart/stock",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IWalmartInventoryService walmart, IReadRepository<CatalogItem> itemRepository) =>
            {
                return await HandleAsync(walmart, itemRepository);
            })
            .Produces<WalmartStockReportResponse>()
            .WithTags("WalmartEndpoints");
    }

    public async Task<IResult> HandleAsync(IWalmartInventoryService walmart, IReadRepository<CatalogItem> itemRepository)
    {
        var walmartStock = await walmart.ListStockAsync();
        var catalogItems = await itemRepository.ListAsync();

        // eShop level keyed by the SKU each catalog item is sold under.
        var eshopBySku = catalogItems.ToDictionary(
            item => WalmartSku.ForCatalogItem(item.Id),
            item => item.StockQuantity,
            StringComparer.OrdinalIgnoreCase);

        // Walmart level keyed by SKU (last wins if Walmart ever repeats a SKU across pages).
        var walmartBySku = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in walmartStock)
        {
            walmartBySku[entry.Sku] = entry.Quantity;
        }

        // Union of both sides so disagreements and one-sided SKUs are all visible.
        var allSkus = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        allSkus.UnionWith(walmartBySku.Keys);
        allSkus.UnionWith(eshopBySku.Keys);

        var items = new List<WalmartStockReportItemDto>(allSkus.Count);
        foreach (var sku in allSkus)
        {
            items.Add(new WalmartStockReportItemDto
            {
                Sku = sku,
                WalmartQuantity = walmartBySku.TryGetValue(sku, out var wq) ? wq : null,
                EshopQuantity = eshopBySku.TryGetValue(sku, out var eq) ? eq : null
            });
        }

        return Results.Ok(new WalmartStockReportResponse { Items = items });
    }
}

public class WalmartStockReportResponse : BaseResponse
{
    public List<WalmartStockReportItemDto> Items { get; set; } = new();
}

public class WalmartStockReportItemDto
{
    public string Sku { get; set; } = string.Empty;

    /// <summary>Walmart's recorded quantity for the SKU, or null if Walmart holds no such SKU.</summary>
    public int? WalmartQuantity { get; set; }

    /// <summary>eShop's own level for the matching catalog item, or null if eShop has no such SKU.</summary>
    public int? EshopQuantity { get; set; }
}
