using System;
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
/// Operator action. Lists every SKU Walmart holds stock for, lined up against eShop's own level for
/// the matching catalog item, so a SKU whose two numbers differ — or that exists on one side only —
/// is visible. Covers every SKU on both sides.
/// </summary>
public class WalmartStockReportEndpoint : IEndpoint<IResult, IWalmartInventoryService>
{
    private readonly IReadRepository<CatalogItem> _catalogRepository;

    public WalmartStockReportEndpoint(IReadRepository<CatalogItem> catalogRepository)
    {
        _catalogRepository = catalogRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/walmart/stock",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IWalmartInventoryService walmart) =>
            {
                return await HandleAsync(walmart);
            })
            .Produces<WalmartStockReportResponse>()
            .WithTags("WalmartStockEndpoints");
    }

    public async Task<IResult> HandleAsync(IWalmartInventoryService walmart)
    {
        var walmartRecords = await walmart.ListStockAsync();
        var catalogItems = await _catalogRepository.ListAsync();

        // Full outer join by SKU: every SKU Walmart holds, plus every eShop catalog SKU, so a SKU on
        // one side only still appears (with null on the missing side).
        var items = new Dictionary<string, WalmartStockReportItem>(StringComparer.Ordinal);

        WalmartStockReportItem For(string sku)
        {
            if (!items.TryGetValue(sku, out var entry))
            {
                entry = new WalmartStockReportItem { Sku = sku };
                items[sku] = entry;
            }
            return entry;
        }

        foreach (var record in walmartRecords)
        {
            For(record.Sku).WalmartQuantity = record.Quantity;
        }

        foreach (var item in catalogItems)
        {
            For(WalmartSku.ForCatalogItem(item.Id)).EshopQuantity = item.StockQuantity;
        }

        var response = new WalmartStockReportResponse();
        response.Items.AddRange(items.Values);
        return Results.Ok(response);
    }
}
