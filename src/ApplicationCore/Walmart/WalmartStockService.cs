using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Walmart;

public class WalmartStockService : IWalmartStockService
{
    private readonly IWalmartInventoryClient _inventoryClient;
    private readonly IReadRepository<CatalogItem> _catalogRepository;

    public WalmartStockService(IWalmartInventoryClient inventoryClient, IReadRepository<CatalogItem> catalogRepository)
    {
        _inventoryClient = inventoryClient;
        _catalogRepository = catalogRepository;
    }

    public async Task<WalmartInventoryUpdateResult> SetWalmartQuantityAsync(string sku, int quantity, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(sku, nameof(sku));
        Guard.Against.Negative(quantity, nameof(quantity));
        return await _inventoryClient.UpdateQuantityAsync(sku, quantity, cancellationToken);
    }

    public async Task<WalmartInventoryRecord?> GetWalmartQuantityAsync(string sku, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(sku, nameof(sku));
        return await _inventoryClient.GetQuantityAsync(sku, cancellationToken);
    }

    public async Task<IReadOnlyList<WalmartSyncResult>> SyncCatalogToWalmartAsync(CancellationToken cancellationToken = default)
    {
        var catalogItems = await _catalogRepository.ListAsync(cancellationToken);

        var items = catalogItems
            .Select(ci => new WalmartInventoryItem(WalmartSku.ForCatalogItemId(ci.Id), ci.StockQuantity))
            .ToList();

        if (items.Count == 0)
        {
            return Array.Empty<WalmartSyncResult>();
        }

        var outcomes = await _inventoryClient.UpdateQuantitiesAsync(items, cancellationToken);
        var bySku = outcomes.ToDictionary(o => o.Sku, StringComparer.OrdinalIgnoreCase);

        var results = new List<WalmartSyncResult>(items.Count);
        foreach (var item in items)
        {
            if (bySku.TryGetValue(item.Sku, out var outcome) && outcome.Success)
            {
                results.Add(new WalmartSyncResult(item.Sku, WalmartSyncStatus.Updated));
            }
            else if (bySku.TryGetValue(item.Sku, out outcome))
            {
                results.Add(new WalmartSyncResult(item.Sku, WalmartSyncStatus.Failed, outcome.ErrorMessage));
            }
            else
            {
                // Walmart acknowledged neither success nor failure for this SKU; treat as failed.
                results.Add(new WalmartSyncResult(item.Sku, WalmartSyncStatus.Failed, "No result returned by Walmart for this SKU."));
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<WalmartReconciliationItem>> ReconcileAsync(CancellationToken cancellationToken = default)
    {
        var walmartRecords = await _inventoryClient.ListAllAsync(cancellationToken);
        var catalogItems = await _catalogRepository.ListAsync(cancellationToken);

        var eshopBySku = catalogItems.ToDictionary(
            ci => WalmartSku.ForCatalogItemId(ci.Id),
            ci => ci.StockQuantity,
            StringComparer.OrdinalIgnoreCase);

        var walmartBySku = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in walmartRecords)
        {
            // Last write wins if Walmart ever returns a SKU twice across pages.
            walmartBySku[record.Sku] = record.Quantity;
        }

        var allSkus = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        allSkus.UnionWith(eshopBySku.Keys);
        allSkus.UnionWith(walmartBySku.Keys);

        var items = new List<WalmartReconciliationItem>(allSkus.Count);
        foreach (var sku in allSkus)
        {
            int? walmartQty = walmartBySku.TryGetValue(sku, out var w) ? w : null;
            int? eshopQty = eshopBySku.TryGetValue(sku, out var e) ? e : null;
            items.Add(new WalmartReconciliationItem(sku, walmartQty, eshopQty));
        }

        return items;
    }
}
