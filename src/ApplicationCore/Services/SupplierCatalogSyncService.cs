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
/// Reads a supplier's listing via <see cref="ISupplierCatalogScraper"/> and matches every
/// product into the store's own catalog. Matching is keyed by the supplier's own identifier
/// (or URL) for the product, so re-running a sync updates the same catalog item rather than
/// creating a duplicate.
/// </summary>
public class SupplierCatalogSyncService : ISupplierCatalogSyncService
{
    /// <summary>Catalog items require a type; supplier listings carry no type, so imports land here.</summary>
    private const string DefaultCatalogTypeName = "Supplier Import";

    /// <summary>The catalog's Name column is capped at 50 characters.</summary>
    private const int CatalogItemNameMaxLength = 50;

    private const string DefaultPictureName = "eCatalog-item-default.png";

    private readonly IRepository<CatalogSync> _syncRepository;
    private readonly IRepository<Supplier> _supplierRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IRepository<CatalogBrand> _catalogBrandRepository;
    private readonly IRepository<CatalogType> _catalogTypeRepository;
    private readonly IRepository<SupplierProductMap> _productMapRepository;
    private readonly ISupplierCatalogScraper _scraper;
    private readonly IAppLogger<SupplierCatalogSyncService> _logger;

    public SupplierCatalogSyncService(
        IRepository<CatalogSync> syncRepository,
        IRepository<Supplier> supplierRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IRepository<CatalogBrand> catalogBrandRepository,
        IRepository<CatalogType> catalogTypeRepository,
        IRepository<SupplierProductMap> productMapRepository,
        ISupplierCatalogScraper scraper,
        IAppLogger<SupplierCatalogSyncService> logger)
    {
        _syncRepository = syncRepository;
        _supplierRepository = supplierRepository;
        _catalogItemRepository = catalogItemRepository;
        _catalogBrandRepository = catalogBrandRepository;
        _catalogTypeRepository = catalogTypeRepository;
        _productMapRepository = productMapRepository;
        _scraper = scraper;
        _logger = logger;
    }

    public async Task RunSyncAsync(int syncId, CancellationToken cancellationToken = default)
    {
        var sync = await _syncRepository.GetByIdAsync(syncId, cancellationToken);
        if (sync is null)
        {
            _logger.LogWarning("Catalog sync {0} not found; nothing to run.", syncId);
            return;
        }

        try
        {
            var supplier = await _supplierRepository.GetByIdAsync(sync.SupplierId, cancellationToken);
            if (supplier is null)
            {
                throw new InvalidOperationException($"Supplier {sync.SupplierId} not found.");
            }

            _logger.LogInformation(
                "Catalog sync {0} started for supplier {1} ({2}).", sync.Id, supplier.Name, supplier.ProductListingUrl);

            var scrapeResult = await _scraper.ScrapeListingAsync(supplier.ProductListingUrl, cancellationToken);

            int itemsFound = scrapeResult.Products.Count;
            int itemsImported = 0;

            var brandIdCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int defaultTypeId = await GetOrCreateCatalogTypeAsync(DefaultCatalogTypeName, cancellationToken);

            foreach (var product in scrapeResult.Products)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (await TryImportProductAsync(supplier.Id, product, defaultTypeId, brandIdCache, cancellationToken))
                {
                    itemsImported++;
                }
            }

            sync.MarkFinished(itemsFound, itemsImported, scrapeResult.ListingFullyCaptured);
            await _syncRepository.UpdateAsync(sync, cancellationToken);

            _logger.LogInformation(
                "Catalog sync {0} finished with status {1}: found {2}, imported {3}.",
                sync.Id, sync.Status, itemsFound, itemsImported);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Catalog sync {0} failed: {1}", syncId, ex.Message);
            sync.MarkFailed(ex.Message);
            await _syncRepository.UpdateAsync(sync, CancellationToken.None);
        }
    }

    /// <summary>
    /// Imports one scraped product. Returns <c>true</c> when the product was brought into the
    /// catalog (created or updated), <c>false</c> when it was found but could not be imported
    /// (missing a name, description, brand, a usable price, or a match key).
    /// </summary>
    private async Task<bool> TryImportProductAsync(
        int supplierId,
        ScrapedProduct product,
        int defaultTypeId,
        Dictionary<string, int> brandIdCache,
        CancellationToken cancellationToken)
    {
        var matchKey = ResolveMatchKey(product);
        var name = product.Name?.Trim();
        var description = product.Description?.Trim();
        var brand = product.Brand?.Trim();

        if (string.IsNullOrEmpty(matchKey) ||
            string.IsNullOrEmpty(name) ||
            string.IsNullOrEmpty(description) ||
            string.IsNullOrEmpty(brand) ||
            !product.Price.HasValue ||
            product.Price.Value <= 0m)
        {
            _logger.LogWarning(
                "Skipping supplier {0} product '{1}' (key '{2}'): incomplete data (price, name, description or brand missing).",
                supplierId, name ?? "<no name>", matchKey ?? "<no key>");
            return false;
        }

        name = Truncate(name, CatalogItemNameMaxLength);
        int brandId = await GetOrCreateCatalogBrandAsync(brand, brandIdCache, cancellationToken);
        decimal price = product.Price.Value;

        var existingMap = await _productMapRepository.FirstOrDefaultAsync(
            new SupplierProductMapByExternalIdSpecification(supplierId, matchKey), cancellationToken);

        if (existingMap is not null)
        {
            var existingItem = await _catalogItemRepository.GetByIdAsync(existingMap.CatalogItemId, cancellationToken);
            if (existingItem is not null)
            {
                existingItem.UpdateDetails(new CatalogItem.CatalogItemDetails(name, description, price));
                existingItem.UpdateBrand(brandId);
                existingItem.UpdateType(defaultTypeId);
                await _catalogItemRepository.UpdateAsync(existingItem, cancellationToken);
                return true;
            }

            // The mapped catalog item is gone; drop the stale mapping and re-create below.
            await _productMapRepository.DeleteAsync(existingMap, cancellationToken);
        }

        var newItem = new CatalogItem(defaultTypeId, brandId, description, name, price, string.Empty);
        newItem.UpdatePictureUri(DefaultPictureName);
        newItem = await _catalogItemRepository.AddAsync(newItem, cancellationToken);

        await _productMapRepository.AddAsync(
            new SupplierProductMap(supplierId, matchKey, newItem.Id), cancellationToken);
        return true;
    }

    private async Task<int> GetOrCreateCatalogBrandAsync(
        string brandName, Dictionary<string, int> cache, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(brandName, out var cachedId))
        {
            return cachedId;
        }

        var existing = await _catalogBrandRepository.FirstOrDefaultAsync(
            new CatalogBrandByNameSpecification(brandName), cancellationToken);

        int id = existing?.Id
            ?? (await _catalogBrandRepository.AddAsync(new CatalogBrand(brandName), cancellationToken)).Id;

        cache[brandName] = id;
        return id;
    }

    private async Task<int> GetOrCreateCatalogTypeAsync(string typeName, CancellationToken cancellationToken)
    {
        var existing = await _catalogTypeRepository.FirstOrDefaultAsync(
            new CatalogTypeByNameSpecification(typeName), cancellationToken);

        return existing?.Id
            ?? (await _catalogTypeRepository.AddAsync(new CatalogType(typeName), cancellationToken)).Id;
    }

    /// <summary>The supplier's own identifier is preferred; the product URL is the fallback match key.</summary>
    private static string? ResolveMatchKey(ScrapedProduct product)
    {
        if (!string.IsNullOrWhiteSpace(product.ExternalId))
        {
            return product.ExternalId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(product.Url))
        {
            return product.Url.Trim();
        }

        return null;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value.Substring(0, maxLength);
}
