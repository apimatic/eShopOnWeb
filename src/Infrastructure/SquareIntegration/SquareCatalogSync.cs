using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.SquareIntegration.Entities;
using Microsoft.Extensions.Logging;
using Square;
using Square.Core.ErrorResponse;
using Square.Core.Exceptions;
using Square.Models;
using Square.Models.Enums;
using Square.Requests.Catalog;
using EShopCatalogItem = Microsoft.eShopWeb.ApplicationCore.Entities.CatalogItem;

namespace Microsoft.eShopWeb.Infrastructure.SquareIntegration;

public static class SquareSyncOutcome
{
    public const string Created = "created";
    public const string Updated = "updated";
    public const string Unchanged = "unchanged";
    public const string Failed = "failed";
}

public sealed record SquareCatalogSyncItem(int CatalogItemId, string Name, string Outcome, string? SquareItemId, string? Error);

public sealed record SquareCatalogSyncResult(string MerchantId, int Created, int Updated, int Unchanged, int Failed,
    IReadOnlyList<SquareCatalogSyncItem> Items);

/// <summary>
/// Square processes one catalog update per seller at a time and rejects the others with 429, so this process
/// sends its catalog writes one after another.
/// </summary>
public sealed class SquareCatalogWriteGate
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Releaser(_semaphore);
    }

    private sealed class Releaser : IDisposable
    {
        private SemaphoreSlim? _semaphore;
        public Releaser(SemaphoreSlim semaphore) => _semaphore = semaphore;
        public void Dispose() => Interlocked.Exchange(ref _semaphore, null)?.Release();
    }
}

/// <summary>
/// Brings the merchant's Square catalog in line with eShop's: every eShop catalog item exists in Square as an item
/// with the same name and a single fixed-price variation with the same price. Only items this shop created (its
/// <see cref="SquareCatalogLink"/> rows) are ever read or written; other items in the merchant's catalog are left alone.
/// </summary>
public sealed class SquareCatalogSync
{
    private const int BatchRetrieveChunk = 100;
    private const int MaxRateLimitRetries = 3;
    private const string VariationName = "Regular";
    private const int MaxMarkerSearchPages = 5;

    private readonly CatalogContext _db;
    private readonly SquareClientProvider _clients;
    private readonly SquareMerchantContextProvider _merchants;
    private readonly SquareCatalogWriteGate _gate;
    private readonly TimeProvider _clock;
    private readonly ILogger<SquareCatalogSync> _logger;

    public SquareCatalogSync(CatalogContext db, SquareClientProvider clients, SquareMerchantContextProvider merchants,
        SquareCatalogWriteGate gate, TimeProvider clock, ILogger<SquareCatalogSync> logger)
    {
        _db = db;
        _clients = clients;
        _merchants = merchants;
        _gate = gate;
        _clock = clock;
        _logger = logger;
    }

    public async Task<SquareCatalogSyncResult> SyncAllAsync(CancellationToken cancellationToken)
    {
        var merchant = await _merchants.GetAsync(cancellationToken);
        using var _ = await _gate.EnterAsync(cancellationToken);

        var items = await _db.CatalogItems.AsNoTracking().OrderBy(i => i.Id).ToListAsync(cancellationToken);
        var links = await _db.SquareCatalogLinks.Where(l => l.MerchantId == merchant.MerchantId)
            .ToDictionaryAsync(l => l.CatalogItemId, cancellationToken);
        var squareObjects = await RetrieveObjectsAsync(
            links.Values.Select(l => l.SquareItemId).OfType<string>().ToList(), cancellationToken);

        var results = new List<SquareCatalogSyncItem>(items.Count);
        foreach (var item in items)
        {
            links.TryGetValue(item.Id, out var link);
            results.Add(await SyncItemAsync(item, link, squareObjects, merchant, cancellationToken));
        }

        var result = new SquareCatalogSyncResult(merchant.MerchantId,
            results.Count(r => r.Outcome == SquareSyncOutcome.Created),
            results.Count(r => r.Outcome == SquareSyncOutcome.Updated),
            results.Count(r => r.Outcome == SquareSyncOutcome.Unchanged),
            results.Count(r => r.Outcome == SquareSyncOutcome.Failed),
            results);
        _logger.LogInformation("Square catalog sync for merchant {MerchantId}: {Created} created, {Updated} updated, {Unchanged} unchanged, {Failed} failed.",
            merchant.MerchantId, result.Created, result.Updated, result.Unchanged, result.Failed);
        return result;
    }

    /// <summary>
    /// Makes sure one eShop item exists in Square with its current name and price and returns its link.
    /// The caller must hold <see cref="SquareCatalogWriteGate"/>.
    /// </summary>
    public async Task<SquareCatalogLink> EnsureItemSyncedAsync(EShopCatalogItem item, SquareMerchantContext merchant,
        CancellationToken cancellationToken)
    {
        var link = await _db.SquareCatalogLinks.SingleOrDefaultAsync(
            l => l.MerchantId == merchant.MerchantId && l.CatalogItemId == item.Id, cancellationToken);
        var squareObjects = await RetrieveObjectsAsync(
            link?.SquareItemId is { } id ? [id] : [], cancellationToken);

        var outcome = await SyncItemAsync(item, link, squareObjects, merchant, cancellationToken);
        if (outcome.Outcome == SquareSyncOutcome.Failed)
            throw new SquareIntegrationException(SquareFailureKind.Unavailable,
                $"Catalog item {item.Id} could not be synced to Square: {outcome.Error}");

        return await _db.SquareCatalogLinks.SingleAsync(
            l => l.MerchantId == merchant.MerchantId && l.CatalogItemId == item.Id, cancellationToken);
    }

    private async Task<SquareCatalogSyncItem> SyncItemAsync(EShopCatalogItem item, SquareCatalogLink? link,
        IReadOnlyDictionary<string, CatalogObject> squareObjects, SquareMerchantContext merchant, CancellationToken cancellationToken)
    {
        try
        {
            var currency = merchant.Currency;
            var amount = SquareMoney.ToMinorUnits(item.Price, currency.Value);

            if (link is null)
            {
                link = await TryClaimCreateAsync(merchant.MerchantId, item, amount, currency.Value, cancellationToken);
                if (link is null) return InProgress(item);
                return await AdoptOrCreateAsync(link, item, amount, currency, cancellationToken);
            }

            if (link.State == SquareCatalogLinkState.PendingCreate)
            {
                // An earlier create whose outcome is unknown: re-send it with the same key and payload so Square
                // returns the item it may already have created instead of creating a second one.
                if (!HasPendingCreatePayload(link))
                {
                    if (!await TryReclaimCreateAsync(link, item, amount, currency.Value, cancellationToken)) return InProgress(item);
                    return await AdoptOrCreateAsync(link, item, amount, currency, cancellationToken);
                }
                var created = await CreateAsync(link, cancellationToken);
                await ReconcileAsync(link, created, item, amount, currency, cancellationToken);
                return Result(item, SquareSyncOutcome.Created, created.Id);
            }

            if (link.SquareItemId is null || !squareObjects.TryGetValue(link.SquareItemId, out var existing)
                || existing.IsDeleted == true || existing.ItemData is null)
            {
                // The item this shop created is gone from Square (deleted by staff): create it again.
                if (!await TryReclaimCreateAsync(link, item, amount, currency.Value, cancellationToken)) return InProgress(item);
                var created = await CreateAsync(link, cancellationToken);
                return Result(item, SquareSyncOutcome.Created, created.Id);
            }

            var changed = await ReconcileAsync(link, existing, item, amount, currency, cancellationToken);
            return changed is null
                ? InProgress(item)
                : Result(item, changed.Value ? SquareSyncOutcome.Updated : SquareSyncOutcome.Unchanged, link.SquareItemId);
        }
        catch (SquareIntegrationException ex)
        {
            _logger.LogWarning("Square catalog sync of item {CatalogItemId} failed ({Kind}, {Code}).", item.Id, ex.Kind, ex.SquareErrorCode);
            return new SquareCatalogSyncItem(item.Id, item.Name, SquareSyncOutcome.Failed, link?.SquareItemId, ex.Message);
        }
    }

    /// <summary>
    /// Updates the Square item when its name or price differs from eShop. Returns true when it wrote, false when
    /// Square already matched, null when another writer holds the claim.
    /// </summary>
    private async Task<bool?> ReconcileAsync(SquareCatalogLink link, CatalogObject existing, EShopCatalogItem item, long amount,
        Currency currency, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            var variation = FindVariation(existing, link.SquareVariationId);
            if (Matches(existing, variation, item.Name, amount, currency))
            {
                if (link.State != SquareCatalogLinkState.Synced || link.SquareVariationId != variation!.Id)
                {
                    MarkSynced(link, existing.Id, variation!.Id);
                    await _db.SaveChangesAsync(cancellationToken);
                }
                return false;
            }

            if (!await TryClaimUpdateAsync(link, item, amount, currency.Value, cancellationToken)) return null;

            var updated = BuildUpdatedItem(existing, variation, item.Id, item.Name, amount, currency);
            try
            {
                var response = await UpsertAsync(new UpsertCatalogObjectRequest
                {
                    IdempotencyKey = link.PendingIdempotencyKey!,
                    Object = updated,
                }, cancellationToken);

                var saved = response.CatalogObject ?? updated;
                var savedVariation = FindVariation(saved, variation?.Id)
                    ?? throw new SquareIntegrationException(SquareFailureKind.Unavailable, "Square returned the item without its variation.");
                MarkSynced(link, saved.Id, MappedId(response, savedVariation.Id));
                await _db.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (SquareIntegrationException ex) when (ex.SquareErrorCode == "VERSION_MISMATCH" && attempt == 0)
            {
                // Staff changed the same field meanwhile: read the item again and re-apply eShop's values once.
                existing = await RetrieveObjectAsync(existing.Id, cancellationToken)
                    ?? throw new SquareIntegrationException(SquareFailureKind.Conflict, "The Square item disappeared during the update.");
            }
        }
    }

    /// <summary>
    /// Before creating, looks for an item this shop already created for the eShop item (its variation carries the
    /// <see cref="MarkerSku"/>), so a lost or never-recorded link does not turn into a duplicate Square item.
    /// </summary>
    private async Task<SquareCatalogSyncItem> AdoptOrCreateAsync(SquareCatalogLink link, EShopCatalogItem item, long amount,
        Currency currency, CancellationToken cancellationToken)
    {
        var existing = await FindByMarkerAsync(item.Id, cancellationToken);
        if (existing is null)
        {
            var created = await CreateAsync(link, cancellationToken);
            return Result(item, SquareSyncOutcome.Created, created.Id);
        }

        var variation = existing.ItemData?.Variations?.FirstOrDefault(v => v.ItemVariationData?.Sku == MarkerSku(item.Id));
        MarkSynced(link, existing.Id, variation?.Id);
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Re-linked eShop item {CatalogItemId} to existing Square item {SquareItemId}.", item.Id, existing.Id);
        var changed = await ReconcileAsync(link, existing, item, amount, currency, cancellationToken);
        return changed is null
            ? InProgress(item)
            : Result(item, changed.Value ? SquareSyncOutcome.Updated : SquareSyncOutcome.Unchanged, existing.Id);
    }

    private async Task<CatalogObject?> FindByMarkerAsync(int catalogItemId, CancellationToken cancellationToken)
    {
        string? cursor = null;
        for (var page = 0; page < MaxMarkerSearchPages; page++)
        {
            SearchCatalogObjectsResponse response;
            try
            {
                response = await _clients.Merchant.Catalog.SearchCatalogObjects(new SearchCatalogObjectsOperationRequest
                {
                    Body = new SearchCatalogObjectsRequest
                    {
                        Cursor = cursor,
                        ObjectTypes = [CatalogObjectType.ItemVariation],
                        Query = new CatalogQuery
                        {
                            ExactQuery = new CatalogQueryExact { AttributeName = "sku", AttributeValue = MarkerSku(catalogItemId) },
                        },
                        Limit = 100,
                    },
                }, cancellationToken: cancellationToken);
            }
            catch (SdkException ex)
            {
                throw SquareErrors.Translate(ex, "searching the Square catalog");
            }

            foreach (var variation in response.Objects ?? [])
            {
                if (variation.IsDeleted == true || variation.ItemVariationData?.ItemId is not { } itemId) continue;
                var parent = await RetrieveObjectAsync(itemId, cancellationToken);
                if (parent?.ItemData is not null) return parent;
            }

            cursor = response.Cursor;
            if (string.IsNullOrEmpty(cursor)) return null;
        }

        // Never conclude "not found" from a lookup that was cut short: that would create a duplicate.
        throw new SquareIntegrationException(SquareFailureKind.Unavailable,
            $"The Square catalog search for item {catalogItemId} did not finish within {MaxMarkerSearchPages} pages.");
    }

    /// <summary>SKU stamped on the variation of every item this shop creates; identifies the eShop item in Square.</summary>
    public static string MarkerSku(int catalogItemId) => $"eshop-item-{catalogItemId}";

    private async Task<CatalogObject> CreateAsync(SquareCatalogLink link, CancellationToken cancellationToken)
    {
        if (!Currency.TryGetKnownValue(link.PendingCurrency, out var currency))
            throw new SquareIntegrationException(SquareFailureKind.Rejected, $"Unsupported currency {link.PendingCurrency}.");

        UpsertCatalogObjectResponse response;
        try
        {
            response = await UpsertAsync(new UpsertCatalogObjectRequest
            {
                IdempotencyKey = link.PendingIdempotencyKey!,
                Object = NewItem(link.CatalogItemId, link.PendingName!, link.PendingAmount!.Value, currency),
            }, cancellationToken);
        }
        catch (SquareIntegrationException ex) when (ex.Kind is SquareFailureKind.Rejected or SquareFailureKind.RateLimited
                                                        or SquareFailureKind.AuthorizationFailed or SquareFailureKind.NotConnected)
        {
            // Square refused it, so nothing was created: drop the pending payload so the next sync starts over from
            // eShop's current data instead of re-sending the refused request.
            link.PendingIdempotencyKey = null;
            link.PendingName = null;
            link.PendingAmount = null;
            link.PendingCurrency = null;
            link.UpdatedAt = _clock.GetUtcNow();
            link.ConcurrencyStamp = Guid.NewGuid();
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }

        var created = response.CatalogObject
            ?? throw new SquareIntegrationException(SquareFailureKind.OutcomeUnknown, "Square returned no catalog object for the created item.");
        var variationId = MappedId(response, TempVariationId(link.CatalogItemId))
            ?? created.ItemData?.Variations?.FirstOrDefault()?.Id;
        MarkSynced(link, created.Id, variationId);
        await _db.SaveChangesAsync(cancellationToken);
        return created;
    }

    /// <summary>
    /// Sends one catalog upsert. Rate-limited requests are re-sent after a pause; a request whose connection
    /// failed is re-sent once with the same idempotency key, which Square documents as safe ("reattempt it with
    /// the same idempotency key without creating duplicate objects"). Still unsettled, the link keeps its
    /// pending key and the next sync re-sends it.
    /// </summary>
    private async Task<UpsertCatalogObjectResponse> UpsertAsync(UpsertCatalogObjectRequest body, CancellationToken cancellationToken)
    {
        var client = _clients.Merchant;
        var rateLimited = 0;
        var connectionFailures = 0;
        while (true)
        {
            try
            {
                return await client.Catalog.UpsertCatalogObject(
                    new UpsertCatalogObjectOperationRequest { Body = body }, cancellationToken: cancellationToken);
            }
            catch (ApiException<RawError> ex) when (SquareErrors.IsRateLimited(ex) && rateLimited < MaxRateLimitRetries)
            {
                rateLimited++;
                await Task.Delay(TimeSpan.FromMilliseconds(500 * Math.Pow(2, rateLimited)), _clock, cancellationToken);
            }
            catch (SdkConnectionException ex) when (connectionFailures == 0)
            {
                connectionFailures++;
                _logger.LogWarning(ex, "Square catalog upsert outcome unknown; re-sending with the same idempotency key.");
            }
            catch (SdkException ex)
            {
                throw SquareErrors.Translate(ex, "the Square catalog upsert", isWrite: true);
            }
        }
    }

    private async Task<SquareCatalogLink?> TryClaimCreateAsync(string merchantId, EShopCatalogItem item, long amount, string currency,
        CancellationToken cancellationToken)
    {
        var link = new SquareCatalogLink { MerchantId = merchantId, CatalogItemId = item.Id };
        SetPendingCreate(link, item, amount, currency);
        _db.SquareCatalogLinks.Add(link);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return link;
        }
        catch (Exception ex) when (SquareClaims.IsDuplicateKey(ex))
        {
            // Another sync inserted the claim for this (merchant, item) first.
            _db.Entry(link).State = EntityState.Detached;
            return null;
        }
    }

    private async Task<bool> TryReclaimCreateAsync(SquareCatalogLink link, EShopCatalogItem item, long amount, string currency,
        CancellationToken cancellationToken)
    {
        SetPendingCreate(link, item, amount, currency);
        return await TrySaveClaimAsync(link, cancellationToken);
    }

    private async Task<bool> TryClaimUpdateAsync(SquareCatalogLink link, EShopCatalogItem item, long amount, string currency,
        CancellationToken cancellationToken)
    {
        link.State = SquareCatalogLinkState.PendingUpdate;
        link.PendingIdempotencyKey = Guid.NewGuid().ToString();
        link.PendingName = item.Name;
        link.PendingAmount = amount;
        link.PendingCurrency = currency;
        link.PendingSince = _clock.GetUtcNow();
        return await TrySaveClaimAsync(link, cancellationToken);
    }

    private void SetPendingCreate(SquareCatalogLink link, EShopCatalogItem item, long amount, string currency)
    {
        link.State = SquareCatalogLinkState.PendingCreate;
        link.SquareItemId = null;
        link.SquareVariationId = null;
        link.PendingIdempotencyKey = Guid.NewGuid().ToString();
        link.PendingName = item.Name;
        link.PendingAmount = amount;
        link.PendingCurrency = currency;
        link.PendingSince = _clock.GetUtcNow();
        link.PhotoState = null;
        link.PhotoSha256 = null;
        link.PhotoIdempotencyKey = null;
        link.PhotoImageId = null;
        link.PhotoImageUrl = null;
        link.UpdatedAt = _clock.GetUtcNow();
    }

    /// <summary>Saves a claim on an existing link; the concurrency stamp rejects a claim another writer changed first.</summary>
    private async Task<bool> TrySaveClaimAsync(SquareCatalogLink link, CancellationToken cancellationToken)
    {
        link.UpdatedAt = _clock.GetUtcNow();
        link.ConcurrencyStamp = Guid.NewGuid();
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            await _db.Entry(link).ReloadAsync(cancellationToken);
            return false;
        }
    }

    private void MarkSynced(SquareCatalogLink link, string itemId, string? variationId)
    {
        link.SquareItemId = itemId;
        link.SquareVariationId = variationId;
        link.State = SquareCatalogLinkState.Synced;
        link.PendingIdempotencyKey = null;
        link.PendingName = null;
        link.PendingAmount = null;
        link.PendingCurrency = null;
        link.PendingSince = null;
        link.UpdatedAt = _clock.GetUtcNow();
        link.ConcurrencyStamp = Guid.NewGuid();
    }

    private async Task<Dictionary<string, CatalogObject>> RetrieveObjectsAsync(IReadOnlyCollection<string> ids,
        CancellationToken cancellationToken)
    {
        var found = new Dictionary<string, CatalogObject>(StringComparer.Ordinal);
        foreach (var chunk in ids.Distinct().Chunk(BatchRetrieveChunk))
        {
            try
            {
                var response = await _clients.Merchant.Catalog.BatchRetrieveCatalogObjects(new BatchRetrieveCatalogObjectsOperationRequest
                {
                    Body = new BatchRetrieveCatalogObjectsRequest { ObjectIds = chunk },
                }, cancellationToken: cancellationToken);
                foreach (var catalogObject in response.Objects ?? [])
                    found[catalogObject.Id] = catalogObject;
            }
            catch (SdkException ex)
            {
                throw SquareErrors.Translate(ex, "reading the Square catalog");
            }
        }
        return found;
    }

    public async Task<CatalogObject?> RetrieveObjectAsync(string objectId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _clients.Merchant.Catalog.RetrieveCatalogObject(
                new RetrieveCatalogObjectRequest { ObjectId = objectId }, cancellationToken: cancellationToken);
            return response.Object is { IsDeleted: not true } found ? found : null;
        }
        catch (ApiException<RawError> ex) when (SquareErrors.IsNotFound(ex))
        {
            return null;
        }
        catch (SdkException ex)
        {
            throw SquareErrors.Translate(ex, "reading a Square catalog object");
        }
    }

    private static bool HasPendingCreatePayload(SquareCatalogLink link) =>
        link.PendingIdempotencyKey is not null && link.PendingName is not null && link.PendingAmount is not null
        && link.PendingCurrency is not null;

    private static CatalogObject? FindVariation(CatalogObject item, string? variationId)
    {
        var variations = item.ItemData?.Variations;
        if (variations is null || variations.Count == 0) return null;
        return variations.FirstOrDefault(v => v.Id == variationId) ?? variations[0];
    }

    private static bool Matches(CatalogObject item, CatalogObject? variation, string name, long amount, Currency currency)
    {
        var data = variation?.ItemVariationData;
        return item.ItemData?.Name == name
               && data?.PriceMoney?.Amount == amount
               && data.PriceMoney.Currency == currency
               && (data.PricingType is null || data.PricingType == CatalogPricingType.FixedPricing);
    }

    /// <summary>
    /// Upsert is full-replacement: start from the object as Square holds it (version, variations, images, every
    /// field staff set) and change only the name and the price.
    /// </summary>
    private static CatalogObject BuildUpdatedItem(CatalogObject existing, CatalogObject? variation, int catalogItemId, string name,
        long amount, Currency currency)
    {
        var price = new Money { Amount = amount, Currency = currency };
        var variations = existing.ItemData!.Variations?.ToList() ?? [];
        if (variation?.ItemVariationData is { } variationData)
        {
            var index = variations.FindIndex(v => v.Id == variation.Id);
            variations[index] = variation with
            {
                ItemVariationData = variationData with
                {
                    PricingType = CatalogPricingType.FixedPricing,
                    PriceMoney = price,
                    Sku = variationData.Sku ?? MarkerSku(catalogItemId),
                },
            };
        }
        else
        {
            variations.Add(NewVariation($"#eshop-variation-new-{existing.Id}", existing.Id, price, MarkerSku(catalogItemId)));
        }

        return existing with { ItemData = existing.ItemData with { Name = name, Variations = variations } };
    }

    private static CatalogObject NewItem(int catalogItemId, string name, long amount, Currency currency)
    {
        var itemId = $"#eshop-item-{catalogItemId}";
        return new CatalogObject
        {
            Type = CatalogObjectType.Item,
            Id = itemId,
            ItemData = new CatalogItem
            {
                Name = name,
                Variations = [NewVariation(TempVariationId(catalogItemId), itemId, new Money { Amount = amount, Currency = currency },
                    MarkerSku(catalogItemId))],
            },
        };
    }

    private static CatalogObject NewVariation(string id, string itemId, Money price, string sku) => new()
    {
        Type = CatalogObjectType.ItemVariation,
        Id = id,
        ItemVariationData = new CatalogItemVariation
        {
            ItemId = itemId,
            Name = VariationName,
            PricingType = CatalogPricingType.FixedPricing,
            PriceMoney = price,
            Sku = sku,
        },
    };

    private static string TempVariationId(int catalogItemId) => $"#eshop-variation-{catalogItemId}";

    private static string? MappedId(UpsertCatalogObjectResponse response, string? clientId)
    {
        if (clientId is null || !clientId.StartsWith('#')) return clientId;
        return response.IdMappings?.FirstOrDefault(m => m.ClientObjectId == clientId)?.ObjectId;
    }

    private static SquareCatalogSyncItem Result(EShopCatalogItem item, string outcome, string? squareItemId) =>
        new(item.Id, item.Name, outcome, squareItemId, null);

    private static SquareCatalogSyncItem InProgress(EShopCatalogItem item) =>
        new(item.Id, item.Name, SquareSyncOutcome.Failed, null, "Another sync is writing this item; run the sync again.");
}
