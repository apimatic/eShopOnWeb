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
using Microsoft.eShopWeb.PublicApi.Walmart;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.WalmartStockEndpoints;

public class WalmartStockReportItemDto
{
    public string Sku { get; set; } = string.Empty;
    public int? WalmartQuantity { get; set; }
    public int? EshopQuantity { get; set; }
}

public class WalmartStockReportResponse : BaseResponse
{
    public WalmartStockReportResponse(Guid correlationId) : base(correlationId) { }
    public WalmartStockReportResponse() { }

    public List<WalmartStockReportItemDto> Items { get; set; } = new();

    /// <summary>True when Walmart held more pages than the listing cap; the report is then partial.</summary>
    public bool Truncated { get; set; }
}

/// <summary>
/// Flow 3 (operator): lists every SKU Walmart holds stock for, lined up against eShop's own level
/// for the matching catalog item, so a SKU whose two numbers differ — or that exists on one side
/// only — is visible. Covers every SKU on both sides.
/// </summary>
public class GetWalmartStockReportEndpoint : IEndpoint<IResult, IRepository<CatalogItem>>
{
    private readonly IWalmartInventoryService _walmart;

    public GetWalmartStockReportEndpoint(IWalmartInventoryService walmart) => _walmart = walmart;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/walmart/stock",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IRepository<CatalogItem> itemRepository, CancellationToken cancellationToken) =>
                await HandleAsync(itemRepository, cancellationToken))
            .Produces<WalmartStockReportResponse>()
            .WithTags("WalmartStockEndpoints");
    }

    // Interface member — the framework only calls AddRoute; the route lambda calls the ct overload.
    public Task<IResult> HandleAsync(IRepository<CatalogItem> itemRepository) =>
        HandleAsync(itemRepository, CancellationToken.None);

    public async Task<IResult> HandleAsync(IRepository<CatalogItem> itemRepository, CancellationToken cancellationToken)
    {
        WalmartStockListing listing;
        try
        {
            listing = await _walmart.ListAllAsync(cancellationToken);
        }
        catch (WalmartIntegrationException ex)
        {
            return WalmartResults.Problem(ex);
        }

        var catalogItems = await itemRepository.ListAsync(cancellationToken);

        // eShop side, keyed by SKU.
        var eshopBySku = catalogItems.ToDictionary(
            ci => WalmartSku.ForCatalogItem(ci.Id),
            ci => (int?)ci.AvailableStock,
            StringComparer.Ordinal);

        // Walmart side, keyed by SKU (last write wins if the provider ever repeats a SKU).
        var walmartBySku = new Dictionary<string, int?>(StringComparer.Ordinal);
        foreach (var level in listing.Items)
        {
            walmartBySku[level.Sku] = level.Quantity;
        }

        // Union both sides so a SKU present on only one side is still visible (null on the other).
        var allSkus = new SortedSet<string>(StringComparer.Ordinal);
        allSkus.UnionWith(eshopBySku.Keys);
        allSkus.UnionWith(walmartBySku.Keys);

        var items = allSkus.Select(sku => new WalmartStockReportItemDto
        {
            Sku = sku,
            WalmartQuantity = walmartBySku.TryGetValue(sku, out var wq) ? wq : null,
            EshopQuantity = eshopBySku.TryGetValue(sku, out var eq) ? eq : null,
        }).ToList();

        return Results.Ok(new WalmartStockReportResponse
        {
            Items = items,
            Truncated = listing.Truncated,
        });
    }
}
