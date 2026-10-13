using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Marketplace;
using WalmartApIs;
using WalmartApIs.Core.Exceptions;
using WalmartApIs.Models;
using WalmartApIs.Requests.Inventory;

namespace Microsoft.eShopWeb.Infrastructure.Marketplace;

/// <summary>
/// Implements the Walmart inventory capability over the Walmart APIs .NET SDK (Inventory group,
/// authenticated with WalletAuth client credentials). Every SDK failure is translated to
/// <see cref="WalmartInventoryException"/> so callers have a single failure type to handle.
/// </summary>
public class WalmartInventoryService : IWalmartInventoryService
{
    // BulkUpdateInventory accepts 1..500 items per call; chunk the catalog to stay within it.
    private const int MaxBatchSize = 500;

    // GetInventorySummaries pages are walked by hand; cap the walk so a provider that never stops
    // handing out a next token cannot spin forever. 500 SKUs/page × 1000 pages = 500k SKUs.
    private const int MaxSummaryPages = 1000;
    private const int SummaryPageSize = 500;

    // Whole-operation budget, independent of the SDK's per-attempt Retry.Timeout.
    private static readonly TimeSpan OperationBudget = TimeSpan.FromSeconds(60);

    private readonly WalmartApIsClient _client;
    private readonly IAppLogger<WalmartInventoryService> _logger;

    public WalmartInventoryService(WalmartApIsClient client, IAppLogger<WalmartInventoryService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public Task<WalmartSkuStock> SetStockAsync(string sku, int quantity, CancellationToken cancellationToken = default)
    {
        return InvokeAsync("set-stock", async ct =>
        {
            await _client.Inventory.UpdateInventoryForSku(
                new UpdateInventoryForSkuRequest
                {
                    Sku = sku,
                    Body = new UpdateInventoryRequest { Quantity = quantity }
                },
                cancellationToken: ct);

            // UpdateInventoryForSku sets Walmart's record to an absolute value, so the quantity now
            // on record is exactly the one we sent (the echoed response quantity is optional).
            return new WalmartSkuStock(sku, quantity);
        }, cancellationToken);
    }

    public Task<WalmartSkuStock> GetStockAsync(string sku, CancellationToken cancellationToken = default)
    {
        return InvokeAsync("get-stock", async ct =>
        {
            var summary = await _client.Inventory.GetInventoryForSku(
                new GetInventoryForSkuRequest { Sku = sku }, cancellationToken: ct);
            return new WalmartSkuStock(sku, QuantityOf(summary));
        }, cancellationToken);
    }

    public Task<IReadOnlyList<WalmartSyncItemResult>> SyncAsync(
        IReadOnlyList<(int CatalogItemId, int Quantity)> items, CancellationToken cancellationToken = default)
    {
        return InvokeAsync<IReadOnlyList<WalmartSyncItemResult>>("sync", async ct =>
        {
            var results = new List<WalmartSyncItemResult>(items.Count);

            foreach (var batch in Chunk(items, MaxBatchSize))
            {
                var bulkItems = batch
                    .Select(i => new BulkInventoryItem
                    {
                        SellerSku = WalmartSku.ForCatalogItem(i.CatalogItemId),
                        Quantity = i.Quantity
                    })
                    .ToList();

                var response = await _client.Inventory.BulkUpdateInventory(
                    new BulkUpdateInventoryOperationRequest
                    {
                        Body = new BulkUpdateInventoryRequest { Items = bulkItems }
                    },
                    cancellationToken: ct);

                // Walmart answered: items it could not process are listed in `errors`; everything
                // else in the batch was accepted. Report per SKU accordingly.
                var failedSkus = new HashSet<string>(
                    (response.Errors ?? Array.Empty<BulkItemError>())
                        .Where(e => e.SellerSku is not null)
                        .Select(e => e.SellerSku!),
                    StringComparer.Ordinal);

                if (failedSkus.Count > 0)
                {
                    _logger.LogWarning("Walmart sync: {Count} SKU(s) rejected in a batch of {BatchSize}.",
                        failedSkus.Count, bulkItems.Count);
                }

                foreach (var item in bulkItems)
                {
                    var status = failedSkus.Contains(item.SellerSku)
                        ? WalmartSyncStatus.Failed
                        : WalmartSyncStatus.Updated;
                    results.Add(new WalmartSyncItemResult(item.SellerSku, status));
                }
            }

            return results;
        }, cancellationToken);
    }

    public Task<IReadOnlyList<WalmartSkuStock>> ListAllStockAsync(CancellationToken cancellationToken = default)
    {
        return InvokeAsync<IReadOnlyList<WalmartSkuStock>>("list-stock", async ct =>
        {
            // One line per SKU, summing across ship nodes if a SKU appears more than once.
            var totals = new Dictionary<string, int>(StringComparer.Ordinal);
            string? nextToken = null;
            var pages = 0;

            do
            {
                var response = await _client.Inventory.GetInventorySummaries(
                    new GetInventorySummariesRequest { PageSize = SummaryPageSize, NextToken = nextToken },
                    cancellationToken: ct);

                foreach (var summary in response.InventorySummaries ?? Array.Empty<InventorySummary>())
                {
                    totals[summary.SellerSku] = totals.TryGetValue(summary.SellerSku, out var running)
                        ? running + QuantityOf(summary)
                        : QuantityOf(summary);
                }

                var newToken = response.Pagination?.NextToken;
                if (string.IsNullOrEmpty(newToken) || string.Equals(newToken, nextToken, StringComparison.Ordinal))
                {
                    // End of the list, or the provider failed to advance the cursor — stop.
                    nextToken = null;
                }
                else
                {
                    nextToken = newToken;
                }

                if (++pages > MaxSummaryPages && nextToken is not null)
                {
                    // Returning here would hand back a silently-truncated report, which is a wrong
                    // answer — fail loudly instead.
                    throw new WalmartInventoryException(
                        $"Walmart returned more than {MaxSummaryPages} pages of inventory summaries; the report is incomplete.");
                }
            }
            while (nextToken is not null);

            return totals.Select(kvp => new WalmartSkuStock(kvp.Key, kvp.Value)).ToList();
        }, cancellationToken);
    }

    /// <summary>
    /// Walmart's recorded quantity for a SKU: the declared on-hand (what the set operation writes),
    /// falling back to what is fulfillable, then 0 when the marketplace reports neither.
    /// </summary>
    private static int QuantityOf(InventorySummary summary) =>
        summary.InventoryDetails?.OnHandQuantity
        ?? summary.InventoryDetails?.FulfillableQuantity
        ?? 0;

    private static IEnumerable<IReadOnlyList<(int CatalogItemId, int Quantity)>> Chunk(
        IReadOnlyList<(int CatalogItemId, int Quantity)> source, int size)
    {
        for (var i = 0; i < source.Count; i += size)
        {
            yield return source.Skip(i).Take(size).ToList();
        }
    }

    /// <summary>
    /// Runs one SDK interaction under a whole-operation deadline and translates every SDK failure
    /// into <see cref="WalmartInventoryException"/>. The caller's own cancellation is left to propagate.
    /// </summary>
    private async Task<T> InvokeAsync<T>(string operation, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(OperationBudget);

        try
        {
            return await action(cts.Token);
        }
        catch (ApiException ex)
        {
            // Covers ApiException<TError> (typed error status) and ResponseDeserializationException
            // (a drifted/malformed body). The HTTP status is always present; carry it as the
            // discriminator. Never surface the SDK message (it carries the request URL) to callers.
            _logger.LogWarning("Walmart {Operation} returned HTTP {Status}.", operation, (int)ex.StatusCode);
            throw new WalmartInventoryException(
                $"Walmart {operation} returned status {(int)ex.StatusCode}.", ex.StatusCode, ex);
        }
        catch (AuthSchemeException ex)
        {
            // Our credentials could not be applied — not the caller's fault; no usable status.
            _logger.LogWarning("Walmart {Operation} failed to authenticate.", operation);
            throw new WalmartInventoryException($"Walmart {operation} could not authenticate with the marketplace.", null, ex);
        }
        catch (SdkConnectionException ex)
        {
            // Includes SdkTimeoutException. Nothing usable answered; the outcome of a write is
            // unknown and is settled by re-reading via the reconciliation/get endpoints.
            _logger.LogWarning("Walmart {Operation} could not reach the marketplace.", operation);
            throw new WalmartInventoryException($"Walmart {operation} could not reach the marketplace.", null, ex);
        }
        catch (SdkException ex)
        {
            _logger.LogWarning("Walmart {Operation} failed.", operation);
            throw new WalmartInventoryException($"Walmart {operation} failed.", null, ex);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Our budget elapsed, not the caller's cancellation.
            _logger.LogWarning("Walmart {Operation} exceeded its {Seconds}s budget.", operation, (int)OperationBudget.TotalSeconds);
            throw new WalmartInventoryException($"Walmart {operation} did not complete within {(int)OperationBudget.TotalSeconds}s.");
        }
    }
}
