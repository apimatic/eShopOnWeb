using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using WalmartApIs;
using WalmartApIs.Core.Exceptions;
using WalmartApIs.Errors;
using WalmartApIs.Models;
using WalmartApIs.Requests.Inventory;

namespace Microsoft.eShopWeb.PublicApi.Walmart;

/// <summary>
/// Talks to Walmart Marketplace through the wm-v4 SDK's Inventory controller. Translates every SDK failure
/// (API error, connection drop, timeout, auth failure, undecodable body) into <see cref="WalmartIntegrationException"/>
/// at this boundary, so callers see one failure type.
/// </summary>
public sealed class WalmartInventoryService : IWalmartInventoryService
{
    // Hand-driven paging never trusts the provider to stop it; cap the walk (500/page x 50 = 25k SKUs).
    private const int MaxPages = 50;

    // A whole-call budget. options.Retry.Timeout bounds one attempt; this bounds the request the caller waits on.
    private static readonly TimeSpan RequestBudget = TimeSpan.FromSeconds(60);

    private readonly WalmartApIsClient _client;
    private readonly IReadRepository<CatalogItem> _catalog;
    private readonly ILogger<WalmartInventoryService> _logger;

    public WalmartInventoryService(
        WalmartApIsClient client,
        IReadRepository<CatalogItem> catalog,
        ILogger<WalmartInventoryService> logger)
    {
        _client = client;
        _catalog = catalog;
        _logger = logger;
    }

    public async Task<WalmartSkuStock> SetStockAsync(string sku, int quantity, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(ct => _client.Inventory.UpdateInventoryForSku(
                new UpdateInventoryForSkuRequest
                {
                    Sku = sku,
                    Body = new UpdateInventoryRequest { Quantity = quantity },
                },
                cancellationToken: ct), cancellationToken);

            return new WalmartSkuStock(sku, response.Quantity ?? quantity);
        }
        catch (ApiException<UpdateInventoryForSkuError> ex)
        {
            throw ToIntegrationException(ex, ex.Error.TryGetErrorList(out var list) ? Describe(list) : null);
        }
        catch (ResponseDeserializationException ex)
        {
            throw UndecodableResponse(ex);
        }
        // Transport/timeout: the write may still have landed — settle the outcome by re-reading instead of failing blind.
        catch (SdkTimeoutException ex)
        {
            return await SettleUnknownAsync(sku, quantity, ex, cancellationToken);
        }
        catch (SdkConnectionException ex)
        {
            return await SettleUnknownAsync(sku, quantity, ex, cancellationToken);
        }
        catch (AuthSchemeException ex)
        {
            throw CredentialsRejected(ex);
        }
    }

    public async Task<WalmartSkuStock> GetStockAsync(string sku, CancellationToken cancellationToken)
    {
        try
        {
            var summary = await Bounded(ct => _client.Inventory.GetInventoryForSku(
                new GetInventoryForSkuRequest { Sku = sku }, cancellationToken: ct), cancellationToken);

            return new WalmartSkuStock(sku, summary.InventoryDetails?.OnHandQuantity ?? 0);
        }
        catch (ApiException<GetInventoryForSkuError> ex)
        {
            throw ToIntegrationException(ex, ex.Error.TryGetErrorList(out var list) ? Describe(list) : null);
        }
        catch (ResponseDeserializationException ex)
        {
            throw UndecodableResponse(ex);
        }
        catch (SdkTimeoutException ex)
        {
            throw Unreachable(ex);
        }
        catch (SdkConnectionException ex)
        {
            throw Unreachable(ex);
        }
        catch (AuthSchemeException ex)
        {
            throw CredentialsRejected(ex);
        }
    }

    public async Task<IReadOnlyList<WalmartSyncResultItem>> SyncCatalogAsync(CancellationToken cancellationToken)
    {
        var items = await _catalog.ListAsync(cancellationToken);
        var results = new List<WalmartSyncResultItem>(items.Count);

        foreach (var item in items)
        {
            var sku = WalmartSkus.ForCatalogItem(item.Id);
            try
            {
                await Bounded(ct => _client.Inventory.UpdateInventoryForSku(
                    new UpdateInventoryForSkuRequest
                    {
                        Sku = sku,
                        Body = new UpdateInventoryRequest { Quantity = item.AvailableStock },
                    },
                    cancellationToken: ct), cancellationToken);

                results.Add(new WalmartSyncResultItem(sku, "updated"));
            }
            catch (SdkException ex)
            {
                // Per-SKU failure: record it and keep going. The catalog-wide comparison report is the sweep.
                _logger.LogWarning(ex, "Walmart sync failed for SKU {Sku}", sku);
                results.Add(new WalmartSyncResultItem(sku, "failed"));
            }
        }

        return results;
    }

    public async Task<WalmartStockReport> GetStockComparisonAsync(CancellationToken cancellationToken)
    {
        var walmartSummaries = new List<InventorySummary>();
        var partial = false;

        try
        {
            string? nextToken = null;
            var pages = 0;
            do
            {
                var token = nextToken;
                var response = await Bounded(ct => _client.Inventory.GetInventorySummaries(
                    new GetInventorySummariesRequest { NextToken = token }, cancellationToken: ct), cancellationToken);

                if (response.InventorySummaries is { Count: > 0 } summaries)
                {
                    walmartSummaries.AddRange(summaries);
                }

                nextToken = response.Pagination?.NextToken;
                pages++;

                if (pages >= MaxPages)
                {
                    // Cap hit: if the provider still offers more, the result is truncated — say so in the result.
                    partial = !string.IsNullOrEmpty(nextToken);
                    break;
                }
            }
            while (!string.IsNullOrEmpty(nextToken));
        }
        catch (ApiException<GetInventorySummariesError> ex)
        {
            throw ToIntegrationException(ex, ex.Error.TryGetErrorList(out var list) ? Describe(list) : null);
        }
        catch (ResponseDeserializationException ex)
        {
            throw UndecodableResponse(ex);
        }
        catch (SdkTimeoutException ex)
        {
            throw Unreachable(ex);
        }
        catch (SdkConnectionException ex)
        {
            throw Unreachable(ex);
        }
        catch (AuthSchemeException ex)
        {
            throw CredentialsRejected(ex);
        }

        return BuildReport(walmartSummaries, await _catalog.ListAsync(cancellationToken), partial);
    }

    private static WalmartStockReport BuildReport(
        IReadOnlyList<InventorySummary> walmartSummaries,
        IReadOnlyList<CatalogItem> catalogItems,
        bool partial)
    {
        var catalogById = catalogItems.ToDictionary(i => i.Id);
        var items = new List<WalmartStockComparisonItem>();
        var seenSkus = new HashSet<string>(StringComparer.Ordinal);
        var matchedCatalogIds = new HashSet<int>();

        // Every SKU Walmart holds, lined up against eShop's level for the matching catalog item.
        foreach (var summary in walmartSummaries)
        {
            var sku = summary.SellerSku;
            if (!seenSkus.Add(sku))
            {
                continue; // same SKU reported for more than one ship node — keep the first
            }

            var walmartQuantity = summary.InventoryDetails?.OnHandQuantity ?? 0;
            int? eshopQuantity = null;
            if (WalmartSkus.TryGetCatalogItemId(sku, out var catalogItemId)
                && catalogById.TryGetValue(catalogItemId, out var catalogItem))
            {
                eshopQuantity = catalogItem.AvailableStock;
                matchedCatalogIds.Add(catalogItemId);
            }

            items.Add(new WalmartStockComparisonItem(sku, walmartQuantity, eshopQuantity));
        }

        // Catalog items that exist only on the eShop side (no matching Walmart SKU) → walmartQuantity null.
        foreach (var catalogItem in catalogItems)
        {
            if (matchedCatalogIds.Contains(catalogItem.Id))
            {
                continue;
            }

            var sku = WalmartSkus.ForCatalogItem(catalogItem.Id);
            if (seenSkus.Contains(sku))
            {
                continue;
            }

            items.Add(new WalmartStockComparisonItem(sku, null, catalogItem.AvailableStock));
        }

        return new WalmartStockReport(items, partial);
    }

    private async Task<WalmartSkuStock> SettleUnknownAsync(string sku, int quantity, SdkException cause, CancellationToken cancellationToken)
    {
        _logger.LogWarning(cause, "Walmart stock update for SKU {Sku} did not confirm; re-reading to settle the outcome", sku);
        try
        {
            var summary = await Bounded(ct => _client.Inventory.GetInventoryForSku(
                new GetInventoryForSkuRequest { Sku = sku }, cancellationToken: ct), cancellationToken);

            var onHand = summary.InventoryDetails?.OnHandQuantity;
            if (onHand == quantity)
            {
                // The write did land despite the transport failure.
                return new WalmartSkuStock(sku, quantity);
            }

            throw new WalmartIntegrationException(
                $"Walmart stock update for SKU {sku} could not be confirmed; Walmart records {(onHand?.ToString() ?? "no")} unit(s).",
                cause);
        }
        catch (SdkException reReadFailure)
        {
            throw new WalmartIntegrationException(
                $"Walmart stock update for SKU {sku} could not be confirmed; the outcome is unknown.", reReadFailure);
        }
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(RequestBudget);
        return await call(cts.Token);
    }

    private static string Describe(ErrorList list) =>
        string.Join("; ", list.Errors.Select(e => $"{e.Code}: {e.Message}"));

    private static WalmartIntegrationException ToIntegrationException(ApiException ex, string? detail) =>
        new($"Walmart returned {(int)ex.StatusCode} ({ex.StatusCode}){(detail is null ? string.Empty : $": {detail}")}",
            ex.StatusCode, ex);

    private static WalmartIntegrationException UndecodableResponse(ResponseDeserializationException ex) =>
        new("Walmart returned a response that could not be processed.", ex.StatusCode, ex);

    private static WalmartIntegrationException Unreachable(SdkException ex) =>
        new("Walmart could not be reached.", ex);

    private static WalmartIntegrationException CredentialsRejected(AuthSchemeException ex) =>
        new("Walmart credentials were rejected.", ex);
}
