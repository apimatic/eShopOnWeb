using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Walmart;

namespace PublicApiIntegrationTests.WalmartStockEndpoints;

/// <summary>
/// In-memory stand-in for <see cref="IWalmartInventoryService"/> so the Walmart endpoints can be
/// exercised end-to-end through PublicApi without a live Walmart environment.
/// </summary>
public class FakeWalmartInventoryService : IWalmartInventoryService
{
    public Dictionary<string, int> Store { get; } = new();

    public Task<WalmartInventorySetResult> SetInventoryAsync(string sku, int quantity, CancellationToken cancellationToken = default)
    {
        Store[sku] = quantity;
        return Task.FromResult(new WalmartInventorySetResult(sku, quantity));
    }

    public Task<WalmartInventorySnapshot?> GetInventoryAsync(string sku, CancellationToken cancellationToken = default)
    {
        if (Store.TryGetValue(sku, out var qty))
        {
            return Task.FromResult<WalmartInventorySnapshot?>(new WalmartInventorySnapshot(sku, qty));
        }
        return Task.FromResult<WalmartInventorySnapshot?>(null);
    }

    public Task<IReadOnlyList<WalmartSyncResult>> SyncInventoryAsync(IReadOnlyCollection<WalmartInventoryUpdate> items, CancellationToken cancellationToken = default)
    {
        var results = new List<WalmartSyncResult>();
        foreach (var item in items)
        {
            Store[item.Sku] = item.Quantity;
            results.Add(new WalmartSyncResult(item.Sku, WalmartSyncStatus.Updated));
        }
        return Task.FromResult<IReadOnlyList<WalmartSyncResult>>(results);
    }

    public Task<IReadOnlyList<WalmartInventorySnapshot>> ListInventoryAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<WalmartInventorySnapshot> snapshots =
            Store.Select(kvp => new WalmartInventorySnapshot(kvp.Key, kvp.Value)).ToList();
        return Task.FromResult(snapshots);
    }
}
