using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Square.Models;
using Square.Models.Enums;
using Square.Requests.Catalog;
using EshopCatalogItem = Microsoft.eShopWeb.ApplicationCore.Entities.CatalogItem;
using SquareCatalogItem = Square.Models.CatalogItem;

namespace Microsoft.eShopWeb.Infrastructure.SquareIntegration;

public sealed record CatalogSyncIssue(int CatalogItemId, string Name, string Reason);

public sealed record CatalogSyncResult(
    string MerchantId,
    int Created,
    int Updated,
    int Unchanged,
    IReadOnlyList<CatalogSyncIssue> Skipped,
    IReadOnlyList<CatalogSyncIssue> Failed)
{
    /// <summary>False when some items could not be brought in line (see <see cref="Skipped"/> / <see cref="Failed"/>).</summary>
    public bool Complete => Skipped.Count == 0 && Failed.Count == 0;
}

/// <summary>
/// Brings Square's catalog in line with eShop's: every eShop catalog item exists in Square as an item
/// with the same name and price. Re-running creates no duplicates (items are recognised by link or SKU
/// marker); only items this shop created are ever touched.
/// </summary>
public sealed class SquareCatalogSyncService
{
    private const string LeaseName = "catalog-sync";
    private const int ItemsPerRequest = 400; // 2 objects per new item; well under 1,000/batch and 10,000/request
    private const string VariationName = "Regular";

    private readonly CatalogContext _db;
    private readonly SquareClientHolder _clients;
    private readonly SquareMerchantContextProvider _merchantContext;
    private readonly SquareCatalogLocator _locator;
    private readonly SquareLeaseStore _leases;
    private readonly ILogger<SquareCatalogSyncService> _logger;

    public SquareCatalogSyncService(
        CatalogContext db,
        SquareClientHolder clients,
        SquareMerchantContextProvider merchantContext,
        SquareCatalogLocator locator,
        SquareLeaseStore leases,
        ILogger<SquareCatalogSyncService> logger)
    {
        _db = db;
        _clients = clients;
        _merchantContext = merchantContext;
        _locator = locator;
        _leases = leases;
        _logger = logger;
    }

    public async Task<CatalogSyncResult> SyncAsync(CancellationToken cancellationToken)
    {
        var context = await _merchantContext.GetAsync(cancellationToken).ConfigureAwait(false);

        await using var lease = await _leases.TryAcquireAsync(LeaseName, SquareConstants.CatalogSyncBudget + TimeSpan.FromMinutes(1), cancellationToken).ConfigureAwait(false)
            ?? throw new SquareOperationInProgressException("A catalog sync is already running. Try again when it has finished.");

        var items = await _db.CatalogItems.AsNoTracking().OrderBy(i => i.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        var lookup = await _locator.ResolveAsync(context.MerchantId, items.Select(i => i.Id).ToArray(), cancellationToken).ConfigureAwait(false);

        var unchanged = 0;
        var skipped = new List<CatalogSyncIssue>();
        var work = new List<PlannedUpsert>();
        foreach (var item in items)
        {
            if (lookup.Found.TryGetValue(item.Id, out var entry))
            {
                if (IsInLine(entry, item, context.Currency))
                {
                    unchanged++;
                }
                else
                {
                    work.Add(PlanUpdate(entry, item, context.Currency));
                }
            }
            else if (lookup.Unresolved.Contains(item.Id))
            {
                skipped.Add(new CatalogSyncIssue(item.Id, item.Name,
                    "Could not confirm the item is absent from Square (search result was partial); not created to avoid a duplicate."));
            }
            else
            {
                work.Add(PlanCreate(item, context.Currency));
            }
        }

        var created = 0;
        var updated = 0;
        var failed = new List<CatalogSyncIssue>();
        foreach (var chunk in work.Chunk(ItemsPerRequest))
        {
            var response = await UpsertAsync(chunk, cancellationToken).ConfigureAwait(false);
            var mappings = (response.IdMappings ?? [])
                .Where(m => m.ClientObjectId is not null && m.ObjectId is not null)
                .ToDictionary(m => m.ClientObjectId!, m => m.ObjectId!, StringComparer.Ordinal);
            var returnedIds = new HashSet<string>((response.Objects ?? []).Select(o => o.Id), StringComparer.Ordinal);
            var errorText = string.Join(", ", (response.Errors ?? []).Select(e => e.Code.Value).Distinct());

            foreach (var planned in chunk)
            {
                if (planned.IsCreate)
                {
                    if (mappings.TryGetValue(planned.ItemObjectId, out var itemId) && mappings.TryGetValue(planned.VariationObjectId, out var variationId))
                    {
                        created++;
                        await _locator.RecordLinkAsync(context.MerchantId, planned.CatalogItem.Id, itemId, variationId, null, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        failed.Add(new CatalogSyncIssue(planned.CatalogItem.Id, planned.CatalogItem.Name,
                            $"Square did not create the item{(errorText.Length > 0 ? $" ({errorText})" : string.Empty)}."));
                    }
                }
                else if (returnedIds.Contains(planned.ItemObjectId))
                {
                    updated++;
                }
                else
                {
                    failed.Add(new CatalogSyncIssue(planned.CatalogItem.Id, planned.CatalogItem.Name,
                        $"Square did not apply the update{(errorText.Length > 0 ? $" ({errorText})" : string.Empty)}."));
                }
            }
        }

        _logger.LogInformation(
            "Square catalog sync for merchant {MerchantId}: {Created} created, {Updated} updated, {Unchanged} unchanged, {Skipped} skipped, {Failed} failed",
            context.MerchantId, created, updated, unchanged, skipped.Count, failed.Count);
        return new CatalogSyncResult(context.MerchantId, created, updated, unchanged, skipped, failed);
    }

    /// <summary>
    /// One atomic batch per eShop item, so a rejected item does not block the others. The idempotency key
    /// is reused when the outcome is unknown, so a re-send can never create a second copy.
    /// </summary>
    private async Task<BatchUpsertCatalogObjectsResponse> UpsertAsync(IReadOnlyCollection<PlannedUpsert> chunk, CancellationToken cancellationToken)
    {
        var request = new BatchUpsertCatalogObjectsOperationRequest
        {
            Body = new BatchUpsertCatalogObjectsRequest
            {
                IdempotencyKey = Guid.NewGuid().ToString(),
                Batches = chunk.Select(p => new CatalogObjectBatch { Objects = p.Objects }).ToArray(),
            },
        };

        try
        {
            return await SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (SquareIntegrationException ex) when (ex.MayHaveReachedSquare)
        {
            _logger.LogWarning("Catalog upsert outcome unknown ({Kind}); re-sending with the same idempotency key", ex.Kind);
            try
            {
                return await SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (SquareIntegrationException retry) when (retry.MayHaveReachedSquare)
            {
                throw new SquareIntegrationException(SquareFailureKind.OutcomeUnknown,
                    "Square did not confirm the catalog changes. Run the sync again — items already created are recognised and not duplicated.",
                    retry.ProviderStatus, retry.ErrorCodes, retry);
            }
        }
    }

    private Task<BatchUpsertCatalogObjectsResponse> SendAsync(BatchUpsertCatalogObjectsOperationRequest request, CancellationToken cancellationToken) =>
        SquareCall.RunAsync("Catalog.BatchUpsertCatalogObjects",
            ct => _clients.Client.Catalog.BatchUpsertCatalogObjects(request, cancellationToken: ct), _logger, cancellationToken);

    internal static bool IsInLine(SquareCatalogEntry entry, EshopCatalogItem item, Currency currency) =>
        string.Equals(entry.Item.ItemData?.Name, item.Name, StringComparison.Ordinal)
        && SquareMoney.Matches(entry.Variation.ItemVariationData?.PriceMoney, item.Price, currency);

    private static PlannedUpsert PlanCreate(EshopCatalogItem item, Currency currency)
    {
        var itemId = $"#eshop-item-{item.Id}";
        var variationId = $"#eshop-variation-{item.Id}";
        var variation = new CatalogObject
        {
            Type = CatalogObjectType.ItemVariation,
            Id = variationId,
            PresentAtAllLocations = true,
            ItemVariationData = new CatalogItemVariation
            {
                ItemId = itemId,
                Name = VariationName,
                Sku = SquareConstants.SkuFor(item.Id),
                PricingType = CatalogPricingType.FixedPricing,
                PriceMoney = SquareMoney.From(item.Price, currency),
            },
        };
        var squareItem = new CatalogObject
        {
            Type = CatalogObjectType.Item,
            Id = itemId,
            PresentAtAllLocations = true,
            ItemData = new SquareCatalogItem { Name = item.Name, Variations = [variation] },
        };
        return new PlannedUpsert(item, true, itemId, variationId, [squareItem]);
    }

    /// <summary>
    /// Updates name and price on the objects as Square returned them (`with` keeps every other field,
    /// including ones this SDK does not model, and the version for optimistic concurrency).
    /// </summary>
    private static PlannedUpsert PlanUpdate(SquareCatalogEntry entry, EshopCatalogItem item, Currency currency)
    {
        var updatedVariation = entry.Variation with
        {
            ItemVariationData = (entry.Variation.ItemVariationData ?? new CatalogItemVariation()) with
            {
                PricingType = CatalogPricingType.FixedPricing,
                PriceMoney = SquareMoney.From(item.Price, currency),
            },
        };

        var itemData = entry.Item.ItemData ?? new SquareCatalogItem();
        var nested = itemData.Variations;
        var nestedHasOurs = nested?.Any(v => v.Id == entry.Variation.Id) == true;
        var updatedItem = entry.Item with
        {
            ItemData = itemData with
            {
                Name = item.Name,
                // Keep every variation the item carries (only ours changes) so none is dropped.
                Variations = nestedHasOurs ? nested!.Select(v => v.Id == entry.Variation.Id ? updatedVariation : v).ToArray() : nested,
            },
        };

        IReadOnlyList<CatalogObject> objects = nestedHasOurs ? [updatedItem] : [updatedItem, updatedVariation];
        return new PlannedUpsert(item, false, entry.Item.Id, entry.Variation.Id, objects);
    }

    private sealed record PlannedUpsert(EshopCatalogItem CatalogItem, bool IsCreate, string ItemObjectId, string VariationObjectId, IReadOnlyList<CatalogObject> Objects);
}
