using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SupplierCatalogSyncService : ISupplierCatalogSyncService
{
    // The catalog's Name column is capped at 50 chars; the Brand column at 100.
    private const int MaxNameLength = 50;
    private const int MaxBrandLength = 100;

    // Imported products carry a brand but not a catalog "type"; they are filed under one type.
    private const string ImportedTypeName = "Supplier Import";
    private const string UnbrandedFallback = "Unbranded";

    private readonly IRepository<Supplier> _supplierRepository;
    private readonly IRepository<SupplierSync> _syncRepository;
    private readonly IRepository<SupplierCatalogItem> _mappingRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IRepository<CatalogBrand> _brandRepository;
    private readonly IRepository<CatalogType> _typeRepository;
    private readonly ISupplierProductReader _productReader;
    private readonly ISupplierSyncQueue _syncQueue;
    private readonly IAppLogger<SupplierCatalogSyncService> _logger;

    public SupplierCatalogSyncService(
        IRepository<Supplier> supplierRepository,
        IRepository<SupplierSync> syncRepository,
        IRepository<SupplierCatalogItem> mappingRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IRepository<CatalogBrand> brandRepository,
        IRepository<CatalogType> typeRepository,
        ISupplierProductReader productReader,
        ISupplierSyncQueue syncQueue,
        IAppLogger<SupplierCatalogSyncService> logger)
    {
        _supplierRepository = supplierRepository;
        _syncRepository = syncRepository;
        _mappingRepository = mappingRepository;
        _catalogItemRepository = catalogItemRepository;
        _brandRepository = brandRepository;
        _typeRepository = typeRepository;
        _productReader = productReader;
        _syncQueue = syncQueue;
        _logger = logger;
    }

    public async Task<Supplier> RegisterSupplierAsync(string name, string productListingUrl, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(name, nameof(name));
        Guard.Against.NullOrWhiteSpace(productListingUrl, nameof(productListingUrl));

        var supplier = new Supplier(name.Trim(), productListingUrl.Trim());
        supplier = await _supplierRepository.AddAsync(supplier, cancellationToken);
        _logger.LogInformation("Registered supplier {0} (id {1}) with listing {2}", supplier.Name, supplier.Id, supplier.ProductListingUrl);
        return supplier;
    }

    public Task<Supplier?> GetSupplierAsync(int supplierId, CancellationToken cancellationToken = default) =>
        _supplierRepository.GetByIdAsync(supplierId, cancellationToken);

    public async Task<SupplierSync> StartSyncAsync(int supplierId, CancellationToken cancellationToken = default)
    {
        var supplier = await _supplierRepository.GetByIdAsync(supplierId, cancellationToken);
        if (supplier is null)
        {
            throw new SupplierNotFoundException(supplierId);
        }

        var sync = new SupplierSync(supplier.Id);
        sync = await _syncRepository.AddAsync(sync, cancellationToken);

        await _syncQueue.EnqueueAsync(sync.Id, cancellationToken);
        _logger.LogInformation("Queued sync {0} for supplier {1}", sync.Id, supplier.Id);
        return sync;
    }

    public Task<SupplierSync?> GetSyncAsync(int syncId, CancellationToken cancellationToken = default) =>
        _syncRepository.GetByIdAsync(syncId, cancellationToken);

    public async Task RunSyncAsync(int syncId, CancellationToken cancellationToken = default)
    {
        var sync = await _syncRepository.GetByIdAsync(syncId, cancellationToken);
        if (sync is null)
        {
            _logger.LogWarning("Sync {0} was requested but no longer exists; skipping.", syncId);
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

            _logger.LogInformation("Sync {0}: reading listing {1}", sync.Id, supplier.ProductListingUrl);
            var readResult = await _productReader.ReadProductsAsync(supplier.ProductListingUrl, cancellationToken);

            // Distinct by the supplier's own product key so a listing that repeats a product does
            // not inflate the found/imported counts or churn the same catalog item twice.
            var products = readResult.Products
                .Where(p => !string.IsNullOrWhiteSpace(p.ExternalId))
                .GroupBy(p => p.ExternalId.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            int found = products.Count;
            int imported = 0;

            foreach (var product in products)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await ImportProductAsync(supplier.Id, product, cancellationToken))
                {
                    imported++;
                }
            }

            sync.MarkFinished(found, imported, readResult.ListingFullyCaptured);
            await _syncRepository.UpdateAsync(sync, cancellationToken);
            _logger.LogInformation("Sync {0} finished: status {1}, found {2}, imported {3}", sync.Id, sync.Status, found, imported);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Sync {0} failed: {1}", sync.Id, ex.Message);
            try
            {
                sync.MarkFailed(ex.Message);
                await _syncRepository.UpdateAsync(sync, CancellationToken.None);
            }
            catch (Exception updateEx)
            {
                _logger.LogWarning("Sync {0}: could not record failure: {1}", sync.Id, updateEx.Message);
            }
        }
    }

    /// <summary>
    /// Creates or updates the catalog item for one supplier product. Returns true when the product
    /// was imported, false when it was skipped for missing required data (name or a positive price).
    /// </summary>
    private async Task<bool> ImportProductAsync(int supplierId, SupplierProduct product, CancellationToken cancellationToken)
    {
        var name = Truncate(product.Name?.Trim(), MaxNameLength);
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        if (product.Price is not { } price || price <= 0m)
        {
            return false;
        }

        var description = string.IsNullOrWhiteSpace(product.Description) ? name : product.Description!.Trim();
        var pictureUri = product.ImageUrl?.Trim() ?? string.Empty;
        int brandId = await ResolveBrandIdAsync(product.Brand, cancellationToken);
        int typeId = await ResolveTypeIdAsync(cancellationToken);
        var key = product.ExternalId.Trim();

        var existingMapping = await _mappingRepository.FirstOrDefaultAsync(
            new SupplierCatalogItemByKeySpecification(supplierId, key), cancellationToken);

        if (existingMapping is not null)
        {
            var item = await _catalogItemRepository.GetByIdAsync(existingMapping.CatalogItemId, cancellationToken);
            if (item is null)
            {
                // The mapped catalog item was removed out-of-band; recreate it and repoint the mapping.
                var recreated = new CatalogItem(typeId, brandId, description, name, price, pictureUri);
                recreated = await _catalogItemRepository.AddAsync(recreated, cancellationToken);
                existingMapping.RepointTo(recreated.Id);
            }
            else
            {
                item.UpdateDetails(new CatalogItem.CatalogItemDetails(name, description, price));
                item.UpdateBrand(brandId);
                item.UpdateType(typeId);
                await _catalogItemRepository.UpdateAsync(item, cancellationToken);
            }

            existingMapping.MarkSynced();
            await _mappingRepository.UpdateAsync(existingMapping, cancellationToken);
            return true;
        }

        var newItem = new CatalogItem(typeId, brandId, description, name, price, pictureUri);
        newItem = await _catalogItemRepository.AddAsync(newItem, cancellationToken);

        var mapping = new SupplierCatalogItem(supplierId, key, newItem.Id);
        await _mappingRepository.AddAsync(mapping, cancellationToken);
        return true;
    }

    private async Task<int> ResolveBrandIdAsync(string? brandName, CancellationToken cancellationToken)
    {
        var name = Truncate(brandName?.Trim(), MaxBrandLength);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = UnbrandedFallback;
        }

        var existing = await _brandRepository.FirstOrDefaultAsync(new CatalogBrandByNameSpecification(name), cancellationToken);
        if (existing is not null)
        {
            return existing.Id;
        }

        var brand = await _brandRepository.AddAsync(new CatalogBrand(name), cancellationToken);
        return brand.Id;
    }

    private async Task<int> ResolveTypeIdAsync(CancellationToken cancellationToken)
    {
        var existing = await _typeRepository.FirstOrDefaultAsync(new CatalogTypeByNameSpecification(ImportedTypeName), cancellationToken);
        if (existing is not null)
        {
            return existing.Id;
        }

        var type = await _typeRepository.AddAsync(new CatalogType(ImportedTypeName), cancellationToken);
        return type.Id;
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
