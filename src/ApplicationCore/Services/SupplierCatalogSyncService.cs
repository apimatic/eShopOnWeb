using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Reads a supplier's product listing and imports the products it finds into the store's own
/// catalog. Matching is by the supplier's own identifier (product URL or SKU), so re-running a
/// sync updates the same catalog item instead of creating a duplicate.
/// </summary>
public class SupplierCatalogSyncService : ISupplierCatalogSyncService
{
    // Catalog items require a non-zero CatalogType; supplier listings don't carry one, so every
    // imported product is filed under this single, self-created type.
    private const string ImportedTypeName = "Supplier Import";

    // A catalog item name column is capped at 50 chars; keep imports within that.
    private const int MaxNameLength = 50;
    private const int MaxBrandLength = 100;

    // Keep the supplier's external identifier within a SQL unique-index key (900-byte limit).
    private const int MaxExternalIdLength = 450;

    private readonly IRepository<SupplierSync> _syncRepository;
    private readonly IRepository<Supplier> _supplierRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IRepository<CatalogBrand> _catalogBrandRepository;
    private readonly IRepository<CatalogType> _catalogTypeRepository;
    private readonly IRepository<SupplierCatalogItem> _linkRepository;
    private readonly ISupplierListingReader _listingReader;
    private readonly IAppLogger<SupplierCatalogSyncService> _logger;

    public SupplierCatalogSyncService(
        IRepository<SupplierSync> syncRepository,
        IRepository<Supplier> supplierRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IRepository<CatalogBrand> catalogBrandRepository,
        IRepository<CatalogType> catalogTypeRepository,
        IRepository<SupplierCatalogItem> linkRepository,
        ISupplierListingReader listingReader,
        IAppLogger<SupplierCatalogSyncService> logger)
    {
        _syncRepository = syncRepository;
        _supplierRepository = supplierRepository;
        _catalogItemRepository = catalogItemRepository;
        _catalogBrandRepository = catalogBrandRepository;
        _catalogTypeRepository = catalogTypeRepository;
        _linkRepository = linkRepository;
        _listingReader = listingReader;
        _logger = logger;
    }

    public async Task ProcessSyncAsync(int syncId, CancellationToken cancellationToken = default)
    {
        var sync = await _syncRepository.GetByIdAsync(syncId, cancellationToken);
        if (sync is null)
        {
            _logger.LogWarning($"Supplier sync {syncId} not found; nothing to process.");
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

        try
        {
            var listing = await _listingReader.ReadListingAsync(supplier.ProductListingUrl, cancellationToken);
            if (!listing.Success)
            {
                sync.MarkFailed(listing.Error ?? "Failed to read the supplier's product listing.");
                await _syncRepository.UpdateAsync(sync, cancellationToken);
                _logger.LogWarning($"Supplier sync {syncId} failed reading '{supplier.ProductListingUrl}': {sync.Error}");
                return;
            }

            int itemsFound = listing.Products.Count;
            int itemsImported = 0;
            int importedTypeId = await EnsureCatalogTypeAsync(ImportedTypeName, cancellationToken);

            foreach (var product in listing.Products)
            {
                try
                {
                    if (await ImportProductAsync(supplier, product, importedTypeId, cancellationToken))
                    {
                        itemsImported++;
                    }
                }
                catch (Exception ex)
                {
                    // A single malformed product must not abort the whole sync — it just means a
                    // partial capture (itemsImported < itemsFound).
                    _logger.LogWarning($"Supplier sync {syncId} skipped a product from '{supplier.ProductListingUrl}': {ex.Message}");
                }
            }

            sync.MarkCompleted(itemsFound, itemsImported);
            await _syncRepository.UpdateAsync(sync, cancellationToken);
            _logger.LogInformation($"Supplier sync {syncId} finished: {itemsImported}/{itemsFound} products imported ({sync.Status}).");
        }
        catch (Exception ex)
        {
            sync.MarkFailed(ex.Message);
            await _syncRepository.UpdateAsync(sync, cancellationToken);
            _logger.LogWarning($"Supplier sync {syncId} failed unexpectedly: {ex.Message}");
        }
    }

    /// <summary>
    /// Creates or updates the catalog item for one scraped product. Returns false when the
    /// product is too incomplete to import (so it counts against a partial capture).
    /// </summary>
    private async Task<bool> ImportProductAsync(Supplier supplier, SupplierProduct product, int catalogTypeId, CancellationToken cancellationToken)
    {
        var name = Clean(product.Name);
        if (string.IsNullOrWhiteSpace(name) || !product.Price.HasValue || product.Price.Value <= 0)
        {
            // Name and a positive price are the minimum a catalog item needs.
            return false;
        }

        name = Truncate(name!, MaxNameLength);
        var description = string.IsNullOrWhiteSpace(product.Description) ? name : product.Description!.Trim();
        decimal price = product.Price.Value;

        string externalId = ResolveExternalId(supplier, product);
        int brandId = await EnsureCatalogBrandAsync(product.Brand, supplier, cancellationToken);

        var existingLink = await _linkRepository.FirstOrDefaultAsync(
            new SupplierCatalogItemByExternalIdSpecification(supplier.Id, externalId), cancellationToken);

        if (existingLink is not null)
        {
            var existingItem = await _catalogItemRepository.GetByIdAsync(existingLink.CatalogItemId, cancellationToken);
            if (existingItem is not null)
            {
                existingItem.UpdateDetails(new CatalogItem.CatalogItemDetails(name, description, price));
                existingItem.UpdateBrand(brandId);
                existingItem.UpdateType(catalogTypeId);
                await _catalogItemRepository.UpdateAsync(existingItem, cancellationToken);

                existingLink.MarkSynced();
                await _linkRepository.UpdateAsync(existingLink, cancellationToken);
                return true;
            }

            // The link points at an item that no longer exists — recreate the item and re-point.
            var replacement = await CreateCatalogItemAsync(catalogTypeId, brandId, description, name, price, cancellationToken);
            existingLink.LinkTo(replacement.Id);
            await _linkRepository.UpdateAsync(existingLink, cancellationToken);
            return true;
        }

        var newItem = await CreateCatalogItemAsync(catalogTypeId, brandId, description, name, price, cancellationToken);
        await _linkRepository.AddAsync(new SupplierCatalogItem(supplier.Id, externalId, newItem.Id), cancellationToken);
        return true;
    }

    private async Task<CatalogItem> CreateCatalogItemAsync(int catalogTypeId, int brandId, string description, string name, decimal price, CancellationToken cancellationToken)
    {
        var item = new CatalogItem(catalogTypeId, brandId, description, name, price, string.Empty);
        // Mirror the store's own create flow: imported items use the shared placeholder image.
        item.UpdatePictureUri("eCatalog-item-default.png");
        return await _catalogItemRepository.AddAsync(item, cancellationToken);
    }

    /// <summary>
    /// The supplier's own stable identifier for the product: its detail URL, else its SKU, else
    /// a normalized fallback from the product name.
    /// </summary>
    private static string ResolveExternalId(Supplier supplier, SupplierProduct product)
    {
        string raw;
        if (!string.IsNullOrWhiteSpace(product.Url))
        {
            raw = $"url:{product.Url!.Trim()}";
        }
        else if (!string.IsNullOrWhiteSpace(product.Sku))
        {
            raw = $"sku:{product.Sku!.Trim()}";
        }
        else
        {
            raw = $"name:{(product.Name ?? string.Empty).Trim().ToLowerInvariant()}";
        }
        return Truncate(raw, MaxExternalIdLength);
    }

    private async Task<int> EnsureCatalogBrandAsync(string? brandName, Supplier supplier, CancellationToken cancellationToken)
    {
        var name = Clean(brandName);
        if (string.IsNullOrWhiteSpace(name))
        {
            // Fall back to the supplier's own name so the item still carries a meaningful brand.
            name = supplier.Name;
        }
        name = Truncate(name!, MaxBrandLength);

        var existing = await _catalogBrandRepository.FirstOrDefaultAsync(new CatalogBrandByNameSpecification(name), cancellationToken);
        if (existing is not null)
        {
            return existing.Id;
        }

        var created = await _catalogBrandRepository.AddAsync(new CatalogBrand(name), cancellationToken);
        return created.Id;
    }

    private async Task<int> EnsureCatalogTypeAsync(string typeName, CancellationToken cancellationToken)
    {
        var existing = await _catalogTypeRepository.FirstOrDefaultAsync(new CatalogTypeByNameSpecification(typeName), cancellationToken);
        if (existing is not null)
        {
            return existing.Id;
        }

        var created = await _catalogTypeRepository.AddAsync(new CatalogType(typeName), cancellationToken);
        return created.Id;
    }

    private static string? Clean(string? value) => value?.Trim();

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value.Substring(0, maxLength);
}
