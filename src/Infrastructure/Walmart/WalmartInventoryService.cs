using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Walmart;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Walmart;

/// <summary>
/// Talks to the Walmart Marketplace Inventory API (<c>api-specs/walmart/inventory.json</c>) over HTTP,
/// authenticating each call with a bearer token from <see cref="IWalmartTokenProvider"/>.
/// </summary>
public class WalmartInventoryService : IWalmartInventoryService
{
    // Contract limit: BulkUpdateInventoryRequest.items has maxItems 500.
    private const int MaxBulkItems = 500;

    // Contract limit: PageSizeParam maximum is 500.
    private const int ListPageSize = 500;

    // Modest resilience for transient failures (429 / 5xx). Inventory writes here are idempotent
    // "set to N" operations, so retrying them is safe.
    private const int MaxAttempts = 3;

    private readonly HttpClient _httpClient;
    private readonly IWalmartTokenProvider _tokenProvider;
    private readonly WalmartEndpoints _endpoints;
    private readonly ILogger<WalmartInventoryService> _logger;

    public WalmartInventoryService(
        HttpClient httpClient,
        IWalmartTokenProvider tokenProvider,
        IOptions<WalmartSettings> settings,
        ILogger<WalmartInventoryService> logger)
    {
        _httpClient = httpClient;
        _tokenProvider = tokenProvider;
        _endpoints = new WalmartEndpoints(settings.Value.BaseUrl);
        _logger = logger;
    }

    public async Task<WalmartInventorySetResult> SetInventoryAsync(string sku, int quantity, CancellationToken cancellationToken = default)
    {
        var body = new UpdateInventoryRequest { Quantity = quantity };

        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Put, _endpoints.InventoryItem(sku))
            {
                Content = JsonContent.Create(body)
            },
            cancellationToken).ConfigureAwait(false);

        await EnsureSuccessAsync(response, $"update inventory for SKU '{sku}'", cancellationToken).ConfigureAwait(false);

        var payload = await response.Content
            .ReadFromJsonAsync<UpdateInventoryResponse>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        // The contract echoes the accepted quantity; fall back to what we requested if it is absent.
        var effectiveQuantity = payload?.Quantity ?? quantity;
        return new WalmartInventorySetResult(sku, effectiveQuantity);
    }

    public async Task<WalmartInventorySnapshot?> GetInventoryAsync(string sku, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, _endpoints.InventoryItem(sku)),
            cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, $"read inventory for SKU '{sku}'", cancellationToken).ConfigureAwait(false);

        var summary = await response.Content
            .ReadFromJsonAsync<InventorySummary>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (summary is null)
        {
            throw new WalmartApiException($"Walmart returned an empty inventory summary for SKU '{sku}'.");
        }

        return new WalmartInventorySnapshot(
            string.IsNullOrEmpty(summary.SellerSku) ? sku : summary.SellerSku,
            QuantityOf(summary));
    }

    public async Task<IReadOnlyList<WalmartSyncResult>> SyncInventoryAsync(
        IReadOnlyCollection<WalmartInventoryUpdate> items,
        CancellationToken cancellationToken = default)
    {
        var results = new List<WalmartSyncResult>(items.Count);

        foreach (var batch in Chunk(items, MaxBulkItems))
        {
            var request = new BulkUpdateInventoryRequest
            {
                Items = batch
                    .Select(i => new BulkInventoryItem { SellerSku = i.Sku, Quantity = i.Quantity })
                    .ToList()
            };

            using var response = await SendAsync(
                () => new HttpRequestMessage(HttpMethod.Post, _endpoints.BulkInventory())
                {
                    Content = JsonContent.Create(request)
                },
                cancellationToken).ConfigureAwait(false);

            await EnsureSuccessAsync(response, "bulk-update inventory", cancellationToken).ConfigureAwait(false);

            var payload = await response.Content
                .ReadFromJsonAsync<BulkUpdateInventoryResponse>(cancellationToken: cancellationToken)
                .ConfigureAwait(false) ?? new BulkUpdateInventoryResponse();

            results.AddRange(MapBatchResults(batch, payload));
        }

        return results;
    }

    public async Task<IReadOnlyList<WalmartInventorySnapshot>> ListInventoryAsync(CancellationToken cancellationToken = default)
    {
        var snapshots = new List<WalmartInventorySnapshot>();
        var seenTokens = new HashSet<string>();
        string? nextToken = null;

        do
        {
            using var response = await SendAsync(
                () => new HttpRequestMessage(HttpMethod.Get, _endpoints.InventorySummaries(ListPageSize, nextToken)),
                cancellationToken).ConfigureAwait(false);

            await EnsureSuccessAsync(response, "list inventory summaries", cancellationToken).ConfigureAwait(false);

            var page = await response.Content
                .ReadFromJsonAsync<GetInventorySummariesResponse>(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            foreach (var summary in page?.InventorySummaries ?? new List<InventorySummary>())
            {
                if (!string.IsNullOrEmpty(summary.SellerSku))
                {
                    snapshots.Add(new WalmartInventorySnapshot(summary.SellerSku, QuantityOf(summary)));
                }
            }

            nextToken = page?.Pagination?.NextToken;

            // Guard against a service that keeps returning the same token (would otherwise loop forever).
            if (!string.IsNullOrEmpty(nextToken) && !seenTokens.Add(nextToken))
            {
                _logger.LogWarning("Walmart inventory listing returned a repeated page token; stopping pagination.");
                break;
            }
        }
        while (!string.IsNullOrEmpty(nextToken));

        return snapshots;
    }

    private static IEnumerable<WalmartSyncResult> MapBatchResults(
        IReadOnlyList<WalmartInventoryUpdate> batch,
        BulkUpdateInventoryResponse payload)
    {
        var succeeded = new HashSet<string>(
            (payload.Succeeded ?? new List<UpdateInventoryResponse>())
                .Select(s => s.SellerSku)
                .Where(s => !string.IsNullOrEmpty(s))!,
            StringComparer.Ordinal);

        var errors = (payload.Errors ?? new List<BulkItemError>())
            .Where(e => !string.IsNullOrEmpty(e.SellerSku))
            .GroupBy(e => e.SellerSku!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var item in batch)
        {
            if (errors.TryGetValue(item.Sku, out var error))
            {
                var message = string.IsNullOrEmpty(error.Message) ? error.Code : error.Message;
                yield return new WalmartSyncResult(item.Sku, WalmartSyncStatus.Failed, message);
            }
            else if (succeeded.Contains(item.Sku))
            {
                yield return new WalmartSyncResult(item.Sku, WalmartSyncStatus.Updated);
            }
            else
            {
                // Walmart acknowledged the batch but did not report this SKU either way.
                yield return new WalmartSyncResult(item.Sku, WalmartSyncStatus.Failed, "No result returned by Walmart for this SKU.");
            }
        }
    }

    private static int QuantityOf(InventorySummary summary) =>
        summary.InventoryDetails?.OnHandQuantity
        ?? summary.InventoryDetails?.FulfillableQuantity
        ?? 0;

    private static IEnumerable<List<T>> Chunk<T>(IEnumerable<T> source, int size)
    {
        var bucket = new List<T>(size);
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

    /// <summary>
    /// Sends a freshly built request (the factory is re-invoked per attempt so the body can be re-sent),
    /// attaching a bearer token and retrying transient 429/5xx responses.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);

            using var request = requestFactory();
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.ParseAdd("application/json");

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (attempt < MaxAttempts)
            {
                _logger.LogWarning(ex, "Transport error calling Walmart (attempt {Attempt}/{Max}); retrying.", attempt, MaxAttempts);
                await DelayAsync(attempt, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (IsTransient(response.StatusCode) && attempt < MaxAttempts)
            {
                _logger.LogWarning("Walmart returned {Status} (attempt {Attempt}/{Max}); retrying.", (int)response.StatusCode, attempt, MaxAttempts);
                response.Dispose();
                await DelayAsync(attempt, cancellationToken).ConfigureAwait(false);
                continue;
            }

            return response;
        }
    }

    private static bool IsTransient(HttpStatusCode status) =>
        status == HttpStatusCode.TooManyRequests || (int)status >= 500;

    private static Task DelayAsync(int attempt, CancellationToken cancellationToken) =>
        Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string action, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = await TryReadErrorAsync(response, cancellationToken).ConfigureAwait(false);
        throw new WalmartApiException(
            $"Walmart failed to {action} ({(int)response.StatusCode} {response.StatusCode}){detail}.");
    }

    private static async Task<string> TryReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var errors = await response.Content
                .ReadFromJsonAsync<ErrorList>(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var first = errors?.Errors?.FirstOrDefault();
            if (first is not null)
            {
                return string.IsNullOrEmpty(first.Message) ? $": {first.Code}" : $": {first.Code} - {first.Message}";
            }
        }
        catch
        {
            // Response body was not a parseable ErrorList; fall through to a status-only message.
        }
        return string.Empty;
    }
}
