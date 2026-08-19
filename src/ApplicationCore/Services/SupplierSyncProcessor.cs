using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Executes one supplier sync: reads the supplier's listing through <see cref="ISupplierCatalogReader"/>
/// and matches every product into the store's own catalog. Matching is keyed on the supplier's own
/// identifier/URL for the product, so re-running a sync updates the same catalog item in place instead
/// of creating a duplicate.
/// </summary>
public class SupplierSyncProcessor : ISupplierSyncProcessor
{
    // CatalogItem.Name is constrained to 50 chars by the catalog schema.
    private const int NameMaxLength = 50;

    // Catalog type imported products are filed under when the listing carries no category of its own.
    private const string DefaultCatalogTypeName = "Supplier Catalog";

    // Brand used when a scraped product does not expose one.
    private const string DefaultBrandName = "Unbranded";

    // Placeholder image reused from the manual "create catalog item" flow.
    private const string DefaultPictureName = "eCatalog-item-default.png";

    private readonly IRepository<SupplierSync> _syncRepository;
    private readonly IRepository<Supplier> _supplierRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IRepository<CatalogBrand> _brandRepository;
    private readonly IRepository<CatalogType> _typeRepository;
    private readonly IRepository<SupplierCatalogItem> _mapRepository;
    private readonly ISupplierCatalogReader _reader;
    private readonly IAppLogger<SupplierSyncProcessor> _logger;

    public SupplierSyncProcessor(
        IRepository<SupplierSync> syncRepository,
        IRepository<Supplier> supplierRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IRepository<CatalogBrand> brandRepository,
        IRepository<CatalogType> typeRepository,
        IRepository<SupplierCatalogItem> mapRepository,
        ISupplierCatalogReader reader,
        IAppLogger<SupplierSyncProcessor> logger)
    {
        _syncRepository = syncRepository;
        _supplierRepository = supplierRepository;
        _catalogItemRepository = catalogItemRepository;
        _brandRepository = brandRepository;
        _typeRepository = typeRepository;
        _mapRepository = mapRepository;
        _reader = reader;
        _logger = logger;
    }

    public async Task ProcessAsync(int syncId, CancellationToken cancellationToken = default)
    {
        var sync = await _syncRepository.GetByIdAsync(syncId, cancellationToken);
        if (sync is null)
        {
            _logger.LogWarning($"Supplier sync {syncId} not found; nothing to process.");
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

            sync.MarkRunning();
            await _syncRepository.UpdateAsync(sync, cancellationToken);

            _logger.LogInformation($"Sync {syncId}: reading listing for supplier '{supplier.Name}' at {supplier.ListingUrl}.");
            var readResult = await _reader.ReadListingAsync(supplier.ListingUrl, cancellationToken);

            int itemsFound = readResult.Products.Count;
            int itemsImported = 0;
            var defaultTypeId = await EnsureCatalogTypeAsync(DefaultCatalogTypeName, cancellationToken);
            var brandCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var product in readResult.Products)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await ImportProductAsync(supplier.Id, product, defaultTypeId, brandCache, cancellationToken);
                    itemsImported++;
                }
                catch (Exception ex)
                {
                    // A single unusable product must not abort the whole sync; it simply counts as
                    // "found but not imported", which surfaces as a partial result.
                    _logger.LogWarning($"Sync {syncId}: skipped a product ('{product.Name}'): {ex.Message}");
                }
            }

            sync.RecordCounts(itemsFound, itemsImported);
            sync.MarkFinished(readResult.ListingFullyCaptured);
            await _syncRepository.UpdateAsync(sync, cancellationToken);

            _logger.LogInformation(
                $"Sync {syncId} finished with status {sync.Status}: found {itemsFound}, imported {itemsImported}.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Sync {syncId} failed: {ex}");
            sync.MarkFailed(ex.Message);
            try
            {
                await _syncRepository.UpdateAsync(sync, CancellationToken.None);
            }
            catch (Exception saveEx)
            {
                _logger.LogWarning($"Sync {syncId}: could not persist failed state: {saveEx}");
            }
        }
    }

    private async Task ImportProductAsync(
        int supplierId,
        ScrapedProduct product,
        int defaultTypeId,
        Dictionary<string, int> brandCache,
        CancellationToken cancellationToken)
    {
        var name = Normalize(product.Name, NameMaxLength);
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("product has no name.");

        // Description is required downstream; fall back to the name when the listing omits it.
        var description = string.IsNullOrWhiteSpace(product.Description) ? name! : product.Description!.Trim();

        if (product.Price is not { } price || price <= 0)
            throw new InvalidOperationException("product has no positive price.");

        var externalId = ResolveExternalId(product, name!);
        var brandId = await EnsureBrandAsync(product.Brand, brandCache, cancellationToken);

        var existingMapping = (await _mapRepository.ListAsync(
            new SupplierCatalogItemByKeySpecification(supplierId, externalId), cancellationToken)).FirstOrDefault();

        if (existingMapping is not null)
        {
            var existingItem = await _catalogItemRepository.GetByIdAsync(existingMapping.CatalogItemId, cancellationToken);
            if (existingItem is not null)
            {
                existingItem.UpdateDetails(new CatalogItem.CatalogItemDetails(name, description, price));
                existingItem.UpdateBrand(brandId);
                existingItem.UpdateType(defaultTypeId);
                await _catalogItemRepository.UpdateAsync(existingItem, cancellationToken);

                existingMapping.MarkResynced();
                await _mapRepository.UpdateAsync(existingMapping, cancellationToken);
                return;
            }

            // Mapping is dangling (its catalog item was removed) — drop it and re-import fresh.
            await _mapRepository.DeleteAsync(existingMapping, cancellationToken);
        }

        var newItem = new CatalogItem(defaultTypeId, brandId, description, name!, price, DefaultPictureName);
        newItem.UpdatePictureUri(DefaultPictureName);
        newItem = await _catalogItemRepository.AddAsync(newItem, cancellationToken);

        var mapping = new SupplierCatalogItem(supplierId, externalId, newItem.Id);
        await _mapRepository.AddAsync(mapping, cancellationToken);
    }

    private static string ResolveExternalId(ScrapedProduct product, string fallbackName)
    {
        if (!string.IsNullOrWhiteSpace(product.ExternalId))
            return product.ExternalId!.Trim();

        // Last-resort stable key when the supplier exposes neither a product URL nor an id.
        return $"name:{fallbackName}";
    }

    private async Task<int> EnsureBrandAsync(string? brandName, Dictionary<string, int> cache, CancellationToken cancellationToken)
    {
        var name = Normalize(brandName, 50) ?? DefaultBrandName;

        if (cache.TryGetValue(name, out var cachedId))
            return cachedId;

        var existing = (await _brandRepository.ListAsync(new CatalogBrandByNameSpecification(name), cancellationToken)).FirstOrDefault();
        var id = existing?.Id ?? (await _brandRepository.AddAsync(new CatalogBrand(name), cancellationToken)).Id;

        cache[name] = id;
        return id;
    }

    private async Task<int> EnsureCatalogTypeAsync(string typeName, CancellationToken cancellationToken)
    {
        var existing = (await _typeRepository.ListAsync(new CatalogTypeByNameSpecification(typeName), cancellationToken)).FirstOrDefault();
        return existing?.Id ?? (await _typeRepository.AddAsync(new CatalogType(typeName), cancellationToken)).Id;
    }

    private static string? Normalize(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed.Substring(0, maxLength);
    }
}
