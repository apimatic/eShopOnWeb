using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Reads a supplier's listing and matches every product found into the store's own catalog.
/// Matching is keyed by the supplier's own identifier/URL for each product, so re-running a sync
/// updates the same catalog item instead of creating a duplicate.
/// </summary>
public class SupplierCatalogSyncService : ISupplierCatalogSyncService
{
    // Catalog items imported from a listing that gives no category are grouped under this type,
    // and those that give no brand under this brand. Both are created on demand if absent.
    private const string DefaultCatalogTypeName = "Imported";
    private const string DefaultBrandName = "Other";
    private const int CatalogNameMaxLength = 50; // matches CatalogItem.Name column constraint

    private readonly IRepository<Supplier> _supplierRepository;
    private readonly IRepository<SupplierSync> _syncRepository;
    private readonly IRepository<SupplierCatalogItem> _linkRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IRepository<CatalogBrand> _catalogBrandRepository;
    private readonly IRepository<CatalogType> _catalogTypeRepository;
    private readonly ISupplierListingReader _listingReader;
    private readonly IAppLogger<SupplierCatalogSyncService> _logger;

    public SupplierCatalogSyncService(
        IRepository<Supplier> supplierRepository,
        IRepository<SupplierSync> syncRepository,
        IRepository<SupplierCatalogItem> linkRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IRepository<CatalogBrand> catalogBrandRepository,
        IRepository<CatalogType> catalogTypeRepository,
        ISupplierListingReader listingReader,
        IAppLogger<SupplierCatalogSyncService> logger)
    {
        _supplierRepository = supplierRepository;
        _syncRepository = syncRepository;
        _linkRepository = linkRepository;
        _catalogItemRepository = catalogItemRepository;
        _catalogBrandRepository = catalogBrandRepository;
        _catalogTypeRepository = catalogTypeRepository;
        _listingReader = listingReader;
        _logger = logger;
    }

    public async Task RunSyncAsync(int syncId, CancellationToken cancellationToken = default)
    {
        var sync = await _syncRepository.GetByIdAsync(syncId, cancellationToken);
        if (sync is null)
        {
            _logger.LogWarning("Supplier sync {0} was requested but does not exist.", syncId);
            return;
        }

        var supplier = await _supplierRepository.GetByIdAsync(sync.SupplierId, cancellationToken);
        if (supplier is null)
        {
            sync.MarkFailed($"Supplier {sync.SupplierId} no longer exists.");
            await _syncRepository.UpdateAsync(sync, cancellationToken);
            return;
        }

        sync.MarkRunning();
        await _syncRepository.UpdateAsync(sync, cancellationToken);
        _logger.LogInformation("Supplier sync {0} started for supplier '{1}' ({2}).",
            syncId, supplier.Name, supplier.ProductListingUrl);

        SupplierListingResult listing;
        try
        {
            listing = await _listingReader.ReadListingAsync(supplier.ProductListingUrl, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Supplier sync {0} failed while reading the listing: {1}", syncId, ex.Message);
            sync.MarkFailed($"Failed to read the supplier listing: {ex.Message}");
            await _syncRepository.UpdateAsync(sync, cancellationToken);
            return;
        }

        // Per-run caches so a brand/type is looked up or created at most once.
        var brandIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var typeIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        int found = listing.Products.Count;
        int imported = 0;

        foreach (var product in listing.Products)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (await ImportProductAsync(supplier.Id, product, brandIds, typeIds, cancellationToken))
                {
                    imported++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Supplier sync {0}: failed to import product '{1}': {2}",
                    syncId, product.Name ?? "(no name)", ex.Message);
            }
        }

        sync.MarkCompleted(found, imported, listing.ListingFullyCaptured);
        await _syncRepository.UpdateAsync(sync, cancellationToken);
        _logger.LogInformation("Supplier sync {0} finished with status {1}: {2} found, {3} imported.",
            syncId, sync.Status, found, imported);
    }

    /// <summary>
    /// Imports a single scraped product into the catalog, creating a new catalog item or updating
    /// the one previously imported for this supplier product. Returns false when the product does
    /// not carry the minimum data required to be a catalog item (name and a positive price).
    /// </summary>
    private async Task<bool> ImportProductAsync(
        int supplierId,
        ScrapedProduct product,
        Dictionary<string, int> brandIds,
        Dictionary<string, int> typeIds,
        CancellationToken cancellationToken)
    {
        var name = product.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        // A stable key for this supplier's product. The product's own page URL is preferred because
        // it is the canonical, reliably-present identifier on a listing page; the supplier's SKU is
        // LLM-inferred and not always returned, so relying on it first would let a re-sync key the
        // same product differently and create a duplicate. Fall back to SKU, then name.
        var externalId = BuildExternalKey(product, name);

        var price = product.Price ?? 0m;
        if (price <= 0m)
        {
            _logger.LogWarning("Skipping supplier product '{0}' because it has no positive price.", name);
            return false;
        }

        var description = product.Description?.Trim();
        if (string.IsNullOrWhiteSpace(description))
        {
            description = name;
        }

        var storedName = name.Length > CatalogNameMaxLength ? name.Substring(0, CatalogNameMaxLength) : name;

        var brandName = string.IsNullOrWhiteSpace(product.Brand) ? DefaultBrandName : product.Brand.Trim();
        var typeName = string.IsNullOrWhiteSpace(product.Category) ? DefaultCatalogTypeName : product.Category.Trim();

        int brandId = await GetOrCreateBrandAsync(brandName, brandIds, cancellationToken);
        int typeId = await GetOrCreateTypeAsync(typeName, typeIds, cancellationToken);

        var existingLink = await _linkRepository.FirstOrDefaultAsync(
            new SupplierCatalogItemByKeySpecification(supplierId, externalId), cancellationToken);

        if (existingLink is not null)
        {
            var existingItem = await _catalogItemRepository.GetByIdAsync(existingLink.CatalogItemId, cancellationToken);
            if (existingItem is not null)
            {
                existingItem.UpdateDetails(new CatalogItem.CatalogItemDetails(storedName, description, price));
                existingItem.UpdateBrand(brandId);
                existingItem.UpdateType(typeId);
                await _catalogItemRepository.UpdateAsync(existingItem, cancellationToken);
                return true;
            }

            // The link points at a catalog item that no longer exists; rebuild it below.
            await _linkRepository.DeleteAsync(existingLink, cancellationToken);
        }

        var newItem = new CatalogItem(typeId, brandId, description, storedName, price, string.Empty);
        newItem = await _catalogItemRepository.AddAsync(newItem, cancellationToken);
        newItem.UpdatePictureUri("eCatalog-item-default.png");
        await _catalogItemRepository.UpdateAsync(newItem, cancellationToken);

        await _linkRepository.AddAsync(new SupplierCatalogItem(supplierId, externalId, newItem.Id), cancellationToken);
        return true;
    }

    private async Task<int> GetOrCreateBrandAsync(string brandName, Dictionary<string, int> cache, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(brandName, out var cachedId))
        {
            return cachedId;
        }

        var existing = await _catalogBrandRepository.FirstOrDefaultAsync(
            new CatalogBrandByNameSpecification(brandName), cancellationToken);
        var brand = existing ?? await _catalogBrandRepository.AddAsync(new CatalogBrand(brandName), cancellationToken);

        cache[brandName] = brand.Id;
        return brand.Id;
    }

    private async Task<int> GetOrCreateTypeAsync(string typeName, Dictionary<string, int> cache, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(typeName, out var cachedId))
        {
            return cachedId;
        }

        var existing = await _catalogTypeRepository.FirstOrDefaultAsync(
            new CatalogTypeByNameSpecification(typeName), cancellationToken);
        var type = existing ?? await _catalogTypeRepository.AddAsync(new CatalogType(typeName), cancellationToken);

        cache[typeName] = type.Id;
        return type.Id;
    }

    /// <summary>
    /// Builds the stable key used to match this supplier product to a catalog item across syncs.
    /// Prefers the product's own page URL (canonical and reliably present), then the supplier SKU,
    /// then the product name as a last resort.
    /// </summary>
    private static string BuildExternalKey(ScrapedProduct product, string name)
    {
        if (!string.IsNullOrWhiteSpace(product.Url))
        {
            return NormalizeUrl(product.Url);
        }

        if (!string.IsNullOrWhiteSpace(product.Sku))
        {
            return product.Sku.Trim();
        }

        return name.Trim().ToLowerInvariant();
    }

    private static string NormalizeUrl(string url)
    {
        var trimmed = url.Trim();
        return trimmed.EndsWith("/") ? trimmed.TrimEnd('/') : trimmed;
    }
}
