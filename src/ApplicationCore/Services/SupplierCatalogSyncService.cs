using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Reads a supplier's product listing via Firecrawl and imports the products it finds into the
/// store's own catalog. Each product is matched to a catalog item by the supplier's own identifier
/// (SKU or product URL), so re-running a sync updates the same catalog item instead of duplicating it.
/// </summary>
public class SupplierCatalogSyncService : ISupplierCatalogSyncService
{
    // Imported names/brands are constrained to the catalog's own column widths.
    private const int NameMaxLength = 50;
    private const int BrandMaxLength = 100;
    private const int ExternalIdMaxLength = 400;

    // Products imported from a supplier have no catalog "type"; they are grouped under this one.
    private const string ImportedTypeName = "Imported";

    // Fallback brand for products whose listing exposes no brand. Matches an existing seeded brand.
    private const string DefaultBrandName = "Other";

    // Placeholder image so imported items render in the existing catalog listing.
    private const string DefaultPictureName = "eCatalog-item-default.png";

    private readonly IRepository<CatalogSync> _syncRepository;
    private readonly IRepository<Supplier> _supplierRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IRepository<CatalogBrand> _catalogBrandRepository;
    private readonly IRepository<CatalogType> _catalogTypeRepository;
    private readonly IRepository<SupplierCatalogItem> _mappingRepository;
    private readonly IFirecrawlClient _firecrawlClient;
    private readonly IAppLogger<SupplierCatalogSyncService> _logger;

    public SupplierCatalogSyncService(
        IRepository<CatalogSync> syncRepository,
        IRepository<Supplier> supplierRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IRepository<CatalogBrand> catalogBrandRepository,
        IRepository<CatalogType> catalogTypeRepository,
        IRepository<SupplierCatalogItem> mappingRepository,
        IFirecrawlClient firecrawlClient,
        IAppLogger<SupplierCatalogSyncService> logger)
    {
        _syncRepository = syncRepository;
        _supplierRepository = supplierRepository;
        _catalogItemRepository = catalogItemRepository;
        _catalogBrandRepository = catalogBrandRepository;
        _catalogTypeRepository = catalogTypeRepository;
        _mappingRepository = mappingRepository;
        _firecrawlClient = firecrawlClient;
        _logger = logger;
    }

    public async Task ExecuteSyncAsync(int syncId, CancellationToken cancellationToken = default)
    {
        var sync = await _syncRepository.GetByIdAsync(syncId, cancellationToken);
        if (sync is null)
        {
            _logger.LogWarning($"Sync {syncId} was requested but no longer exists.");
            return;
        }

        var supplier = await _supplierRepository.GetByIdAsync(sync.SupplierId, cancellationToken);
        if (supplier is null)
        {
            sync.Fail($"Supplier {sync.SupplierId} no longer exists.");
            await _syncRepository.UpdateAsync(sync, cancellationToken);
            return;
        }

        try
        {
            _logger.LogInformation($"Sync {syncId}: reading listing for supplier '{supplier.Name}' at {supplier.ProductListingUrl}.");
            var products = await _firecrawlClient.ScrapeProductListingAsync(supplier.ProductListingUrl, cancellationToken);

            int found = products.Count;
            int imported = 0;

            // Resolve the shared "Imported" type once per run.
            int importedTypeId = await ResolveTypeAsync(ImportedTypeName, cancellationToken);

            foreach (var product in products)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (await ImportProductAsync(supplier.Id, product, importedTypeId, cancellationToken))
                    {
                        imported++;
                    }
                }
                catch (Exception ex)
                {
                    // A single unusable product must not abort the whole sync; it simply counts as
                    // "found but not imported", which surfaces as a partial capture.
                    _logger.LogWarning($"Sync {syncId}: skipped a product ('{product.Name}') that could not be imported: {ex.Message}");
                }
            }

            sync.Complete(found, imported);
            await _syncRepository.UpdateAsync(sync, cancellationToken);
            _logger.LogInformation($"Sync {syncId}: finished with status {sync.Status} ({imported}/{found} imported).");
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Sync {syncId}: failed to read supplier listing: {ex.Message}");
            sync.Fail(ex.Message);
            await _syncRepository.UpdateAsync(sync, cancellationToken);
        }
    }

    /// <summary>
    /// Creates or updates the catalog item for a single scraped product. Returns true when the
    /// product was imported, false when it was skipped for want of the minimum required data.
    /// </summary>
    private async Task<bool> ImportProductAsync(int supplierId, ScrapedProduct product, int typeId, CancellationToken cancellationToken)
    {
        var name = Truncate(product.Name?.Trim(), NameMaxLength);
        if (string.IsNullOrWhiteSpace(name))
        {
            return false; // A product with no name cannot be matched or displayed.
        }

        var price = product.Price ?? 0m;
        if (price <= 0m)
        {
            return false; // Without a usable price the item cannot be sold; treat as not imported.
        }

        var externalId = Truncate(FirstNonEmpty(product.Sku, product.ProductUrl, name), ExternalIdMaxLength);
        var description = string.IsNullOrWhiteSpace(product.Description) ? name : product.Description.Trim();
        var brandId = await ResolveBrandAsync(product.Brand, cancellationToken);

        var existingMapping = await _mappingRepository.FirstOrDefaultAsync(
            new SupplierCatalogItemByExternalIdSpecification(supplierId, externalId!), cancellationToken);

        if (existingMapping is not null)
        {
            var existingItem = await _catalogItemRepository.GetByIdAsync(existingMapping.CatalogItemId, cancellationToken);
            if (existingItem is not null)
            {
                existingItem.UpdateDetails(new CatalogItem.CatalogItemDetails(name, description, price));
                existingItem.UpdateBrand(brandId);
                existingItem.UpdateType(typeId);
                await _catalogItemRepository.UpdateAsync(existingItem, cancellationToken);

                existingMapping.MarkSynced();
                await _mappingRepository.UpdateAsync(existingMapping, cancellationToken);
                return true;
            }

            // The mapped catalog item was removed out of band; drop the stale mapping and re-create.
            await _mappingRepository.DeleteAsync(existingMapping, cancellationToken);
        }

        var newItem = new CatalogItem(typeId, brandId, description, name, price, DefaultPictureName);
        newItem = await _catalogItemRepository.AddAsync(newItem, cancellationToken);
        newItem.UpdatePictureUri(DefaultPictureName);
        await _catalogItemRepository.UpdateAsync(newItem, cancellationToken);

        await _mappingRepository.AddAsync(new SupplierCatalogItem(supplierId, externalId!, newItem.Id), cancellationToken);
        return true;
    }

    private async Task<int> ResolveBrandAsync(string? brand, CancellationToken cancellationToken)
    {
        var brandName = Truncate(string.IsNullOrWhiteSpace(brand) ? DefaultBrandName : brand.Trim(), BrandMaxLength)!;

        var existing = await _catalogBrandRepository.FirstOrDefaultAsync(
            new CatalogBrandByNameSpecification(brandName), cancellationToken);
        if (existing is not null)
        {
            return existing.Id;
        }

        var created = await _catalogBrandRepository.AddAsync(new CatalogBrand(brandName), cancellationToken);
        return created.Id;
    }

    private async Task<int> ResolveTypeAsync(string typeName, CancellationToken cancellationToken)
    {
        var existing = await _catalogTypeRepository.FirstOrDefaultAsync(
            new CatalogTypeByNameSpecification(typeName), cancellationToken);
        if (existing is not null)
        {
            return existing.Id;
        }

        var created = await _catalogTypeRepository.AddAsync(new CatalogType(typeName), cancellationToken);
        return created.Id;
    }

    private static string? FirstNonEmpty(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate.Trim();
            }
        }
        return null;
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }
        return value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }
}
