using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.SquareIntegration.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Square.Models;
using Square.Models.Enums;
using Square.Requests.Catalog;

namespace Microsoft.eShopWeb.Infrastructure.SquareIntegration;

/// <summary>A Square item (and the variation carrying the price) that represents an eShop catalog item.</summary>
public sealed record SquareCatalogEntry(int CatalogItemId, CatalogObject Item, CatalogObject Variation);

public sealed class SquareCatalogLookup
{
    public SquareCatalogLookup(IReadOnlyDictionary<int, SquareCatalogEntry> found, IReadOnlyCollection<int> unresolved)
    {
        Found = found;
        Unresolved = unresolved;
    }

    /// <summary>eShop items that exist in Square.</summary>
    public IReadOnlyDictionary<int, SquareCatalogEntry> Found { get; }

    /// <summary>
    /// eShop items for which absence from Square could not be confirmed because the search hit its page cap.
    /// Never create these — they may already exist.
    /// </summary>
    public IReadOnlyCollection<int> Unresolved { get; }

    public bool IsComplete => Unresolved.Count == 0;
}

/// <summary>
/// Finds the Square objects this shop created for eShop catalog items. The local link table is the
/// primary index (read back by id, which is consistent); the SKU marker search covers items without a
/// link, e.g. after the database was recreated. Items without the marker are never matched.
/// </summary>
public sealed class SquareCatalogLocator
{
    internal const int MaxSearchPages = 20;
    private const int BatchRetrieveChunk = 100;
    private const int SkuSearchChunk = 100;

    private readonly SquareClientHolder _clients;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _clock;
    private readonly ILogger<SquareCatalogLocator> _logger;

    public SquareCatalogLocator(SquareClientHolder clients, IServiceScopeFactory scopeFactory, TimeProvider clock, ILogger<SquareCatalogLocator> logger)
    {
        _clients = clients;
        _scopeFactory = scopeFactory;
        _clock = clock;
        _logger = logger;
    }

    public async Task<SquareCatalogLookup> ResolveAsync(string merchantId, IReadOnlyCollection<int> catalogItemIds, CancellationToken cancellationToken)
    {
        var ids = catalogItemIds.Distinct().ToArray();
        var found = new Dictionary<int, SquareCatalogEntry>();

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        var links = await db.SquareCatalogLinks
            .Where(l => l.MerchantId == merchantId && ids.Contains(l.CatalogItemId))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        if (links.Count > 0)
        {
            var objects = await RetrieveAsync(
                links.SelectMany(l => new[] { l.SquareItemId, l.SquareVariationId }).ToArray(), cancellationToken).ConfigureAwait(false);
            foreach (var link in links)
            {
                var entry = ToEntry(link.CatalogItemId, objects, link.SquareItemId, link.SquareVariationId);
                if (entry is not null)
                {
                    found[link.CatalogItemId] = entry;
                }
                else
                {
                    _logger.LogInformation("Square objects for catalog item {CatalogItemId} no longer exist; forgetting the link", link.CatalogItemId);
                    db.SquareCatalogLinks.Remove(link);
                }
            }
        }

        var missing = ids.Where(id => !found.ContainsKey(id)).ToArray();
        var unresolved = new List<int>();
        if (missing.Length > 0)
        {
            var (bySku, complete) = await SearchBySkuAsync(missing, cancellationToken).ConfigureAwait(false);
            foreach (var id in missing)
            {
                if (bySku.TryGetValue(id, out var entry))
                {
                    found[id] = entry;
                    await UpsertLinkAsync(db, merchantId, entry, cancellationToken).ConfigureAwait(false);
                }
                else if (!complete)
                {
                    unresolved.Add(id);
                }
            }
        }

        await SaveLinksAsync(db, cancellationToken).ConfigureAwait(false);
        return new SquareCatalogLookup(found, unresolved);
    }

    public async Task RecordLinkAsync(string merchantId, int catalogItemId, string itemId, string variationId, string? imageId, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        var link = await db.SquareCatalogLinks.FindAsync([merchantId, catalogItemId], cancellationToken).ConfigureAwait(false);
        if (link is null)
        {
            link = new SquareCatalogLink { MerchantId = merchantId, CatalogItemId = catalogItemId };
            db.SquareCatalogLinks.Add(link);
        }

        link.SquareItemId = itemId;
        link.SquareVariationId = variationId;
        link.SquareImageId = imageId ?? link.SquareImageId;
        link.UpdatedAt = _clock.GetUtcNow();
        await SaveLinksAsync(db, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads catalog objects by id (consistent read). Deleted objects are not returned.</summary>
    public async Task<Dictionary<string, CatalogObject>> RetrieveAsync(IReadOnlyCollection<string> objectIds, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, CatalogObject>(StringComparer.Ordinal);
        foreach (var chunk in objectIds.Distinct().Chunk(BatchRetrieveChunk))
        {
            var response = await SquareCall.RunAsync("Catalog.BatchRetrieveCatalogObjects", ct => _clients.Client.Catalog.BatchRetrieveCatalogObjects(
                new BatchRetrieveCatalogObjectsOperationRequest
                {
                    Body = new BatchRetrieveCatalogObjectsRequest { ObjectIds = chunk, IncludeRelatedObjects = false },
                },
                cancellationToken: ct), _logger, cancellationToken).ConfigureAwait(false);

            foreach (var obj in response.Objects ?? [])
            {
                if (obj.IsDeleted != true)
                {
                    result[obj.Id] = obj;
                }

                // Variations nested in an item are usable too (they carry their own id and version).
                foreach (var nested in obj.ItemData?.Variations ?? [])
                {
                    if (nested.IsDeleted != true && !result.ContainsKey(nested.Id))
                    {
                        result[nested.Id] = nested;
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Searches Square for variations carrying this shop's SKU marker. Bounded: at most
    /// <see cref="MaxSearchPages"/> pages per chunk; when the cap is hit, <c>complete</c> is false.
    /// </summary>
    private async Task<(Dictionary<int, SquareCatalogEntry> Found, bool Complete)> SearchBySkuAsync(
        IReadOnlyCollection<int> catalogItemIds, CancellationToken cancellationToken)
    {
        var skuToId = catalogItemIds.ToDictionary(SquareConstants.SkuFor, id => id, StringComparer.Ordinal);
        var variations = new Dictionary<int, CatalogObject>();
        var complete = true;

        foreach (var chunk in skuToId.Keys.Chunk(SkuSearchChunk))
        {
            string? cursor = null;
            var pages = 0;
            do
            {
                var pageCursor = cursor;
                var response = await SquareCall.RunAsync("Catalog.SearchCatalogObjects", ct => _clients.Client.Catalog.SearchCatalogObjects(
                    new SearchCatalogObjectsOperationRequest
                    {
                        Body = new SearchCatalogObjectsRequest
                        {
                            ObjectTypes = [CatalogObjectType.ItemVariation],
                            Query = new CatalogQuery
                            {
                                SetQuery = new CatalogQuerySet { AttributeName = "sku", AttributeValues = chunk },
                            },
                            IncludeDeletedObjects = false,
                            Cursor = pageCursor,
                            Limit = 1000,
                        },
                    },
                    cancellationToken: ct), _logger, cancellationToken).ConfigureAwait(false);

                foreach (var variation in response.Objects ?? [])
                {
                    var sku = variation.ItemVariationData?.Sku;
                    if (variation.IsDeleted != true && sku is not null && skuToId.TryGetValue(sku, out var catalogItemId))
                    {
                        // Prefer the oldest-created match if several exist (stable choice, never a new one).
                        variations.TryAdd(catalogItemId, variation);
                    }
                }

                var next = string.IsNullOrEmpty(response.Cursor) ? null : response.Cursor;
                if (next is not null && next == cursor)
                {
                    _logger.LogWarning("Square catalog search returned the same cursor twice; stopping");
                    complete = false;
                    break;
                }

                cursor = next;
                if (cursor is not null && ++pages >= MaxSearchPages)
                {
                    _logger.LogWarning("Square catalog search hit its page cap ({Pages} pages); result is partial", MaxSearchPages);
                    complete = false;
                    break;
                }
            }
            while (cursor is not null);
        }

        var found = new Dictionary<int, SquareCatalogEntry>();
        if (variations.Count == 0)
        {
            return (found, complete);
        }

        var itemIds = variations.Values.Select(v => v.ItemVariationData?.ItemId).OfType<string>().ToArray();
        var objects = await RetrieveAsync(itemIds, cancellationToken).ConfigureAwait(false);
        foreach (var (catalogItemId, variation) in variations)
        {
            var itemId = variation.ItemVariationData?.ItemId;
            if (itemId is not null && objects.TryGetValue(itemId, out var item) && item.Type == CatalogObjectType.Item)
            {
                found[catalogItemId] = new SquareCatalogEntry(catalogItemId, item, PreferNested(item, variation));
            }
        }

        return (found, complete);
    }

    private static SquareCatalogEntry? ToEntry(int catalogItemId, IReadOnlyDictionary<string, CatalogObject> objects, string itemId, string variationId)
    {
        if (!objects.TryGetValue(itemId, out var item) || item.Type != CatalogObjectType.Item)
        {
            return null;
        }

        if (!objects.TryGetValue(variationId, out var variation) || variation.Type != CatalogObjectType.ItemVariation)
        {
            return null;
        }

        return new SquareCatalogEntry(catalogItemId, item, PreferNested(item, variation));
    }

    private static CatalogObject PreferNested(CatalogObject item, CatalogObject variation) =>
        item.ItemData?.Variations?.FirstOrDefault(v => v.Id == variation.Id) ?? variation;

    private async Task UpsertLinkAsync(CatalogContext db, string merchantId, SquareCatalogEntry entry, CancellationToken cancellationToken)
    {
        var link = await db.SquareCatalogLinks.FindAsync([merchantId, entry.CatalogItemId], cancellationToken).ConfigureAwait(false);
        if (link is null)
        {
            link = new SquareCatalogLink { MerchantId = merchantId, CatalogItemId = entry.CatalogItemId };
            db.SquareCatalogLinks.Add(link);
        }

        link.SquareItemId = entry.Item.Id;
        link.SquareVariationId = entry.Variation.Id;
        link.UpdatedAt = _clock.GetUtcNow();
    }

    private async Task SaveLinksAsync(CatalogContext db, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (SquareLeaseStore.IsDuplicateKey(ex) || ex is DbUpdateConcurrencyException)
        {
            // A concurrent request recorded the same link; the links are a cache of what Square holds.
            _logger.LogDebug(ex, "Concurrent Square catalog link update ignored");
        }
    }
}
