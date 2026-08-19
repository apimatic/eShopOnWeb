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
/// Reads a supplier's product listing through Firecrawl and matches every product it finds into
/// the store's own catalog. Matching is done by the supplier's own identifier or URL for the
/// product, so re-running a sync updates the same catalog item instead of creating a duplicate.
/// </summary>
public class SupplierCatalogSyncService : ISupplierCatalogSyncService
{
    // Products imported from a supplier have no catalog "type" of their own; they are grouped
    // under a single reserved type so they satisfy the catalog's required type relationship.
    private const string ImportedCatalogTypeName = "Imported";
    private const string UnknownBrandName = "Unknown";
    private const string DefaultPicture = "eCatalog-item-default.png";

    private readonly IRepository<CatalogSync> _syncRepository;
    private readonly IRepository<Supplier> _supplierRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IRepository<CatalogBrand> _brandRepository;
    private readonly IRepository<CatalogType> _typeRepository;
    private readonly IRepository<SupplierCatalogItem> _linkRepository;
    private readonly IFirecrawlClient _firecrawlClient;
    private readonly IAppLogger<SupplierCatalogSyncService> _logger;

    public SupplierCatalogSyncService(
        IRepository<CatalogSync> syncRepository,
        IRepository<Supplier> supplierRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IRepository<CatalogBrand> brandRepository,
        IRepository<CatalogType> typeRepository,
        IRepository<SupplierCatalogItem> linkRepository,
        IFirecrawlClient firecrawlClient,
        IAppLogger<SupplierCatalogSyncService> logger)
    {
        _syncRepository = syncRepository;
        _supplierRepository = supplierRepository;
        _catalogItemRepository = catalogItemRepository;
        _brandRepository = brandRepository;
        _typeRepository = typeRepository;
        _linkRepository = linkRepository;
        _firecrawlClient = firecrawlClient;
        _logger = logger;
    }

    public async Task RunSyncAsync(int syncId, CancellationToken cancellationToken = default)
    {
        var sync = await _syncRepository.GetByIdAsync(syncId, cancellationToken);
        if (sync is null)
        {
            _logger.LogWarning("Sync {0} no longer exists; skipping.", syncId);
            return;
        }

        var supplier = await _supplierRepository.GetByIdAsync(sync.SupplierId, cancellationToken);
        if (supplier is null)
        {
            sync.MarkFailed($"Supplier {sync.SupplierId} no longer exists.");
            await _syncRepository.UpdateAsync(sync, cancellationToken);
            return;
        }

        _logger.LogInformation("Sync {0} started for supplier '{1}' ({2}).", sync.Id, supplier.Name, supplier.ListingUrl);

        FirecrawlScrapeResult result;
        try
        {
            result = await _firecrawlClient.ScrapeProductListingAsync(supplier.ListingUrl, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Sync {0} could not read the listing: {1}", sync.Id, ex.Message);
            sync.MarkFailed($"Failed to read supplier listing: {ex.Message}");
            await _syncRepository.UpdateAsync(sync, cancellationToken);
            return;
        }

        if (!result.Success)
        {
            sync.MarkFailed(result.Error ?? "Firecrawl returned an unsuccessful response.");
            await _syncRepository.UpdateAsync(sync, cancellationToken);
            return;
        }

        var found = result.Products.Count;
        var imported = 0;

        var typeId = await GetOrCreateTypeIdAsync(ImportedCatalogTypeName, cancellationToken);
        var brandCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var product in result.Products)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var name = product.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name) || !product.Price.HasValue || product.Price.Value <= 0m)
            {
                // A product we found but cannot represent as a catalog item (missing name/price).
                // It counts toward "found" but not "imported", which surfaces as a Partial sync.
                _logger.LogWarning("Sync {0}: skipping product with missing name or price (name='{1}', price={2}).",
                    sync.Id, product.Name ?? "(none)", product.Price?.ToString() ?? "(none)");
                continue;
            }

            var description = string.IsNullOrWhiteSpace(product.Description) ? name : product.Description!.Trim();
            var price = product.Price!.Value;
            var externalKey = ResolveExternalKey(product);
            var nameKey = name.ToLowerInvariant();
            var brandId = await GetOrCreateBrandIdAsync(product.Brand, brandCache, cancellationToken);

            try
            {
                await UpsertCatalogItemAsync(supplier.Id, externalKey, nameKey, name, description, price, typeId, brandId, cancellationToken);
                imported++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Sync {0}: failed to import product '{1}': {2}", sync.Id, name, ex.Message);
            }
        }

        sync.MarkFinished(found, imported);
        await _syncRepository.UpdateAsync(sync, cancellationToken);

        _logger.LogInformation("Sync {0} finished with status {1}: {2} found, {3} imported.",
            sync.Id, sync.Status, sync.ItemsFound, sync.ItemsImported);
    }

    private async Task UpsertCatalogItemAsync(int supplierId, string externalKey, string nameKey, string name,
        string description, decimal price, int typeId, int brandId, CancellationToken cancellationToken)
    {
        var linkSpec = new SupplierCatalogItemByExternalKeySpecification(supplierId, externalKey, nameKey);
        var link = await _linkRepository.FirstOrDefaultAsync(linkSpec, cancellationToken);

        if (link is not null)
        {
            var existing = await _catalogItemRepository.GetByIdAsync(link.CatalogItemId, cancellationToken);
            if (existing is not null)
            {
                existing.UpdateDetails(new CatalogItem.CatalogItemDetails(name, description, price));
                existing.UpdateType(typeId);
                existing.UpdateBrand(brandId);
                await _catalogItemRepository.UpdateAsync(existing, cancellationToken);

                // Self-heal the stored keys in case the supplier's identifier/URL drifted between reads.
                link.UpdateKeys(externalKey, nameKey);
                await _linkRepository.UpdateAsync(link, cancellationToken);
                return;
            }

            // The link is orphaned (its catalog item is gone); recreate the item and re-point the link.
            var recreated = await CreateCatalogItemAsync(typeId, brandId, description, name, price, cancellationToken);
            link.PointToCatalogItem(recreated.Id);
            link.UpdateKeys(externalKey, nameKey);
            await _linkRepository.UpdateAsync(link, cancellationToken);
            return;
        }

        var created = await CreateCatalogItemAsync(typeId, brandId, description, name, price, cancellationToken);
        var newLink = new SupplierCatalogItem(supplierId, externalKey, nameKey, created.Id);
        await _linkRepository.AddAsync(newLink, cancellationToken);
    }

    private async Task<CatalogItem> CreateCatalogItemAsync(int typeId, int brandId, string description, string name,
        decimal price, CancellationToken cancellationToken)
    {
        var item = new CatalogItem(typeId, brandId, description, name, price, string.Empty);
        item = await _catalogItemRepository.AddAsync(item, cancellationToken);

        // Uploads are disabled in this sample; imported items use the shared placeholder image,
        // matching how the existing create-catalog-item endpoint behaves.
        item.UpdatePictureUri(DefaultPicture);
        await _catalogItemRepository.UpdateAsync(item, cancellationToken);
        return item;
    }

    private static string ResolveExternalKey(ScrapedProduct product)
    {
        // The supplier's own identifier or URL for the product. When the listing exposes neither on
        // this read, the name-based secondary key (computed by the caller) still prevents duplicates.
        if (!string.IsNullOrWhiteSpace(product.ExternalId))
        {
            return $"id:{product.ExternalId!.Trim()}";
        }

        if (!string.IsNullOrWhiteSpace(product.Url))
        {
            return $"url:{product.Url!.Trim().TrimEnd('/')}";
        }

        return $"name:{(product.Name ?? string.Empty).Trim().ToLowerInvariant()}";
    }

    private async Task<int> GetOrCreateBrandIdAsync(string? brandName, IDictionary<string, int> cache,
        CancellationToken cancellationToken)
    {
        var name = string.IsNullOrWhiteSpace(brandName) ? UnknownBrandName : brandName!.Trim();

        if (cache.TryGetValue(name, out var cachedId))
        {
            return cachedId;
        }

        var existing = await _brandRepository.FirstOrDefaultAsync(new CatalogBrandByNameSpecification(name), cancellationToken);
        var id = existing?.Id ?? (await _brandRepository.AddAsync(new CatalogBrand(name), cancellationToken)).Id;

        cache[name] = id;
        return id;
    }

    private async Task<int> GetOrCreateTypeIdAsync(string typeName, CancellationToken cancellationToken)
    {
        var existing = await _typeRepository.FirstOrDefaultAsync(new CatalogTypeByNameSpecification(typeName), cancellationToken);
        if (existing is not null)
        {
            return existing.Id;
        }

        var created = await _typeRepository.AddAsync(new CatalogType(typeName), cancellationToken);
        return created.Id;
    }
}
