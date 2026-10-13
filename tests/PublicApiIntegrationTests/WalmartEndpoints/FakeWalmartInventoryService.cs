using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Marketplace;

namespace PublicApiIntegrationTests.WalmartEndpoints;

/// <summary>
/// Stand-in for the Walmart integration so the PublicApi Walmart endpoints can be exercised
/// end-to-end without a live Walmart environment. Records what the endpoints send and returns
/// canned data shaped to probe the reconciliation report.
/// </summary>
public class FakeWalmartInventoryService : IWalmartInventoryService
{
    public List<WalmartSkuStock> SetCalls { get; } = new();
    public List<WalmartSkuStock> LastSyncItems { get; } = new();

    public Task<WalmartSkuStock> SetStockAsync(string sku, int quantity, CancellationToken cancellationToken = default)
    {
        SetCalls.Add(new WalmartSkuStock(sku, quantity));
        return Task.FromResult(new WalmartSkuStock(sku, quantity));
    }

    public Task<WalmartSkuStock> GetStockAsync(string sku, CancellationToken cancellationToken = default)
        => Task.FromResult(new WalmartSkuStock(sku, 99));

    public Task<IReadOnlyList<WalmartSyncResult>> SyncStockAsync(
        IReadOnlyCollection<WalmartSkuStock> items, CancellationToken cancellationToken = default)
    {
        LastSyncItems.Clear();
        LastSyncItems.AddRange(items);

        // Mark everything updated except ESHOP-1, to exercise the "failed" status mapping.
        var results = items
            .Select(i => i.Sku == "ESHOP-1"
                ? new WalmartSyncResult(i.Sku, Success: false, "BAD_SKU", "simulated failure")
                : new WalmartSyncResult(i.Sku, Success: true))
            .ToList();
        return Task.FromResult<IReadOnlyList<WalmartSyncResult>>(results);
    }

    public Task<IReadOnlyList<WalmartSkuStock>> ListStockAsync(CancellationToken cancellationToken = default)
    {
        // ESHOP-1 exists on both sides (to show a possible disagreement); WALMART-ONLY exists only
        // on Walmart (eShop side will be null). eShop items not listed here appear with Walmart null.
        IReadOnlyList<WalmartSkuStock> list = new List<WalmartSkuStock>
        {
            new("ESHOP-1", 100),
            new("WALMART-ONLY", 7),
        };
        return Task.FromResult(list);
    }
}
