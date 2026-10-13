using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Walmart;
using Microsoft.Extensions.Logging;
using WalmartApIs;
using WalmartApIs.Core.ErrorResponse;
using WalmartApIs.Core.Exceptions;
using WalmartApIs.Errors;
using WalmartApIs.Models;
using WalmartApIs.Requests.Inventory;

namespace Microsoft.eShopWeb.Infrastructure.Walmart;

/// <summary>
/// Talks to Walmart Marketplace via the wm-v4 SDK's Inventory controller. All Inventory operations
/// use WalletAuth (OAuth2 client credentials) on server group Default1; the client is configured
/// once at registration (see <see cref="WalmartServiceCollectionExtensions"/>).
/// </summary>
public class WalmartMarketplaceService : IWalmartMarketplaceService
{
    // Per-attempt timeout is set on the client; these are the whole-call budgets enforced here via a
    // linked CancellationToken (the only thing that bounds a full call, retries included).
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan BatchBudget = TimeSpan.FromMinutes(2);

    // Provider-independent bound on the hand-driven inventory-summaries page walk.
    private const int MaxPages = 1000;
    private const int PageSize = 100;

    private readonly WalmartApIsClient _client;
    private readonly ILogger<WalmartMarketplaceService> _logger;

    public WalmartMarketplaceService(WalmartApIsClient client, ILogger<WalmartMarketplaceService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<WalmartSkuStock> SetStockAsync(string sku, int quantity, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(sku, nameof(sku));
        Guard.Against.Negative(quantity, nameof(quantity));

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);

        var request = new UpdateInventoryForSkuRequest
        {
            Sku = sku,
            Body = new UpdateInventoryRequest { Quantity = quantity }
        };

        try
        {
            await _client.Inventory.UpdateInventoryForSku(request, cancellationToken: cts.Token);
            _logger.LogInformation("Set Walmart stock for SKU {Sku} to {Quantity}.", sku, quantity);
            return new WalmartSkuStock(sku, quantity);
        }
        catch (ApiException<UpdateInventoryForSkuError> ex)
        {
            throw MapApiError(ex.StatusCode, ex.Error.TryGetErrorList(out var list) ? list : null, ex, $"set stock for SKU {sku}");
        }
        catch (ResponseDeserializationException ex)
        {
            throw MapDeserialization(ex, $"set stock for SKU {sku}");
        }
        catch (ApiException ex)
        {
            throw MapApiError(ex.StatusCode, null, ex, $"set stock for SKU {sku}");
        }
        catch (AuthSchemeException ex)
        {
            throw MapAuth(ex);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw BudgetTimeout($"set stock for SKU {sku}");
        }
        catch (SdkConnectionException ex)
        {
            // The PUT may still have landed — re-read to settle the outcome rather than report failure.
            return await SettleUnknownSetAsync(sku, quantity, ex, cancellationToken);
        }
    }

    public async Task<WalmartSkuStock> GetStockAsync(string sku, CancellationToken cancellationToken)
    {
        var quantity = await GetStockQuantityAsync(sku, cancellationToken);
        return new WalmartSkuStock(sku, quantity);
    }

    public async Task<IReadOnlyList<WalmartSyncItemResult>> SyncAsync(
        IReadOnlyList<WalmartSkuStock> desired, CancellationToken cancellationToken)
    {
        Guard.Against.Null(desired, nameof(desired));

        var results = new List<WalmartSyncItemResult>(desired.Count);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(BatchBudget);

        foreach (var item in desired)
        {
            try
            {
                var request = new UpdateInventoryForSkuRequest
                {
                    Sku = item.Sku,
                    Body = new UpdateInventoryRequest { Quantity = item.Quantity }
                };
                await _client.Inventory.UpdateInventoryForSku(request, cancellationToken: cts.Token);
                results.Add(WalmartSyncItemResult.Updated(item.Sku));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw; // the caller aborted — stop the whole sync
            }
            catch (Exception ex)
            {
                // Per the contract, every SKU gets its own outcome; a failure never aborts the batch.
                // UpdateInventoryForSku is an absolute set, so a failed SKU is safely re-pushed next sync.
                _logger.LogWarning(ex, "Sync failed for SKU {Sku}.", item.Sku);
                results.Add(WalmartSyncItemResult.Failed(item.Sku));
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<WalmartSkuStock>> ListAllStockAsync(CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(BatchBudget);

        var all = new List<WalmartSkuStock>();
        string? nextToken = null;
        string? previousToken = null;
        var pages = 0;

        try
        {
            do
            {
                var response = await _client.Inventory.GetInventorySummaries(
                    new GetInventorySummariesRequest { PageSize = PageSize, NextToken = nextToken },
                    cancellationToken: cts.Token);

                if (response.InventorySummaries is not null)
                {
                    foreach (var summary in response.InventorySummaries)
                    {
                        all.Add(new WalmartSkuStock(summary.SellerSku, ReadQuantity(summary)));
                    }
                }

                nextToken = response.Pagination?.NextToken;
                pages++;

                if (!string.IsNullOrEmpty(nextToken))
                {
                    // Bounds that do not depend on the provider cooperating. Truncating the report
                    // silently would be a wrong answer (it must cover every SKU), so we throw.
                    if (pages >= MaxPages)
                    {
                        throw new WalmartIntegrationException(
                            "Walmart returned more inventory pages than the reconciliation report can process.",
                            HttpStatusCode.BadGateway);
                    }
                    if (nextToken == previousToken)
                    {
                        throw new WalmartIntegrationException(
                            "Walmart inventory paging did not advance.", HttpStatusCode.BadGateway);
                    }
                }

                previousToken = nextToken;
            }
            while (!string.IsNullOrEmpty(nextToken));

            return all;
        }
        catch (ApiException<GetInventorySummariesError> ex)
        {
            throw MapApiError(ex.StatusCode, ex.Error.TryGetErrorList(out var list) ? list : null, ex, "list Walmart stock");
        }
        catch (ResponseDeserializationException ex)
        {
            throw MapDeserialization(ex, "list Walmart stock");
        }
        catch (ApiException ex)
        {
            throw MapApiError(ex.StatusCode, null, ex, "list Walmart stock");
        }
        catch (AuthSchemeException ex)
        {
            throw MapAuth(ex);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw BudgetTimeout("list Walmart stock");
        }
        catch (SdkConnectionException ex)
        {
            throw MapTransport(ex, "list Walmart stock");
        }
    }

    private async Task<int> GetStockQuantityAsync(string sku, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(sku, nameof(sku));

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);

        try
        {
            var summary = await _client.Inventory.GetInventoryForSku(
                new GetInventoryForSkuRequest { Sku = sku }, cancellationToken: cts.Token);
            return ReadQuantity(summary);
        }
        catch (ApiException<GetInventoryForSkuError> ex)
        {
            throw MapApiError(ex.StatusCode, ex.Error.TryGetErrorList(out var list) ? list : null, ex, $"read stock for SKU {sku}");
        }
        catch (ResponseDeserializationException ex)
        {
            throw MapDeserialization(ex, $"read stock for SKU {sku}");
        }
        catch (ApiException ex)
        {
            throw MapApiError(ex.StatusCode, null, ex, $"read stock for SKU {sku}");
        }
        catch (AuthSchemeException ex)
        {
            throw MapAuth(ex);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw BudgetTimeout($"read stock for SKU {sku}");
        }
        catch (SdkConnectionException ex)
        {
            throw MapTransport(ex, $"read stock for SKU {sku}");
        }
    }

    private async Task<WalmartSkuStock> SettleUnknownSetAsync(
        string sku, int quantity, Exception cause, CancellationToken cancellationToken)
    {
        _logger.LogWarning(cause,
            "Connection failed setting Walmart stock for SKU {Sku}; re-reading to settle the outcome.", sku);
        try
        {
            var current = await GetStockQuantityAsync(sku, cancellationToken);
            if (current == quantity)
            {
                _logger.LogInformation(
                    "Re-read confirms Walmart stock for SKU {Sku} is {Quantity}; treating the set as applied.", sku, quantity);
                return new WalmartSkuStock(sku, quantity);
            }

            throw new WalmartIntegrationException(
                $"Setting Walmart stock for SKU {sku} could not be confirmed; Walmart now records {current}.",
                HttpStatusCode.ServiceUnavailable, cause);
        }
        catch (WalmartIntegrationException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new WalmartIntegrationException(
                $"Setting Walmart stock for SKU {sku} may not have applied and could not be re-read.",
                HttpStatusCode.ServiceUnavailable, cause);
        }
    }

    private static int ReadQuantity(InventorySummary summary) =>
        summary.InventoryDetails?.OnHandQuantity
        ?? summary.InventoryDetails?.FulfillableQuantity
        ?? 0;

    private WalmartIntegrationException MapApiError(HttpStatusCode status, ErrorList? list, Exception ex, string context)
    {
        var detail = (list is not null && list.Errors.Count > 0)
            ? string.Join("; ", list.Errors.Select(e => $"{e.Code}: {e.Message}"))
            : $"HTTP {(int)status}";
        _logger.LogError(ex, "Walmart API error while trying to {Context}: {Status} {Detail}", context, (int)status, detail);

        var (callerStatus, message) = MapStatus(status, detail);
        return new WalmartIntegrationException(message, callerStatus, ex);
    }

    private static (HttpStatusCode Status, string Message) MapStatus(HttpStatusCode providerStatus, string detail)
    {
        var code = (int)providerStatus;
        // Our credentials / our quota: the caller did nothing wrong and cannot fix it.
        if (code is 401 or 403)
        {
            return (HttpStatusCode.BadGateway, "Walmart Marketplace is unavailable.");
        }
        if (code == 429)
        {
            return (HttpStatusCode.ServiceUnavailable, "Walmart Marketplace is temporarily unavailable.");
        }
        // The provider rejected the caller's request — surface the same status so they can act on it.
        if (code is >= 400 and < 500)
        {
            return (providerStatus, $"Walmart rejected the request: {detail}");
        }
        // 5xx and anything else — no meaningful caller status.
        return (HttpStatusCode.BadGateway, "Walmart Marketplace returned an error.");
    }

    private WalmartIntegrationException MapDeserialization(ResponseDeserializationException ex, string context)
    {
        _logger.LogError(ex, "Walmart returned an unprocessable response while trying to {Context} ({Status}).",
            context, (int)ex.StatusCode);
        return new WalmartIntegrationException(
            "Walmart Marketplace returned a response that could not be processed.", HttpStatusCode.BadGateway, ex);
    }

    private WalmartIntegrationException MapAuth(AuthSchemeException ex)
    {
        _logger.LogError(ex, "Walmart credentials could not be applied.");
        return new WalmartIntegrationException(
            "Walmart Marketplace credentials are not configured correctly.", HttpStatusCode.BadGateway, ex);
    }

    private WalmartIntegrationException MapTransport(Exception ex, string context)
    {
        _logger.LogError(ex, "Walmart Marketplace could not be reached while trying to {Context}.", context);
        return new WalmartIntegrationException(
            "Walmart Marketplace could not be reached.", HttpStatusCode.ServiceUnavailable, ex);
    }

    private WalmartIntegrationException BudgetTimeout(string context)
    {
        _logger.LogWarning("Walmart call budget elapsed while trying to {Context}.", context);
        return new WalmartIntegrationException(
            "Walmart Marketplace did not respond in time.", HttpStatusCode.GatewayTimeout);
    }
}
