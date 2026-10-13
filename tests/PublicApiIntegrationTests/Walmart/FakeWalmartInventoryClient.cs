using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Walmart;

namespace PublicApiIntegrationTests.Walmart;

/// <summary>
/// In-memory stand-in for the Walmart Inventory API so the Walmart flows can be exercised
/// end-to-end through PublicApi without a live Walmart environment. It behaves like the contract:
/// PUT records a quantity, GET reads it back, bulk records many, list returns all.
/// </summary>
public class FakeWalmartInventoryClient : IWalmartInventoryClient
{
    private readonly Dictionary<string, int> _store = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    /// <summary>When set, bulk updates for these SKUs report failure (to exercise the failed status).</summary>
    public HashSet<string> FailBulkForSkus { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void Seed(string sku, int quantity)
    {
        lock (_gate) { _store[sku] = quantity; }
    }

    public Task<WalmartInventoryUpdateResult> UpdateQuantityAsync(string sku, int quantity, CancellationToken cancellationToken = default)
    {
        lock (_gate) { _store[sku] = quantity; }
        return Task.FromResult(new WalmartInventoryUpdateResult(sku, quantity));
    }

    public Task<WalmartInventoryRecord?> GetQuantityAsync(string sku, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_store.TryGetValue(sku, out var q)
                ? new WalmartInventoryRecord?(new WalmartInventoryRecord(sku, q))
                : null);
        }
    }

    public Task<IReadOnlyList<WalmartBulkItemResult>> UpdateQuantitiesAsync(IReadOnlyCollection<WalmartInventoryItem> items, CancellationToken cancellationToken = default)
    {
        var results = new List<WalmartBulkItemResult>(items.Count);
        lock (_gate)
        {
            foreach (var item in items)
            {
                if (FailBulkForSkus.Contains(item.Sku))
                {
                    results.Add(new WalmartBulkItemResult(item.Sku, false, "SIM-FAIL", "Simulated failure."));
                    continue;
                }
                _store[item.Sku] = item.Quantity;
                results.Add(new WalmartBulkItemResult(item.Sku, true));
            }
        }
        return Task.FromResult<IReadOnlyList<WalmartBulkItemResult>>(results);
    }

    public Task<IReadOnlyList<WalmartInventoryRecord>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<WalmartInventoryRecord> snapshot = _store
                .Select(kv => new WalmartInventoryRecord(kv.Key, kv.Value))
                .ToList();
            return Task.FromResult(snapshot);
        }
    }
}
