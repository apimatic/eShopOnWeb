using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Marketplace;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Walmart;

/// <summary>
/// Implements <see cref="IWalmartInventoryService"/> against the Walmart Inventory API
/// (api-specs/walmart/inventory.json). The injected <see cref="HttpClient"/> is pre-configured with
/// the resolved inventory base address and an auth handler that mints/attaches the bearer token.
/// </summary>
public class WalmartInventoryService : IWalmartInventoryService
{
    // Contract limit: bulk accepts a maximum of 500 items per request.
    private const int BulkBatchSize = 500;
    // Contract limit: pageSize maximum is 500 when listing inventory summaries.
    private const int ListPageSize = 500;

    private readonly HttpClient _httpClient;
    private readonly IAppLogger<WalmartInventoryService> _logger;

    public WalmartInventoryService(HttpClient httpClient, IAppLogger<WalmartInventoryService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<WalmartInventoryRecord> SetStockAsync(string sku, int quantity, CancellationToken cancellationToken = default)
    {
        var request = new UpdateInventoryRequest { Quantity = quantity };
        using var response = await _httpClient.PutAsJsonAsync($"inventory/v4/items/{Uri.EscapeDataString(sku)}", request, cancellationToken);
        await EnsureSuccessAsync(response, $"update inventory for SKU '{sku}'", cancellationToken);

        var body = await ReadJsonAsync<UpdateInventoryResponse>(response, cancellationToken);
        // The contract echoes the accepted quantity; fall back to the requested value if absent.
        return new WalmartInventoryRecord(sku, body?.Quantity ?? quantity);
    }

    public async Task<WalmartInventoryRecord?> GetStockAsync(string sku, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"inventory/v4/items/{Uri.EscapeDataString(sku)}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, $"read inventory for SKU '{sku}'", cancellationToken);

        var summary = await ReadJsonAsync<InventorySummary>(response, cancellationToken);
        if (summary is null)
        {
            return null;
        }

        return new WalmartInventoryRecord(summary.SellerSku ?? sku, summary.InventoryDetails.ResolveQuantity());
    }

    public async Task<WalmartBulkResult> SyncStockAsync(IReadOnlyList<WalmartInventoryUpdate> updates, CancellationToken cancellationToken = default)
    {
        var results = new List<WalmartSyncResult>(updates.Count);

        for (var offset = 0; offset < updates.Count; offset += BulkBatchSize)
        {
            var batch = new List<WalmartInventoryUpdate>();
            for (var i = offset; i < Math.Min(offset + BulkBatchSize, updates.Count); i++)
            {
                batch.Add(updates[i]);
            }

            results.AddRange(await SyncBatchAsync(batch, cancellationToken));
        }

        return new WalmartBulkResult(results);
    }

    private async Task<IReadOnlyList<WalmartSyncResult>> SyncBatchAsync(IReadOnlyList<WalmartInventoryUpdate> batch, CancellationToken cancellationToken)
    {
        var request = new BulkUpdateInventoryRequest
        {
            Items = new List<BulkInventoryItem>(batch.Count)
        };
        foreach (var update in batch)
        {
            request.Items.Add(new BulkInventoryItem { SellerSku = update.Sku, Quantity = update.Quantity });
        }

        using var response = await _httpClient.PostAsJsonAsync("inventory/v4/items/bulk", request, cancellationToken);
        await EnsureSuccessAsync(response, "bulk update inventory", cancellationToken);

        var body = await ReadJsonAsync<BulkUpdateInventoryResponse>(response, cancellationToken);

        var succeeded = new HashSet<string>(StringComparer.Ordinal);
        if (body?.Succeeded is not null)
        {
            foreach (var item in body.Succeeded)
            {
                if (!string.IsNullOrEmpty(item.SellerSku))
                {
                    succeeded.Add(item.SellerSku!);
                }
            }
        }

        var failed = new HashSet<string>(StringComparer.Ordinal);
        if (body?.Errors is not null)
        {
            foreach (var error in body.Errors)
            {
                if (!string.IsNullOrEmpty(error.SellerSku))
                {
                    failed.Add(error.SellerSku!);
                }
            }
        }

        // Report one result per SKU in the batch, keyed by what the request asked for so the caller
        // always gets a complete accounting even if the response omits a SKU.
        var results = new List<WalmartSyncResult>(batch.Count);
        foreach (var update in batch)
        {
            var ok = succeeded.Contains(update.Sku) && !failed.Contains(update.Sku);
            results.Add(new WalmartSyncResult(update.Sku, ok));
        }

        return results;
    }

    public async Task<IReadOnlyList<WalmartInventoryRecord>> ListStockAsync(CancellationToken cancellationToken = default)
    {
        var records = new List<WalmartInventoryRecord>();
        string? nextToken = null;

        do
        {
            var url = $"inventory/v4/items?pageSize={ListPageSize}";
            if (!string.IsNullOrEmpty(nextToken))
            {
                url += $"&nextToken={Uri.EscapeDataString(nextToken)}";
            }

            using var response = await _httpClient.GetAsync(url, cancellationToken);
            await EnsureSuccessAsync(response, "list inventory summaries", cancellationToken);

            var page = await ReadJsonAsync<GetInventorySummariesResponse>(response, cancellationToken);
            if (page?.InventorySummaries is not null)
            {
                foreach (var summary in page.InventorySummaries)
                {
                    if (string.IsNullOrEmpty(summary.SellerSku))
                    {
                        continue;
                    }
                    records.Add(new WalmartInventoryRecord(summary.SellerSku!, summary.InventoryDetails.ResolveQuantity()));
                }
            }

            nextToken = page?.Pagination?.NextToken;
        }
        while (!string.IsNullOrEmpty(nextToken));

        return records;
    }

    private async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            throw new WalmartApiException($"Walmart response could not be parsed: {ex.Message}", response.StatusCode, ex);
        }
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string action, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = await ReadErrorListAsync(response, cancellationToken);
        _logger.LogWarning($"Walmart call failed: {action} -> {(int)response.StatusCode} {response.StatusCode}. {detail}");
        throw new WalmartApiException(
            $"Walmart failed to {action} ({(int)response.StatusCode} {response.StatusCode}): {detail}",
            response.StatusCode);
    }

    private static async Task<string> ReadErrorListAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var errors = await response.Content.ReadFromJsonAsync<WalmartErrorList>(cancellationToken: cancellationToken);
            if (errors?.Errors is { Count: > 0 })
            {
                var parts = new List<string>();
                foreach (var error in errors.Errors)
                {
                    parts.Add(string.IsNullOrEmpty(error.Message) ? (error.Code ?? "error") : error.Message!);
                }
                return string.Join("; ", parts);
            }
        }
        catch
        {
            // Ignore parse failures and return a generic reason below.
        }

        return response.ReasonPhrase ?? "unexpected error";
    }
}
