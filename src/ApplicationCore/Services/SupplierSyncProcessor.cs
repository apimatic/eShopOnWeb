using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SupplierSyncProcessor : ISupplierSyncProcessor
{
    // Catalog items require a type; supplier listings only give us name/description/price/brand,
    // so imported items land under a dedicated type that keeps them visible in the catalog listing.
    private const string ImportedCatalogTypeName = "Imported";
    private const string DefaultBrandName = "Other";

    // The existing Catalog schema caps these lengths; keep imports within them so the SQL provider
    // (used outside this in-memory dev setup) accepts them too.
    private const int MaxNameLength = 50;
    private const int MaxBrandLength = 100;

    private readonly IRepository<CatalogSync> _syncRepository;
    private readonly IReadRepository<Supplier> _supplierRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IRepository<CatalogBrand> _catalogBrandRepository;
    private readonly IRepository<CatalogType> _catalogTypeRepository;
    private readonly ISupplierCatalogReader _reader;
    private readonly IAppLogger<SupplierSyncProcessor> _logger;

    public SupplierSyncProcessor(
        IRepository<CatalogSync> syncRepository,
        IReadRepository<Supplier> supplierRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IRepository<CatalogBrand> catalogBrandRepository,
        IRepository<CatalogType> catalogTypeRepository,
        ISupplierCatalogReader reader,
        IAppLogger<SupplierSyncProcessor> logger)
    {
        _syncRepository = syncRepository;
        _supplierRepository = supplierRepository;
        _catalogItemRepository = catalogItemRepository;
        _catalogBrandRepository = catalogBrandRepository;
        _catalogTypeRepository = catalogTypeRepository;
        _reader = reader;
        _logger = logger;
    }

    public async Task ProcessAsync(int syncId, CancellationToken cancellationToken = default)
    {
        var sync = await _syncRepository.GetByIdAsync(syncId, cancellationToken);
        if (sync is null)
        {
            _logger.LogWarning($"Sync {syncId} not found; nothing to process.");
            return;
        }

        try
        {
            var supplier = await _supplierRepository.GetByIdAsync(sync.SupplierId, cancellationToken);
            if (supplier is null)
            {
                sync.MarkFailed($"Supplier {sync.SupplierId} no longer exists.");
                await _syncRepository.UpdateAsync(sync, cancellationToken);
                return;
            }

            var products = await _reader.ReadProductListingAsync(supplier.ProductListingUrl, cancellationToken);

            int found = products.Count;
            int imported = 0;

            foreach (var product in products)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (await TryImportProductAsync(supplier, product, cancellationToken))
                    {
                        imported++;
                    }
                }
                catch (Exception ex)
                {
                    // One bad product must not fail the whole sync; it just won't be counted as imported.
                    _logger.LogWarning($"Failed to import product '{product.ProductKey ?? product.Name}' for supplier {supplier.Id}: {ex.Message}");
                }
            }

            sync.MarkCompleted(found, imported);
            await _syncRepository.UpdateAsync(sync, cancellationToken);
            _logger.LogInformation($"Sync {syncId} for supplier {supplier.Id} finished: {imported}/{found} imported ({sync.Status}).");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Sync {syncId} failed: {ex.Message}");
            try
            {
                sync.MarkFailed(ex.Message);
                await _syncRepository.UpdateAsync(sync, cancellationToken);
            }
            catch (Exception updateEx)
            {
                _logger.LogWarning($"Could not record failure for sync {syncId}: {updateEx.Message}");
            }
        }
    }

    /// <summary>
    /// Matches the product against the catalog by supplier + supplier key. Updates the existing item
    /// if found, otherwise creates a new one. Returns false when the product lacks the minimum data
    /// needed to be a valid catalog item (so it counts as found-but-not-imported).
    /// </summary>
    private async Task<bool> TryImportProductAsync(Supplier supplier, SupplierProduct product, CancellationToken cancellationToken)
    {
        var name = Clip(product.Name, MaxNameLength);
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        // A stable key is required so re-syncs update the same item; prefer the supplier's URL/id and
        // fall back to the product name.
        var key = FirstNonBlank(product.ProductKey, product.Name);
        if (key is null)
        {
            return false;
        }

        if (!product.Price.HasValue || product.Price.Value <= 0m)
        {
            return false;
        }

        var price = decimal.Round(product.Price.Value, 2);
        var description = FirstNonBlank(product.Description, name)!;

        var brand = await GetOrCreateBrandAsync(product.Brand, cancellationToken);
        var type = await GetOrCreateTypeAsync(cancellationToken);

        var existing = await _catalogItemRepository.FirstOrDefaultAsync(
            new CatalogItemsBySupplierKeySpecification(supplier.Id, key), cancellationToken);

        if (existing is not null)
        {
            existing.UpdateDetails(new CatalogItem.CatalogItemDetails(name, description, price));
            existing.UpdateBrand(brand.Id);
            existing.UpdateType(type.Id);
            await _catalogItemRepository.UpdateAsync(existing, cancellationToken);
            return true;
        }

        var newItem = new CatalogItem(type.Id, brand.Id, description, name, price, string.Empty);
        newItem.LinkToSupplier(supplier.Id, key);
        // Match the store's own convention of shipping a placeholder image for catalog items.
        newItem.UpdatePictureUri("eCatalog-item-default.png");
        await _catalogItemRepository.AddAsync(newItem, cancellationToken);
        return true;
    }

    private async Task<CatalogBrand> GetOrCreateBrandAsync(string? brandName, CancellationToken cancellationToken)
    {
        var name = Clip(FirstNonBlank(brandName, DefaultBrandName), MaxBrandLength)!;
        var existing = await _catalogBrandRepository.FirstOrDefaultAsync(
            new CatalogBrandByNameSpecification(name), cancellationToken);
        return existing ?? await _catalogBrandRepository.AddAsync(new CatalogBrand(name), cancellationToken);
    }

    private async Task<CatalogType> GetOrCreateTypeAsync(CancellationToken cancellationToken)
    {
        var existing = await _catalogTypeRepository.FirstOrDefaultAsync(
            new CatalogTypeByNameSpecification(ImportedCatalogTypeName), cancellationToken);
        return existing ?? await _catalogTypeRepository.AddAsync(new CatalogType(ImportedCatalogTypeName), cancellationToken);
    }

    private static string? FirstNonBlank(params string?[] candidates)
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

    private static string? Clip(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        value = value.Trim();
        return value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }
}
