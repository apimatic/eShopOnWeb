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
using Microsoft.eShopWeb.ApplicationCore.Walmart;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.WalmartStockEndpoints;

/// <summary>
/// Reports every SKU Walmart holds stock for, lined up against eShop's own level for the matching
/// catalog item, so a SKU whose two numbers differ — or that exists on one side only — is visible.
/// Covers the full union of Walmart SKUs and eShop catalog items. Operator-only.
/// </summary>
public class GetWalmartStockReportEndpoint : IEndpoint<IResult, IReadRepository<CatalogItem>, IWalmartInventoryService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/walmart/stock",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IReadRepository<CatalogItem> itemRepository, IWalmartInventoryService walmart) =>
            {
                return await HandleAsync(itemRepository, walmart);
            })
            .Produces<WalmartStockReportResponse>()
            .WithTags("WalmartStockEndpoints");
    }

    public async Task<IResult> HandleAsync(IReadRepository<CatalogItem> itemRepository, IWalmartInventoryService walmart)
    {
        // eShop side: every catalog item maps to a SKU, keyed by that SKU.
        var catalogItems = await itemRepository.ListAsync();
        var eshopBySku = catalogItems.ToDictionary(
            item => WalmartSku.ForCatalogItem(item.Id),
            item => item.StockQuantity,
            System.StringComparer.Ordinal);

        // Walmart side: every SKU Walmart holds a record for.
        var walmartSnapshots = await walmart.ListInventoryAsync();
        var walmartBySku = new Dictionary<string, int>(System.StringComparer.Ordinal);
        foreach (var snapshot in walmartSnapshots)
        {
            walmartBySku[snapshot.Sku] = snapshot.Quantity;
        }

        // Union of both sides, so SKUs present on only one side still appear.
        var allSkus = eshopBySku.Keys
            .Union(walmartBySku.Keys, System.StringComparer.Ordinal)
            .OrderBy(sku => sku, System.StringComparer.Ordinal);

        var response = new WalmartStockReportResponse();
        foreach (var sku in allSkus)
        {
            response.Items.Add(new WalmartStockComparisonDto
            {
                Sku = sku,
                WalmartQuantity = walmartBySku.TryGetValue(sku, out var wq) ? wq : null,
                EshopQuantity = eshopBySku.TryGetValue(sku, out var eq) ? eq : null
            });
        }
        return Results.Ok(response);
    }
}
