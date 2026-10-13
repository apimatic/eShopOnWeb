using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Marketplace;
using Microsoft.eShopWeb.Infrastructure.Marketplace.Walmart.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Marketplace.Walmart;

/// <summary>
/// Talks to Walmart's Inventory API (api-specs/walmart/inventory.json) to set, read, bulk-update
/// and list seller inventory. Every request carries a Bearer access token obtained from the
/// authorization flow; every URL is resolved through the base-URL override.
/// </summary>
internal sealed class WalmartInventoryService : IWalmartInventoryService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IWalmartAccessTokenProvider _tokenProvider;
    private readonly WalmartUrlResolver _urlResolver;
    private readonly ILogger<WalmartInventoryService> _logger;

    public WalmartInventoryService(
        IHttpClientFactory httpClientFactory,
        IWalmartAccessTokenProvider tokenProvider,
        IOptions<WalmartSettings> settings,
        ILogger<WalmartInventoryService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _tokenProvider = tokenProvider;
        _urlResolver = new WalmartUrlResolver(settings.Value.BaseUrl);
        _logger = logger;
    }

    public async Task<WalmartSkuStock> SetStockAsync(string sku, int quantity, CancellationToken cancellationToken = default)
    {
        var uri = _urlResolver.Resolve(WalmartContract.InventoryServer, $"{WalmartContract.InventoryItemsPath}/{Uri.EscapeDataString(sku)}");
        using var request = new HttpRequestMessage(HttpMethod.Put, uri)
        {
            Content = JsonBody(new UpdateInventoryRequest { Quantity = quantity })
        };

        var body = await SendAsync<UpdateInventoryResponse>(request, cancellationToken);
        // Walmart echoes the quantity it recorded; fall back to what we sent if omitted.
        return new WalmartSkuStock(body?.SellerSku ?? sku, body?.Quantity ?? quantity);
    }

    public async Task<WalmartSkuStock> GetStockAsync(string sku, CancellationToken cancellationToken = default)
    {
        var uri = _urlResolver.Resolve(WalmartContract.InventoryServer, $"{WalmartContract.InventoryItemsPath}/{Uri.EscapeDataString(sku)}");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);

        var summary = await SendAsync<InventorySummary>(request, cancellationToken);
        return new WalmartSkuStock(summary?.SellerSku ?? sku, QuantityOf(summary));
    }

    public async Task<IReadOnlyList<WalmartSyncResult>> SyncStockAsync(
        IReadOnlyCollection<WalmartSkuStock> items, CancellationToken cancellationToken = default)
    {
        var results = new List<WalmartSyncResult>(items.Count);

        // The bulk endpoint accepts up to BulkMaxItems per request; chunk to stay within contract.
        foreach (var batch in Chunk(items, WalmartContract.BulkMaxItems))
        {
            results.AddRange(await SyncBatchAsync(batch, cancellationToken));
        }

        return results;
    }

    private async Task<IReadOnlyList<WalmartSyncResult>> SyncBatchAsync(
        IReadOnlyList<WalmartSkuStock> batch, CancellationToken cancellationToken)
    {
        var payload = new BulkUpdateInventoryRequest();
        foreach (var item in batch)
        {
            payload.Items.Add(new BulkInventoryItem { SellerSku = item.Sku, Quantity = item.Quantity });
        }

        var uri = _urlResolver.Resolve(WalmartContract.InventoryServer, WalmartContract.InventoryBulkPath);

        BulkUpdateInventoryResponse? response;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = JsonBody(payload) };
            response = await SendAsync<BulkUpdateInventoryResponse>(request, cancellationToken);
        }
        catch (WalmartApiException ex)
        {
            // A whole-batch failure (e.g. 4xx/5xx) means every SKU in it failed.
            _logger.LogError(ex, "Walmart bulk inventory update failed for {Count} SKUs.", batch.Count);
            var reason = ex.StatusCode is { } code ? $"HTTP {(int)code}" : "request failed";
            return Fail(batch, reason, ex.Message);
        }

        return MapBulkResults(batch, response);
    }

    public async Task<IReadOnlyList<WalmartSkuStock>> ListStockAsync(CancellationToken cancellationToken = default)
    {
        var all = new List<WalmartSkuStock>();
        string? nextToken = null;

        do
        {
            var query = $"?pageSize={WalmartContract.MaxPageSize}";
            if (!string.IsNullOrEmpty(nextToken))
            {
                query += $"&nextToken={Uri.EscapeDataString(nextToken)}";
            }

            var uri = _urlResolver.Resolve(WalmartContract.InventoryServer, WalmartContract.InventoryItemsPath + query);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);

            var page = await SendAsync<GetInventorySummariesResponse>(request, cancellationToken);
            if (page?.InventorySummaries is { } summaries)
            {
                foreach (var summary in summaries)
                {
                    if (!string.IsNullOrEmpty(summary.SellerSku))
                    {
                        all.Add(new WalmartSkuStock(summary.SellerSku!, QuantityOf(summary)));
                    }
                }
            }

            nextToken = page?.Pagination?.NextToken;
        }
        while (!string.IsNullOrEmpty(nextToken));

        return all;
    }

    private static IReadOnlyList<WalmartSyncResult> MapBulkResults(
        IReadOnlyList<WalmartSkuStock> batch, BulkUpdateInventoryResponse? response)
    {
        var succeeded = new HashSet<string>(StringComparer.Ordinal);
        if (response?.Succeeded is { } ok)
        {
            foreach (var entry in ok)
            {
                if (!string.IsNullOrEmpty(entry.SellerSku))
                {
                    succeeded.Add(entry.SellerSku!);
                }
            }
        }

        var errors = new Dictionary<string, BulkItemError>(StringComparer.Ordinal);
        if (response?.Errors is { } errs)
        {
            foreach (var err in errs)
            {
                if (!string.IsNullOrEmpty(err.SellerSku))
                {
                    errors[err.SellerSku!] = err;
                }
            }
        }

        var results = new List<WalmartSyncResult>(batch.Count);
        foreach (var item in batch)
        {
            if (errors.TryGetValue(item.Sku, out var err))
            {
                results.Add(new WalmartSyncResult(item.Sku, Success: false, err.Code, err.Message));
            }
            else if (succeeded.Contains(item.Sku))
            {
                results.Add(new WalmartSyncResult(item.Sku, Success: true));
            }
            else
            {
                // Walmart acknowledged the batch but did not report this SKU either way.
                results.Add(new WalmartSyncResult(item.Sku, Success: false, "no_result",
                    "Walmart did not report an outcome for this SKU."));
            }
        }

        return results;
    }

    private static IReadOnlyList<WalmartSyncResult> Fail(IReadOnlyList<WalmartSkuStock> batch, string code, string message)
    {
        var results = new List<WalmartSyncResult>(batch.Count);
        foreach (var item in batch)
        {
            results.Add(new WalmartSyncResult(item.Sku, Success: false, code, message));
        }
        return results;
    }

    // Walmart's declared quantity surfaces in inventoryDetails; prefer on-hand (what a PUT sets),
    // falling back to fulfillable (units available to sell) when on-hand is not populated.
    private static int QuantityOf(InventorySummary? summary) =>
        summary?.InventoryDetails?.OnHandQuantity ?? summary?.InventoryDetails?.FulfillableQuantity ?? 0;

    // Buffer the JSON eagerly so the request carries an explicit Content-Length (rather than
    // chunked transfer encoding), which is the most broadly compatible across HTTP servers.
    private static StringContent JsonBody<T>(T value)
    {
        var json = JsonSerializer.Serialize(value);
        return new StringContent(json, System.Text.Encoding.UTF8, "application/json");
    }

    private async Task<T?> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.ParseAdd("application/json");

        var client = _httpClientFactory.CreateClient(WalmartHttpClient.Name);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new WalmartApiException($"Failed to reach Walmart at {request.RequestUri}.", inner: ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var detail = await TryReadErrorAsync(response, cancellationToken);
                throw new WalmartApiException(
                    $"Walmart call {request.Method} {request.RequestUri} failed ({(int)response.StatusCode} {response.StatusCode}){detail}.",
                    response.StatusCode);
            }

            try
            {
                return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                throw new WalmartApiException(
                    $"Walmart response from {request.RequestUri} could not be parsed.", response.StatusCode, ex);
            }
        }
    }

    private static async Task<string> TryReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var errorList = await response.Content.ReadFromJsonAsync<ErrorList>(cancellationToken: cancellationToken);
            var first = errorList?.Errors is { Count: > 0 } list ? list[0] : null;
            return first is not null ? $": {first.Code} {first.Message}".TrimEnd() : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static IEnumerable<IReadOnlyList<T>> Chunk<T>(IReadOnlyCollection<T> source, int size)
    {
        var bucket = new List<T>(Math.Min(size, source.Count));
        foreach (var item in source)
        {
            bucket.Add(item);
            if (bucket.Count == size)
            {
                yield return bucket;
                bucket = new List<T>(size);
            }
        }
        if (bucket.Count > 0)
        {
            yield return bucket;
        }
    }
}
