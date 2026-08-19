using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierCatalogAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Reads a supplier's product listing and upserts every product into the store catalog.
/// Products are matched by the supplier's own identifier/URL so re-running a sync updates
/// the same catalog item instead of creating a duplicate.
/// </summary>
public class SupplierCatalogSyncService : ISupplierCatalogSyncService
{
    // Imported catalog items need a catalog type (the domain requires one) but a supplier
    // listing doesn't carry one, so imports share a single dedicated type.
    private const string ImportedTypeName = "Supplier Import";
    private const string DefaultBrandName = "Other";
    private const string DefaultPictureName = "eCatalog-item-default.png";
    private const int NameMaxLength = 50;
    private const int BrandMaxLength = 100;
    private const int TypeMaxLength = 100;

    private readonly IRepository<CatalogSync> _syncRepository;
    private readonly IReadRepository<Supplier> _supplierRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IRepository<CatalogBrand> _brandRepository;
    private readonly IRepository<CatalogType> _typeRepository;
    private readonly IRepository<SupplierProduct> _supplierProductRepository;
    private readonly ISupplierProductScraper _scraper;
    private readonly IAppLogger<SupplierCatalogSyncService> _logger;

    public SupplierCatalogSyncService(
        IRepository<CatalogSync> syncRepository,
        IReadRepository<Supplier> supplierRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IRepository<CatalogBrand> brandRepository,
        IRepository<CatalogType> typeRepository,
        IRepository<SupplierProduct> supplierProductRepository,
        ISupplierProductScraper scraper,
        IAppLogger<SupplierCatalogSyncService> logger)
    {
        _syncRepository = syncRepository;
        _supplierRepository = supplierRepository;
        _catalogItemRepository = catalogItemRepository;
        _brandRepository = brandRepository;
        _typeRepository = typeRepository;
        _supplierProductRepository = supplierProductRepository;
        _scraper = scraper;
        _logger = logger;
    }

    public async Task RunSyncAsync(Guid syncId, CancellationToken cancellationToken)
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
            sync.Fail($"Supplier {sync.SupplierId} no longer exists.");
            await _syncRepository.UpdateAsync(sync, CancellationToken.None);
            return;
        }

        sync.MarkRunning();
        await _syncRepository.UpdateAsync(sync, cancellationToken);
        _logger.LogInformation("Sync {0} started for supplier '{1}' ({2}).",
            sync.Id, supplier.Name, supplier.ProductListingUrl);

        int found = 0;
        int imported = 0;
        try
        {
            var result = await _scraper.ScrapeListingAsync(supplier.ProductListingUrl, cancellationToken);
            found = result.Products.Count;

            var importType = await GetOrCreateTypeAsync(ImportedTypeName, cancellationToken);

            foreach (var product in result.Products)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!TryNormalize(product, out var name, out var description, out var price, out var externalId))
                {
                    _logger.LogWarning("Sync {0}: skipping product with insufficient data (externalId='{1}', name='{2}').",
                        sync.Id, product.ExternalId, product.Name ?? string.Empty);
                    continue;
                }

                var brand = await GetOrCreateBrandAsync(product.Brand, cancellationToken);
                await UpsertCatalogItemAsync(supplier.Id, externalId, name, description, price,
                    brand.Id, importType.Id, cancellationToken);
                imported++;
            }

            sync.Complete(found, imported);
            await _syncRepository.UpdateAsync(sync, cancellationToken);
            _logger.LogInformation("Sync {0} finished with status {1}: {2} found, {3} imported.",
                sync.Id, sync.Status, found, imported);
        }
        catch (Exception ex)
        {
            // Persist the failure (with any partial counts) even if the run was cancelled.
            sync.Fail(ex.Message, found, imported);
            await _syncRepository.UpdateAsync(sync, CancellationToken.None);
            _logger.LogWarning("Sync {0} failed after {1} found / {2} imported: {3}",
                sync.Id, found, imported, ex.Message);
        }
    }

    /// <summary>
    /// Upserts a single scraped product into the catalog, keyed by the supplier's own
    /// identifier/URL so a second sync never creates a duplicate.
    /// </summary>
    private async Task UpsertCatalogItemAsync(Guid supplierId, string externalId, string name,
        string description, decimal price, int brandId, int typeId, CancellationToken cancellationToken)
    {
        var mapping = await _supplierProductRepository.FirstOrDefaultAsync(
            new SupplierProductByExternalIdSpecification(supplierId, externalId), cancellationToken);

        if (mapping is not null)
        {
            var existing = await _catalogItemRepository.GetByIdAsync(mapping.CatalogItemId, cancellationToken);
            if (existing is not null)
            {
                existing.UpdateDetails(new CatalogItem.CatalogItemDetails(name, description, price));
                existing.UpdateBrand(brandId);
                existing.UpdateType(typeId);
                await _catalogItemRepository.UpdateAsync(existing, cancellationToken);

                mapping.MarkSynced();
                await _supplierProductRepository.UpdateAsync(mapping, cancellationToken);
                return;
            }

            // The catalog item was removed out from under us; drop the stale mapping and re-import.
            await _supplierProductRepository.DeleteAsync(mapping, cancellationToken);
        }

        var item = new CatalogItem(typeId, brandId, description, name, price, string.Empty);
        item.UpdatePictureUri(DefaultPictureName);
        item = await _catalogItemRepository.AddAsync(item, cancellationToken);

        await _supplierProductRepository.AddAsync(
            new SupplierProduct(supplierId, externalId, item.Id), cancellationToken);
    }

    private async Task<CatalogBrand> GetOrCreateBrandAsync(string? brandName, CancellationToken cancellationToken)
    {
        var name = Truncate(string.IsNullOrWhiteSpace(brandName) ? DefaultBrandName : brandName!.Trim(), BrandMaxLength);
        var brand = await _brandRepository.FirstOrDefaultAsync(new CatalogBrandByNameSpecification(name), cancellationToken);
        return brand ?? await _brandRepository.AddAsync(new CatalogBrand(name), cancellationToken);
    }

    private async Task<CatalogType> GetOrCreateTypeAsync(string typeName, CancellationToken cancellationToken)
    {
        var name = Truncate(typeName, TypeMaxLength);
        var type = await _typeRepository.FirstOrDefaultAsync(new CatalogTypeByNameSpecification(name), cancellationToken);
        return type ?? await _typeRepository.AddAsync(new CatalogType(name), cancellationToken);
    }

    /// <summary>
    /// Turns a scraped product into the fields the catalog requires. Returns false when the
    /// product cannot be imported (no usable name, or no positive price) — those count toward
    /// "found" but not "imported", which is exactly what drives a partial sync.
    /// </summary>
    private static bool TryNormalize(ScrapedProduct product, out string name, out string description,
        out decimal price, out string externalId)
    {
        name = string.Empty;
        description = string.Empty;
        price = 0m;
        externalId = string.Empty;

        var trimmedName = product.Name?.Trim();
        if (string.IsNullOrEmpty(trimmedName))
            return false;

        if (product.Price is not { } p || p <= 0m)
            return false;

        name = Truncate(trimmedName, NameMaxLength);
        price = p;

        var trimmedDescription = product.Description?.Trim();
        description = string.IsNullOrEmpty(trimmedDescription) ? name : trimmedDescription;

        // Prefer the supplier's own identifier/URL as the upsert key; fall back to the
        // product name so a product without one still de-duplicates across syncs.
        var trimmedExternalId = product.ExternalId?.Trim();
        externalId = string.IsNullOrEmpty(trimmedExternalId) ? $"name:{trimmedName}" : trimmedExternalId;
        return true;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value.Substring(0, maxLength);
}
