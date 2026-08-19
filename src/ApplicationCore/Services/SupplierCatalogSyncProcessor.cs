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
/// Runs one queued sync: reads the supplier's listing and matches every product found into the
/// store's own catalog. Matching is keyed by the supplier's own identifier/URL, so re-running a
/// sync updates the same catalog item instead of creating a duplicate.
/// </summary>
public class SupplierCatalogSyncProcessor : ISupplierCatalogSyncProcessor
{
    /// <summary>Catalog type assigned to imported items (the catalog requires every item to have one).</summary>
    private const string ImportedCatalogTypeName = "Imported";

    /// <summary>Brand used when a listing does not name a brand for a product.</summary>
    private const string DefaultBrandName = "Unbranded";

    /// <summary>Catalog names are capped at 50 characters in the schema; imported names are trimmed to fit.</summary>
    private const int MaxNameLength = 50;

    private const string DefaultPictureName = "eCatalog-item-default.png";

    private readonly IRepository<CatalogSync> _syncRepository;
    private readonly IRepository<Supplier> _supplierRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IRepository<CatalogBrand> _catalogBrandRepository;
    private readonly IRepository<CatalogType> _catalogTypeRepository;
    private readonly IRepository<SupplierProductMap> _productMapRepository;
    private readonly IProductListingReader _listingReader;
    private readonly IAppLogger<SupplierCatalogSyncProcessor> _logger;

    // Per-run caches so a brand/type shared by several products is only resolved once.
    private readonly Dictionary<string, CatalogBrand> _brandCache = new(StringComparer.OrdinalIgnoreCase);
    private CatalogType? _importedType;

    public SupplierCatalogSyncProcessor(
        IRepository<CatalogSync> syncRepository,
        IRepository<Supplier> supplierRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IRepository<CatalogBrand> catalogBrandRepository,
        IRepository<CatalogType> catalogTypeRepository,
        IRepository<SupplierProductMap> productMapRepository,
        IProductListingReader listingReader,
        IAppLogger<SupplierCatalogSyncProcessor> logger)
    {
        _syncRepository = syncRepository;
        _supplierRepository = supplierRepository;
        _catalogItemRepository = catalogItemRepository;
        _catalogBrandRepository = catalogBrandRepository;
        _catalogTypeRepository = catalogTypeRepository;
        _productMapRepository = productMapRepository;
        _listingReader = listingReader;
        _logger = logger;
    }

    public async Task ProcessAsync(int syncId, CancellationToken cancellationToken = default)
    {
        var sync = await _syncRepository.GetByIdAsync(syncId, cancellationToken);
        if (sync is null)
        {
            _logger.LogWarning("Catalog sync {0} was queued but no longer exists.", syncId);
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
            _logger.LogInformation("Sync {0}: reading listing {1} for supplier {2}.",
                sync.Id, supplier.ProductListingUrl, supplier.Name);

            var listing = await _listingReader.ReadAsync(supplier.ProductListingUrl, cancellationToken);

            if (!string.IsNullOrWhiteSpace(listing.ExternalJobId))
            {
                sync.RecordExternalJob(listing.ExternalJobId!);
            }

            int found = listing.Products.Count;
            int imported = 0;

            foreach (var product in listing.Products)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (await TryImportProductAsync(supplier, product, cancellationToken))
                {
                    imported++;
                }
            }

            sync.Complete(found, imported);
            await _syncRepository.UpdateAsync(sync, cancellationToken);

            _logger.LogInformation("Sync {0} finished with status {1}: {2} found, {3} imported.",
                sync.Id, sync.Status, found, imported);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Sync {0} failed: {1}", sync.Id, ex.Message);
            sync.Fail(ex.Message);
            await _syncRepository.UpdateAsync(sync, cancellationToken);
        }
    }

    /// <summary>
    /// Imports a single product. Returns true when it was created or updated in the catalog, false
    /// when it was found but could not be imported (for example it has no usable name or price).
    /// A product that is found but not imported is what turns a sync into a partial one.
    /// </summary>
    private async Task<bool> TryImportProductAsync(Supplier supplier, ScrapedProduct product, CancellationToken cancellationToken)
    {
        var name = Truncate(product.Name?.Trim(), MaxNameLength);
        if (string.IsNullOrWhiteSpace(name))
        {
            _logger.LogWarning("Sync for supplier {0}: skipping a product with no name.", supplier.Id);
            return false;
        }

        // The catalog models price as a required, positive amount. A listing entry with no usable
        // price (e.g. "Contact for pricing") is found but cannot be brought into the catalog.
        if (product.Price is not decimal price || price <= 0m)
        {
            _logger.LogWarning("Sync for supplier {0}: skipping '{1}' because it has no usable price.",
                supplier.Id, name);
            return false;
        }

        var externalId = ResolveExternalId(product, name);
        var description = string.IsNullOrWhiteSpace(product.Description) ? name : product.Description.Trim();

        var brand = await GetOrCreateBrandAsync(product.Brand, cancellationToken);
        var type = await GetImportedTypeAsync(cancellationToken);

        var existingMap = await _productMapRepository.FirstOrDefaultAsync(
            new SupplierProductMapSpecification(supplier.Id, externalId), cancellationToken);

        if (existingMap is not null)
        {
            var existingItem = await _catalogItemRepository.GetByIdAsync(existingMap.CatalogItemId, cancellationToken);
            if (existingItem is not null)
            {
                existingItem.UpdateDetails(new CatalogItem.CatalogItemDetails(name, description, price));
                existingItem.UpdateBrand(brand.Id);
                existingItem.UpdateType(type.Id);
                await _catalogItemRepository.UpdateAsync(existingItem, cancellationToken);
                return true;
            }
            // The mapping pointed at an item that is gone; drop it and re-create below.
            await _productMapRepository.DeleteAsync(existingMap, cancellationToken);
        }

        var newItem = new CatalogItem(type.Id, brand.Id, description, name, price, string.Empty);
        newItem.UpdatePictureUri(DefaultPictureName);
        newItem = await _catalogItemRepository.AddAsync(newItem, cancellationToken);

        await _productMapRepository.AddAsync(
            new SupplierProductMap(supplier.Id, externalId, newItem.Id), cancellationToken);

        return true;
    }

    /// <summary>
    /// The supplier's own stable key for the product: its product URL if it has one, otherwise its
    /// SKU, otherwise a name-based fallback so re-syncs still line up.
    /// </summary>
    private static string ResolveExternalId(ScrapedProduct product, string name)
    {
        if (!string.IsNullOrWhiteSpace(product.ProductUrl))
        {
            return product.ProductUrl.Trim();
        }
        if (!string.IsNullOrWhiteSpace(product.Sku))
        {
            return product.Sku.Trim();
        }
        return $"name:{name.ToLowerInvariant()}";
    }

    private async Task<CatalogBrand> GetOrCreateBrandAsync(string? brandName, CancellationToken cancellationToken)
    {
        var name = string.IsNullOrWhiteSpace(brandName) ? DefaultBrandName : brandName.Trim();

        if (_brandCache.TryGetValue(name, out var cached))
        {
            return cached;
        }

        var brand = await _catalogBrandRepository.FirstOrDefaultAsync(
            new CatalogBrandByNameSpecification(name), cancellationToken);

        brand ??= await _catalogBrandRepository.AddAsync(new CatalogBrand(name), cancellationToken);

        _brandCache[name] = brand;
        return brand;
    }

    private async Task<CatalogType> GetImportedTypeAsync(CancellationToken cancellationToken)
    {
        if (_importedType is not null)
        {
            return _importedType;
        }

        var type = await _catalogTypeRepository.FirstOrDefaultAsync(
            new CatalogTypeByNameSpecification(ImportedCatalogTypeName), cancellationToken);

        type ??= await _catalogTypeRepository.AddAsync(new CatalogType(ImportedCatalogTypeName), cancellationToken);

        _importedType = type;
        return type;
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }
        return value.Substring(0, maxLength);
    }
}
