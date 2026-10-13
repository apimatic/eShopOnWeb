using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Walmart;

namespace PublicApiIntegrationTests.Walmart;

/// <summary>
/// In-memory fake standing in for the real Walmart service in endpoint tests (no live Walmart).
/// Records what it was asked and returns canned, deterministic data.
/// </summary>
public sealed class FakeWalmartInventoryService : IWalmartInventoryService
{
    public readonly Dictionary<string, int> Store = new();
    public IReadOnlyCollection<WalmartSkuQuantity>? LastSync;

    public Task<WalmartStockLevel> SetStockAsync(string sku, int quantity, CancellationToken cancellationToken)
    {
        Store[sku] = quantity;
        return Task.FromResult(new WalmartStockLevel(sku, quantity));
    }

    public Task<WalmartStockLevel> GetStockAsync(string sku, CancellationToken cancellationToken) =>
        Task.FromResult(new WalmartStockLevel(sku, Store.TryGetValue(sku, out var q) ? q : 0));

    public Task<IReadOnlyList<WalmartSyncItemResult>> SyncAsync(
        IReadOnlyCollection<WalmartSkuQuantity> items, CancellationToken cancellationToken)
    {
        LastSync = items;
        foreach (var i in items)
        {
            Store[i.Sku] = i.Quantity;
        }
        IReadOnlyList<WalmartSyncItemResult> results = items
            .Select(i => new WalmartSyncItemResult(i.Sku, WalmartSyncItemResult.Updated))
            .ToList();
        return Task.FromResult(results);
    }

    public Task<WalmartStockListing> ListAllAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<WalmartStockLevel> items = Store
            .Select(kvp => new WalmartStockLevel(kvp.Key, kvp.Value))
            .ToList();
        return Task.FromResult(new WalmartStockListing(items, Truncated: false));
    }
}
