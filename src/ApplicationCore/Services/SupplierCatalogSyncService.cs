using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SupplierCatalogSyncService : ISupplierCatalogSyncService
{
    /// <summary>Catalog type assigned to imported products (they carry a brand but no store type).</summary>
    public const string ImportedCatalogTypeName = "Imported";

    private const string UnknownBrandName = "Unknown";
    private const string DefaultPicture = "eCatalog-item-default.png";
    private const int NameMaxLength = 50; // matches CatalogItemConfiguration

    private readonly IRepository<CatalogSync> _syncRepository;
    private readonly IRepository<Supplier> _supplierRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IRepository<CatalogBrand> _brandRepository;
    private readonly IRepository<CatalogType> _typeRepository;
    private readonly IRepository<SupplierCatalogItem> _linkRepository;
    private readonly ISupplierListingReader _listingReader;
    private readonly IAppLogger<SupplierCatalogSyncService> _logger;

    public SupplierCatalogSyncService(
        IRepository<CatalogSync> syncRepository,
        IRepository<Supplier> supplierRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IRepository<CatalogBrand> brandRepository,
        IRepository<CatalogType> typeRepository,
        IRepository<SupplierCatalogItem> linkRepository,
        ISupplierListingReader listingReader,
        IAppLogger<SupplierCatalogSyncService> logger)
    {
        _syncRepository = syncRepository;
        _supplierRepository = supplierRepository;
        _catalogItemRepository = catalogItemRepository;
        _brandRepository = brandRepository;
        _typeRepository = typeRepository;
        _linkRepository = linkRepository;
        _listingReader = listingReader;
        _logger = logger;
    }

    public async Task RunSyncAsync(int syncId, CancellationToken cancellationToken = default)
    {
        var sync = await _syncRepository.GetByIdAsync(syncId, cancellationToken);
        if (sync is null)
        {
            _logger.LogWarning($"Sync {syncId} not found; skipping.");
            return;
        }

        var supplier = await _supplierRepository.GetByIdAsync(sync.SupplierId, cancellationToken);
        if (supplier is null)
        {
            sync.Fail($"Supplier {sync.SupplierId} no longer exists.");
            await _syncRepository.UpdateAsync(sync, cancellationToken);
            return;
        }

        sync.MarkRunning();
        await _syncRepository.UpdateAsync(sync, cancellationToken);

        try
        {
            var listing = await _listingReader.ReadListingAsync(supplier.ListingUrl, cancellationToken);

            int typeId = (await GetOrCreateTypeAsync(ImportedCatalogTypeName, cancellationToken)).Id;
            var brandCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            int found = listing.Products.Count;
            int imported = 0;
            int skipped = 0;

            foreach (var product in listing.Products)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!IsImportable(product, out var reason))
                {
                    skipped++;
                    _logger.LogWarning($"Sync {syncId}: skipping product '{product.Name}' ({product.ExternalId}) - {reason}.");
                    continue;
                }

                int brandId = await ResolveBrandIdAsync(product.Brand, brandCache, cancellationToken);
                await UpsertCatalogItemAsync(supplier.Id, product, brandId, typeId, cancellationToken);
                imported++;
            }

            var (status, detail) = DetermineOutcome(listing, found, imported, skipped);
            sync.Complete(status, found, imported, detail);
            await _syncRepository.UpdateAsync(sync, cancellationToken);

            _logger.LogInformation(
                $"Sync {syncId} for supplier {supplier.Id} finished: {status}. Found {found}, imported {imported}.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Sync {syncId} for supplier {supplier.Id} failed: {ex.Message}");
            // Reload to avoid persisting a partially mutated tracked instance.
            var current = await _syncRepository.GetByIdAsync(syncId, cancellationToken);
            if (current is not null)
            {
                current.Fail($"Sync failed: {ex.Message}");
                await _syncRepository.UpdateAsync(current, cancellationToken);
            }
        }
    }

    private static bool IsImportable(SupplierProduct product, out string reason)
    {
        if (string.IsNullOrWhiteSpace(product.Name))
        {
            reason = "no product name";
            return false;
        }
        if (string.IsNullOrWhiteSpace(product.ExternalId))
        {
            reason = "no supplier identifier or URL to match on";
            return false;
        }
        if (product.Price is null || product.Price <= 0m)
        {
            reason = "no purchasable price";
            return false;
        }
        reason = string.Empty;
        return true;
    }

    private static (CatalogSyncStatus Status, string? Detail) DetermineOutcome(
        SupplierListingResult listing, int found, int imported, int skipped)
    {
        if (found == 0)
        {
            // Nothing on the listing. If we could not even read it fully, that is a partial read;
            // otherwise the listing is genuinely empty and the (empty) catalog is fully in sync.
            return listing.FullyCaptured
                ? (CatalogSyncStatus.Completed, listing.Detail ?? "Listing contained no products.")
                : (CatalogSyncStatus.PartiallyCompleted, listing.Detail ?? "Listing could not be read in full.");
        }

        if (listing.FullyCaptured && imported == found)
        {
            return (CatalogSyncStatus.Completed, listing.Detail);
        }

        var reasons = new List<string>();
        if (!listing.FullyCaptured)
        {
            reasons.Add(listing.Detail ?? "the listing could not be read in full");
        }
        if (skipped > 0)
        {
            reasons.Add($"{skipped} product(s) could not be imported (e.g. missing price or identifier)");
        }
        var detail = reasons.Count > 0
            ? $"Imported {imported} of {found} products; " + string.Join("; ", reasons) + "."
            : $"Imported {imported} of {found} products.";
        return (CatalogSyncStatus.PartiallyCompleted, detail);
    }

    private async Task UpsertCatalogItemAsync(
        int supplierId, SupplierProduct product, int brandId, int typeId, CancellationToken cancellationToken)
    {
        var name = Trim(product.Name!, NameMaxLength);
        var description = string.IsNullOrWhiteSpace(product.Description) ? name : product.Description!;
        var price = product.Price!.Value;
        var externalId = product.ExternalId!;

        var link = await _linkRepository.FirstOrDefaultAsync(
            new SupplierCatalogItemSpecification(supplierId, externalId), cancellationToken);

        if (link is not null)
        {
            var existing = await _catalogItemRepository.GetByIdAsync(link.CatalogItemId, cancellationToken);
            if (existing is not null)
            {
                existing.UpdateDetails(new CatalogItem.CatalogItemDetails(name, description, price));
                existing.UpdateBrand(brandId);
                existing.UpdateType(typeId);
                await _catalogItemRepository.UpdateAsync(existing, cancellationToken);

                link.MarkSynced();
                await _linkRepository.UpdateAsync(link, cancellationToken);
                return;
            }

            // Link dangles (catalog item was removed) - drop it and re-create below.
            await _linkRepository.DeleteAsync(link, cancellationToken);
        }

        var newItem = new CatalogItem(typeId, brandId, description, name, price, DefaultPicture);
        newItem = await _catalogItemRepository.AddAsync(newItem, cancellationToken);
        newItem.UpdatePictureUri(DefaultPicture);
        await _catalogItemRepository.UpdateAsync(newItem, cancellationToken);

        await _linkRepository.AddAsync(
            new SupplierCatalogItem(supplierId, externalId, newItem.Id), cancellationToken);
    }

    private async Task<int> ResolveBrandIdAsync(
        string? brandName, IDictionary<string, int> cache, CancellationToken cancellationToken)
    {
        var name = string.IsNullOrWhiteSpace(brandName) ? UnknownBrandName : brandName.Trim();
        name = Trim(name, 100); // matches CatalogBrandConfiguration

        if (cache.TryGetValue(name, out var cachedId))
        {
            return cachedId;
        }

        var existing = await _brandRepository.FirstOrDefaultAsync(
            new CatalogBrandByNameSpecification(name), cancellationToken);
        var brand = existing ?? await _brandRepository.AddAsync(new CatalogBrand(name), cancellationToken);

        cache[name] = brand.Id;
        return brand.Id;
    }

    private async Task<CatalogType> GetOrCreateTypeAsync(string typeName, CancellationToken cancellationToken)
    {
        var existing = await _typeRepository.FirstOrDefaultAsync(
            new CatalogTypeByNameSpecification(typeName), cancellationToken);
        return existing ?? await _typeRepository.AddAsync(new CatalogType(typeName), cancellationToken);
    }

    private static string Trim(string value, int maxLength) =>
        value.Length <= maxLength ? value : value.Substring(0, maxLength);
}
