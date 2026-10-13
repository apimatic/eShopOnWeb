using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Walmart;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Walmart;

/// <summary>
/// <see cref="IWalmartInventoryClient"/> built directly against the Walmart Inventory OpenAPI
/// contract (inventory.json). The injected <see cref="HttpClient"/> carries the bearer-token auth
/// handler; URLs come from <see cref="WalmartEndpointResolver"/> so the base-URL override applies.
/// </summary>
public class WalmartInventoryClient : IWalmartInventoryClient
{
    // inventory.json caps bulk updates at 500 items per request.
    private const int MaxBulkItems = 500;
    // Max page size the inventory list endpoint allows.
    private const int ListPageSize = 500;

    private readonly HttpClient _httpClient;
    private readonly WalmartEndpointResolver _resolver;
    private readonly ILogger<WalmartInventoryClient> _logger;

    public WalmartInventoryClient(HttpClient httpClient, WalmartEndpointResolver resolver, ILogger<WalmartInventoryClient> logger)
    {
        _httpClient = httpClient;
        _resolver = resolver;
        _logger = logger;
    }

    public async Task<WalmartInventoryUpdateResult> UpdateQuantityAsync(string sku, int quantity, CancellationToken cancellationToken = default)
    {
        var url = _resolver.InventoryItemUrl(sku);
        using var request = new HttpRequestMessage(HttpMethod.Put, url)
        {
            Content = JsonContent.Create(new UpdateInventoryRequest { Quantity = quantity })
        };
        request.Headers.Accept.ParseAdd("application/json");

        using var response = await SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var body = await response.Content.ReadFromJsonAsync<UpdateInventoryResponse>(cancellationToken: cancellationToken);
        var echoedSku = string.IsNullOrEmpty(body?.SellerSku) ? sku : body!.SellerSku!;
        var echoedQuantity = body?.Quantity ?? quantity;
        return new WalmartInventoryUpdateResult(echoedSku, echoedQuantity);
    }

    public async Task<WalmartInventoryRecord?> GetQuantityAsync(string sku, CancellationToken cancellationToken = default)
    {
        var url = _resolver.InventoryItemUrl(sku);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("application/json");

        using var response = await SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        await EnsureSuccessAsync(response, cancellationToken);

        var summary = await response.Content.ReadFromJsonAsync<InventorySummary>(cancellationToken: cancellationToken);
        if (summary is null)
        {
            return null;
        }

        var recordedSku = string.IsNullOrEmpty(summary.SellerSku) ? sku : summary.SellerSku!;
        return new WalmartInventoryRecord(recordedSku, ExtractQuantity(summary.InventoryDetails));
    }

    public async Task<IReadOnlyList<WalmartBulkItemResult>> UpdateQuantitiesAsync(IReadOnlyCollection<WalmartInventoryItem> items, CancellationToken cancellationToken = default)
    {
        var results = new List<WalmartBulkItemResult>(items.Count);

        foreach (var chunk in Chunk(items, MaxBulkItems))
        {
            var payload = new BulkUpdateInventoryRequest
            {
                Items = chunk.Select(i => new BulkInventoryItem { SellerSku = i.Sku, Quantity = i.Quantity }).ToList()
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, _resolver.InventoryBulkUrl())
            {
                Content = JsonContent.Create(payload)
            };
            request.Headers.Accept.ParseAdd("application/json");

            using var response = await SendAsync(request, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);

            var body = await response.Content.ReadFromJsonAsync<BulkUpdateInventoryResponse>(cancellationToken: cancellationToken);

            foreach (var ok in body?.Succeeded ?? Enumerable.Empty<UpdateInventoryResponse>())
            {
                if (!string.IsNullOrEmpty(ok.SellerSku))
                {
                    results.Add(new WalmartBulkItemResult(ok.SellerSku!, true));
                }
            }

            foreach (var err in body?.Errors ?? Enumerable.Empty<BulkItemError>())
            {
                if (!string.IsNullOrEmpty(err.SellerSku))
                {
                    results.Add(new WalmartBulkItemResult(err.SellerSku!, false, err.Code, err.Message));
                }
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<WalmartInventoryRecord>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        var records = new List<WalmartInventoryRecord>();
        string? nextToken = null;

        do
        {
            var url = _resolver.InventoryListUrl(ListPageSize, nextToken);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd("application/json");

            using var response = await SendAsync(request, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);

            var page = await response.Content.ReadFromJsonAsync<GetInventorySummariesResponse>(cancellationToken: cancellationToken);
            foreach (var summary in page?.InventorySummaries ?? Enumerable.Empty<InventorySummary>())
            {
                if (!string.IsNullOrEmpty(summary.SellerSku))
                {
                    records.Add(new WalmartInventoryRecord(summary.SellerSku!, ExtractQuantity(summary.InventoryDetails)));
                }
            }

            nextToken = page?.Pagination?.NextToken;
        }
        while (!string.IsNullOrEmpty(nextToken));

        return records;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            var uri = request.RequestUri;
            throw new WalmartApiException(
                $"Failed to reach the Walmart inventory API at {uri?.Scheme}://{uri?.Authority}.", innerException: ex);
        }
    }

    private static int ExtractQuantity(InventoryDetails? details)
    {
        // The declared quantity set via PUT maps to on-hand; a seller item surfaces it as the
        // fulfillable (available-to-sell) quantity, falling back to on-hand. Treat absence as 0.
        if (details is null)
        {
            return 0;
        }
        return details.FulfillableQuantity ?? details.OnHandQuantity ?? 0;
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string? errorCode = null;
        string detail = response.ReasonPhrase ?? response.StatusCode.ToString();
        try
        {
            var errors = await response.Content.ReadFromJsonAsync<WalmartErrorList>(cancellationToken: cancellationToken);
            var first = errors?.Errors?.FirstOrDefault();
            if (first is not null)
            {
                errorCode = first.Code;
                detail = first.Message ?? first.Code ?? detail;
            }
        }
        catch
        {
            // Non-JSON / empty error body; keep the status-based detail.
        }

        _logger.LogError("Walmart inventory API call failed with status {StatusCode} ({ErrorCode}).",
            (int)response.StatusCode, errorCode ?? "n/a");

        throw new WalmartApiException($"Walmart inventory API returned {(int)response.StatusCode}: {detail}.", response.StatusCode, errorCode);
    }

    private static IEnumerable<IReadOnlyList<WalmartInventoryItem>> Chunk(IReadOnlyCollection<WalmartInventoryItem> items, int size)
    {
        var batch = new List<WalmartInventoryItem>(size);
        foreach (var item in items)
        {
            batch.Add(item);
            if (batch.Count == size)
            {
                yield return batch;
                batch = new List<WalmartInventoryItem>(size);
            }
        }
        if (batch.Count > 0)
        {
            yield return batch;
        }
    }
}
