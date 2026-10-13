using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using WalmartApIs;
using WalmartApIs.Core.ErrorResponse;
using WalmartApIs.Core.Exceptions;
using WalmartApIs.Errors;
using WalmartApIs.Models;
using WalmartApIs.Requests.Inventory;

namespace Microsoft.eShopWeb.PublicApi.WalmartIntegration;

/// <summary>
/// Wraps the Walmart APIs SDK inventory operations behind <see cref="IWalmartInventoryService"/>,
/// translating SDK exceptions into <see cref="WalmartApiException"/> at this single boundary and
/// bounding every call with a request-linked deadline.
/// </summary>
public class WalmartInventoryService : IWalmartInventoryService
{
    // Walmart's bulk update accepts at most 500 items per call.
    private const int BulkChunkSize = 500;

    // Whole-call budgets (the per-attempt SDK Timeout is set at registration; a CancellationToken is
    // the only thing that bounds a whole call). Sync fans out over chunks, so it gets a larger budget.
    private static readonly TimeSpan SingleCallBudget = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SyncBudget = TimeSpan.FromSeconds(120);

    private readonly WalmartApIsClient _client;
    private readonly IReadRepository<CatalogItem> _catalogRepository;
    private readonly ILogger<WalmartInventoryService> _logger;

    public WalmartInventoryService(
        WalmartApIsClient client,
        IReadRepository<CatalogItem> catalogRepository,
        ILogger<WalmartInventoryService> logger)
    {
        _client = client;
        _catalogRepository = catalogRepository;
        _logger = logger;
    }

    public async Task SetStockAsync(string sku, int quantity, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(SingleCallBudget);

        _logger.LogInformation("Setting Walmart stock for SKU {Sku} to {Quantity}.", sku, quantity);
        try
        {
            await _client.Inventory.UpdateInventoryForSku(
                new UpdateInventoryForSkuRequest
                {
                    Sku = sku,
                    Body = new UpdateInventoryRequest { Quantity = quantity },
                },
                cancellationToken: cts.Token);
            _logger.LogInformation("Set Walmart stock for SKU {Sku}.", sku);
        }
        catch (ApiException<UpdateInventoryForSkuError> ex)
        {
            throw TranslateTyped(ex, ex.Error.TryGetErrorList(out var errors) ? errors : null, $"update inventory for SKU {sku}");
        }
        catch (Exception ex) when (TryTranslateCommon(ex, $"update inventory for SKU {sku}", out var translated))
        {
            throw translated;
        }
    }

    public async Task<int> GetStockAsync(string sku, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(SingleCallBudget);

        _logger.LogInformation("Reading Walmart stock for SKU {Sku}.", sku);
        try
        {
            var summary = await _client.Inventory.GetInventoryForSku(
                new GetInventoryForSkuRequest { Sku = sku },
                cancellationToken: cts.Token);
            return ReadQuantity(summary);
        }
        catch (ApiException<GetInventoryForSkuError> ex)
        {
            throw TranslateTyped(ex, ex.Error.TryGetErrorList(out var errors) ? errors : null, $"get inventory for SKU {sku}");
        }
        catch (Exception ex) when (TryTranslateCommon(ex, $"get inventory for SKU {sku}", out var translated))
        {
            throw translated;
        }
    }

    public async Task<IReadOnlyList<SkuSyncResult>> SyncCatalogAsync(CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(SyncBudget);

        var catalogItems = await _catalogRepository.ListAsync(cts.Token);
        _logger.LogInformation("Syncing stock for {Count} catalog item(s) to Walmart.", catalogItems.Count);

        var results = new List<SkuSyncResult>(catalogItems.Count);
        foreach (var chunk in Chunk(catalogItems, BulkChunkSize))
        {
            var skusInChunk = chunk.Select(ci => WalmartSku.ForCatalogItem(ci.Id)).ToList();
            var body = new BulkUpdateInventoryRequest
            {
                Items = chunk
                    .Select(ci => new BulkInventoryItem
                    {
                        SellerSku = WalmartSku.ForCatalogItem(ci.Id),
                        Quantity = ci.UnitsInStock,
                    })
                    .ToList(),
            };

            try
            {
                var response = await _client.Inventory.BulkUpdateInventory(
                    new BulkUpdateInventoryOperationRequest { Body = body },
                    cancellationToken: cts.Token);

                // A SKU Walmart reported an error for is 'failed'; every other SKU we sent is 'updated'.
                var failedSkus = response.Errors is null
                    ? new HashSet<string>(StringComparer.Ordinal)
                    : new HashSet<string>(
                        response.Errors.Where(e => e.SellerSku is not null).Select(e => e.SellerSku!),
                        StringComparer.Ordinal);

                if (response.Errors is not null)
                {
                    foreach (var error in response.Errors)
                    {
                        _logger.LogWarning(
                            "Walmart sync failed for SKU {Sku}: {Code} {Message}.",
                            error.SellerSku, error.Code, error.Message);
                    }
                }

                foreach (var sku in skusInChunk)
                {
                    results.Add(new SkuSyncResult(
                        sku,
                        failedSkus.Contains(sku) ? SkuSyncResult.Failed : SkuSyncResult.Updated));
                }
            }
            catch (ApiException<BulkUpdateInventoryError> ex)
            {
                LogTyped(ex, ex.Error.TryGetErrorList(out var errors) ? errors : null, "bulk update inventory");
                results.AddRange(skusInChunk.Select(sku => new SkuSyncResult(sku, SkuSyncResult.Failed)));
            }
            catch (Exception ex) when (IsSdkFailure(ex))
            {
                // The outcome of this chunk is unknown, but the write is an absolute set and therefore
                // idempotent: a later sync or GET re-asserts the intended levels. Report it as failed.
                _logger.LogWarning(ex, "Walmart bulk update chunk failed; reporting its SKUs as failed.");
                results.AddRange(skusInChunk.Select(sku => new SkuSyncResult(sku, SkuSyncResult.Failed)));
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<WalmartSkuStock>> ListWalmartStockAsync(CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(SingleCallBudget);

        var stock = new List<WalmartSkuStock>();
        string? nextToken = null;
        const int maxPages = 1000; // hard backstop so a mis-behaving cursor cannot loop forever
        var pages = 0;

        _logger.LogInformation("Listing all Walmart inventory summaries.");
        try
        {
            do
            {
                var response = await _client.Inventory.GetInventorySummaries(
                    new GetInventorySummariesRequest { PageSize = 500, NextToken = nextToken },
                    cancellationToken: cts.Token);

                if (response.InventorySummaries is not null)
                {
                    foreach (var summary in response.InventorySummaries)
                    {
                        stock.Add(new WalmartSkuStock(summary.SellerSku, ReadQuantity(summary)));
                    }
                }

                nextToken = response.Pagination?.NextToken;
            }
            while (!string.IsNullOrEmpty(nextToken) && ++pages < maxPages);

            if (pages >= maxPages && !string.IsNullOrEmpty(nextToken))
            {
                _logger.LogWarning("Stopped paging Walmart inventory summaries at the {MaxPages}-page backstop.", maxPages);
            }

            return stock;
        }
        catch (ApiException<GetInventorySummariesError> ex)
        {
            throw TranslateTyped(ex, ex.Error.TryGetErrorList(out var errors) ? errors : null, "list inventory summaries");
        }
        catch (Exception ex) when (TryTranslateCommon(ex, "list inventory summaries", out var translated))
        {
            throw translated;
        }
    }

    /// <summary>
    /// Reads the quantity Walmart records for a SKU. The set quantity maps to on-hand; on read-back the
    /// Walmart-compatible service may surface it as fulfillable or on-hand, so prefer fulfillable and
    /// fall back to on-hand (0 when neither is present).
    /// </summary>
    private static int ReadQuantity(InventorySummary summary)
    {
        var details = summary.InventoryDetails;
        return details?.FulfillableQuantity ?? details?.OnHandQuantity ?? 0;
    }

    private WalmartApiException TranslateTyped(ApiException ex, ErrorList? errors, string description)
    {
        LogTyped(ex, errors, description);
        var detail = DescribeErrors(errors);
        var message = detail is null
            ? $"Walmart returned {(int)ex.StatusCode} while trying to {description}."
            : $"Walmart returned {(int)ex.StatusCode} while trying to {description}: {detail}";
        return new WalmartApiException(message, ex.StatusCode, ex);
    }

    private void LogTyped(ApiException ex, ErrorList? errors, string description)
    {
        _logger.LogWarning(
            "Walmart call to {Description} returned {StatusCode}: {Detail}",
            description, (int)ex.StatusCode, DescribeErrors(errors) ?? "(no error detail)");
    }

    private static string? DescribeErrors(ErrorList? errors)
    {
        if (errors?.Errors is null || errors.Errors.Count == 0)
        {
            return null;
        }
        return string.Join("; ", errors.Errors.Select(e => $"{e.Code}: {e.Message}"));
    }

    /// <summary>
    /// Translates the non-typed SDK exception leaves (deserialization, timeout, connection, auth) into a
    /// <see cref="WalmartApiException"/>. Returns false for anything else (including the caller's own
    /// cancellation, which must propagate unwrapped).
    /// </summary>
    private bool TryTranslateCommon(Exception ex, string description, out WalmartApiException translated)
    {
        switch (ex)
        {
            case ResponseDeserializationException de:
                _logger.LogWarning(de, "Walmart response for {Description} could not be deserialized.", description);
                translated = new WalmartApiException(
                    $"Walmart returned a response that could not be processed while trying to {description}.",
                    de.StatusCode, de);
                return true;
            case SdkTimeoutException te:
                _logger.LogWarning(te, "Walmart call to {Description} timed out.", description);
                translated = new WalmartApiException($"Walmart did not answer in time while trying to {description}.", null, te);
                return true;
            case SdkConnectionException ce:
                _logger.LogWarning(ce, "Walmart was unreachable for {Description}.", description);
                translated = new WalmartApiException($"Walmart was unreachable while trying to {description}.", null, ce);
                return true;
            case AuthSchemeException ae:
                _logger.LogWarning(ae, "Walmart credentials could not be applied for {Description}.", description);
                translated = new WalmartApiException($"Walmart credentials were rejected while trying to {description}.", null, ae);
                return true;
            default:
                translated = null!;
                return false;
        }
    }

    private static bool IsSdkFailure(Exception ex) =>
        ex is ApiException or ResponseDeserializationException or SdkTimeoutException
            or SdkConnectionException or AuthSchemeException;

    private static IEnumerable<IReadOnlyList<T>> Chunk<T>(IReadOnlyList<T> source, int size)
    {
        for (var i = 0; i < source.Count; i += size)
        {
            yield return source.Skip(i).Take(size).ToList();
        }
    }
}
