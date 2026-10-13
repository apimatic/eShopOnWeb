using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WalmartApIs;
using WalmartApIs.Core.ErrorResponse;
using WalmartApIs.Core.Exceptions;
using WalmartApIs.Errors;
using WalmartApIs.Models;
using WalmartApIs.Requests.Inventory;

namespace Microsoft.eShopWeb.PublicApi.Walmart;

/// <summary>
/// Wraps the Walmart APIs SDK (<c>client.Inventory</c>) for the four stock flows, translating SDK
/// failures to <see cref="WalmartIntegrationException"/> and bounding every call with a deadline.
/// </summary>
public sealed class WalmartInventoryService : IWalmartInventoryService
{
    private const int BulkBatchSize = 500;   // BulkUpdateInventoryRequest.Items max is 500
    private const int SummariesPageSize = 500;
    private const int MaxSummariesPages = 1000;

    private static readonly TimeSpan SingleCallBudget = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FanOutBudget = TimeSpan.FromSeconds(120);

    private readonly WalmartApIsClient _client;
    private readonly ILogger<WalmartInventoryService> _logger;

    public WalmartInventoryService(WalmartApIsClient client, ILogger<WalmartInventoryService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<WalmartStockLevel> SetStockAsync(string sku, int quantity, CancellationToken cancellationToken)
    {
        var response = await CallAsync<UpdateInventoryResponse, UpdateInventoryForSkuError>(
            ct => _client.Inventory.UpdateInventoryForSku(
                new UpdateInventoryForSkuRequest
                {
                    Sku = sku,
                    Body = new UpdateInventoryRequest { Quantity = quantity },
                },
                cancellationToken: ct),
            e => e.TryGetErrorList(out var el) ? el : null,
            nameof(_client.Inventory.UpdateInventoryForSku),
            SingleCallBudget,
            cancellationToken);

        _logger.LogInformation("Walmart stock set for SKU {Sku} to {Quantity}", sku, quantity);
        return new WalmartStockLevel(sku, response.Quantity ?? quantity);
    }

    public async Task<WalmartStockLevel> GetStockAsync(string sku, CancellationToken cancellationToken)
    {
        var summary = await CallAsync<InventorySummary, GetInventoryForSkuError>(
            ct => _client.Inventory.GetInventoryForSku(
                new GetInventoryForSkuRequest { Sku = sku },
                cancellationToken: ct),
            e => e.TryGetErrorList(out var el) ? el : null,
            nameof(_client.Inventory.GetInventoryForSku),
            SingleCallBudget,
            cancellationToken);

        return new WalmartStockLevel(sku, ReadQuantity(summary));
    }

    public async Task<IReadOnlyList<WalmartSyncItemResult>> SyncAsync(
        IReadOnlyCollection<WalmartSkuQuantity> items, CancellationToken cancellationToken)
    {
        var results = new List<WalmartSyncItemResult>(items.Count);

        foreach (var batch in items.Chunk(BulkBatchSize))
        {
            var body = new BulkUpdateInventoryRequest
            {
                Items = batch
                    .Select(i => new BulkInventoryItem { SellerSku = i.Sku, Quantity = i.Quantity })
                    .ToList(),
            };

            try
            {
                var response = await CallAsync<BulkUpdateInventoryResponse, BulkUpdateInventoryError>(
                    ct => _client.Inventory.BulkUpdateInventory(
                        new BulkUpdateInventoryOperationRequest { Body = body },
                        cancellationToken: ct),
                    e => e.TryGetErrorList(out var el) ? el : null,
                    nameof(_client.Inventory.BulkUpdateInventory),
                    FanOutBudget,
                    cancellationToken);

                var succeeded = response.Succeeded?
                    .Select(s => s.SellerSku)
                    .Where(s => s is not null)
                    .ToHashSet(StringComparer.Ordinal) ?? new HashSet<string?>(StringComparer.Ordinal);

                foreach (var item in batch)
                {
                    results.Add(new WalmartSyncItemResult(
                        item.Sku,
                        succeeded.Contains(item.Sku) ? WalmartSyncItemResult.Updated : WalmartSyncItemResult.Failed));
                }
            }
            catch (WalmartIntegrationException ex)
                when (ex.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden))
            {
                // A whole-batch fault that is not a credential problem: report every SKU in this
                // batch as failed and keep going. The idempotent re-sync and the Flow-3 report
                // settle the outcome; a credential fault (above) is rethrown so the operator sees it.
                _logger.LogWarning(ex, "Walmart bulk inventory batch failed for {Count} SKUs", batch.Length);
                foreach (var item in batch)
                {
                    results.Add(new WalmartSyncItemResult(item.Sku, WalmartSyncItemResult.Failed));
                }
            }
        }

        return results;
    }

    public async Task<WalmartStockListing> ListAllAsync(CancellationToken cancellationToken)
    {
        var items = new List<WalmartStockLevel>();
        string? nextToken = null;
        var pages = 0;
        var truncated = false;

        while (true)
        {
            var token = nextToken;
            var response = await CallAsync<GetInventorySummariesResponse, GetInventorySummariesError>(
                ct => _client.Inventory.GetInventorySummaries(
                    new GetInventorySummariesRequest { PageSize = SummariesPageSize, NextToken = token },
                    cancellationToken: ct),
                e => e.TryGetErrorList(out var el) ? el : null,
                nameof(_client.Inventory.GetInventorySummaries),
                FanOutBudget,
                cancellationToken);

            foreach (var summary in response.InventorySummaries ?? Array.Empty<InventorySummary>())
            {
                items.Add(new WalmartStockLevel(summary.SellerSku, ReadQuantity(summary)));
            }

            nextToken = response.Pagination?.NextToken;

            if (string.IsNullOrEmpty(nextToken))
            {
                break;
            }

            if (++pages >= MaxSummariesPages)
            {
                // Hard cap reached with more pages still available: stop and tell the caller.
                truncated = true;
                _logger.LogWarning("Walmart inventory listing capped at {MaxPages} pages; result is partial", MaxSummariesPages);
                break;
            }
        }

        return new WalmartStockListing(items, truncated);
    }

    /// <summary>
    /// Reads Walmart's recorded quantity for a SKU from the summary's inventory breakdown. The
    /// set value maps to on-hand, so prefer <c>onHandQuantity</c> and fall back to
    /// <c>fulfillableQuantity</c>; null when neither is present.
    /// </summary>
    private static int? ReadQuantity(InventorySummary summary) =>
        summary.InventoryDetails?.OnHandQuantity ?? summary.InventoryDetails?.FulfillableQuantity;

    /// <summary>
    /// Invokes an SDK operation under a whole-call deadline and translates every SDK failure to
    /// <see cref="WalmartIntegrationException"/>. <paramref name="readErrors"/> pulls the typed
    /// <c>ErrorList</c> off the operation's own error type.
    /// </summary>
    private async Task<TResponse> CallAsync<TResponse, TError>(
        Func<CancellationToken, Task<TResponse>> call,
        Func<TError, ErrorList?> readErrors,
        string operation,
        TimeSpan budget,
        CancellationToken cancellationToken)
        where TError : ApiError
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(budget);

        try
        {
            return await call(cts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Our own deadline fired (not the caller's abort): treat as a timeout.
            throw new WalmartIntegrationException(operation, $"Walmart did not respond within {budget}.", null, null);
        }
        catch (ApiException<TError> ex)
        {
            var errorList = readErrors(ex.Error);
            var message = errorList?.Errors is { Count: > 0 } errors
                ? string.Join("; ", errors.Select(e => $"{e.Code}: {e.Message}"))
                : $"Walmart returned HTTP {(int)ex.StatusCode}.";
            _logger.LogWarning("{Operation} returned {Status}: {Message}", operation, (int)ex.StatusCode, message);
            throw new WalmartIntegrationException(operation, message, ex.StatusCode, ex);
        }
        catch (ResponseDeserializationException ex)
        {
            throw new WalmartIntegrationException(
                operation, "Walmart returned a response that could not be processed.", ex.StatusCode, ex);
        }
        catch (SdkTimeoutException ex)
        {
            throw new WalmartIntegrationException(operation, $"Walmart did not respond within {ex.Timeout}.", null, ex);
        }
        catch (SdkConnectionException ex)
        {
            throw new WalmartIntegrationException(operation, "Walmart is currently unreachable.", null, ex);
        }
        catch (AuthSchemeException ex)
        {
            // Our credentials could not be applied/accepted — a configuration fault. Surface it as
            // 401 so callers map it to "provider unavailable" rather than a caller error.
            _logger.LogError(ex, "{Operation} failed to authenticate with Walmart", operation);
            throw new WalmartIntegrationException(operation, "Walmart credentials were rejected.", HttpStatusCode.Unauthorized, ex);
        }
    }
}
