using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Marketplace;

namespace PublicApiIntegrationTests;

/// <summary>
/// In-memory stand-in for Walmart Marketplace used by integration tests (there is no live Walmart
/// environment). Behaves like Walmart's own record: PUT/sync overwrite the stored quantity, GET/list
/// read it back.
/// </summary>
public class FakeWalmartInventoryService : IWalmartInventoryService
{
    private readonly Dictionary<string, int> _store = new(StringComparer.Ordinal);

    public Task<WalmartInventoryRecord> SetStockAsync(string sku, int quantity, CancellationToken cancellationToken = default)
    {
        _store[sku] = quantity;
        return Task.FromResult(new WalmartInventoryRecord(sku, quantity));
    }

    public Task<WalmartInventoryRecord?> GetStockAsync(string sku, CancellationToken cancellationToken = default)
    {
        if (_store.TryGetValue(sku, out var qty))
        {
            return Task.FromResult<WalmartInventoryRecord?>(new WalmartInventoryRecord(sku, qty));
        }
        return Task.FromResult<WalmartInventoryRecord?>(null);
    }

    public Task<WalmartBulkResult> SyncStockAsync(IReadOnlyList<WalmartInventoryUpdate> updates, CancellationToken cancellationToken = default)
    {
        var results = new List<WalmartSyncResult>();
        foreach (var update in updates)
        {
            _store[update.Sku] = update.Quantity;
            results.Add(new WalmartSyncResult(update.Sku, true));
        }
        return Task.FromResult(new WalmartBulkResult(results));
    }

    public Task<IReadOnlyList<WalmartInventoryRecord>> ListStockAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<WalmartInventoryRecord> records =
            _store.Select(kvp => new WalmartInventoryRecord(kvp.Key, kvp.Value)).ToList();
        return Task.FromResult(records);
    }
}
