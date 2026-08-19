using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SupplierCatalogSyncService : ISupplierCatalogSyncService
{
    /// <summary>Catalog type assigned to every imported product (products carry no type of their own).</summary>
    private const string ImportCatalogTypeName = "Imported";

    /// <summary>Brand used when a supplier product does not specify one.</summary>
    private const string UnbrandedName = "Unbranded";

    /// <summary>Matches the max length configured for <c>CatalogItem.Name</c>.</summary>
    private const int NameMaxLength = 50;

    /// <summary>Placeholder image used for imported items, mirroring the create-catalog-item flow.</summary>
    private const string DefaultPictureName = "eCatalog-item-default.png";

    private readonly IRepository<Supplier> _supplierRepository;
    private readonly IRepository<CatalogSync> _syncRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<CatalogBrand> _brandRepository;
    private readonly IRepository<CatalogType> _typeRepository;
    private readonly IFirecrawlProductScraper _scraper;
    private readonly ISupplierSyncQueue _queue;
    private readonly IAppLogger<SupplierCatalogSyncService> _logger;
    private readonly FirecrawlSettings _settings;

    public SupplierCatalogSyncService(
        IRepository<Supplier> supplierRepository,
        IRepository<CatalogSync> syncRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<CatalogBrand> brandRepository,
        IRepository<CatalogType> typeRepository,
        IFirecrawlProductScraper scraper,
        ISupplierSyncQueue queue,
        IAppLogger<SupplierCatalogSyncService> logger,
        IOptions<FirecrawlSettings> settings)
    {
        _supplierRepository = supplierRepository;
        _syncRepository = syncRepository;
        _itemRepository = itemRepository;
        _brandRepository = brandRepository;
        _typeRepository = typeRepository;
        _scraper = scraper;
        _queue = queue;
        _logger = logger;
        _settings = settings.Value;
    }

    public async Task<Supplier> RegisterSupplierAsync(string name, string listingUrl, CancellationToken cancellationToken = default)
    {
        var supplier = new Supplier(name, listingUrl);
        await _supplierRepository.AddAsync(supplier, cancellationToken);
        _logger.LogInformation("Registered supplier {0} (id {1}) with listing {2}", supplier.Name, supplier.Id, supplier.ListingUrl);
        return supplier;
    }

    public async Task<CatalogSync?> StartSyncAsync(int supplierId, CancellationToken cancellationToken = default)
    {
        var supplier = await _supplierRepository.GetByIdAsync(supplierId, cancellationToken);
        if (supplier == null)
        {
            return null;
        }

        var sync = new CatalogSync(supplier.Id);
        await _syncRepository.AddAsync(sync, cancellationToken);

        await _queue.EnqueueAsync(sync.Id, cancellationToken);
        _logger.LogInformation("Queued sync {0} for supplier {1}", sync.Id, supplier.Id);
        return sync;
    }

    public Task<CatalogSync?> GetSyncAsync(int syncId, CancellationToken cancellationToken = default)
        => _syncRepository.GetByIdAsync(syncId, cancellationToken);

    public async Task RunSyncAsync(int syncId, CancellationToken cancellationToken = default)
    {
        var sync = await _syncRepository.GetByIdAsync(syncId, cancellationToken);
        if (sync == null)
        {
            _logger.LogWarning("Sync {0} not found; skipping.", syncId);
            return;
        }

        try
        {
            sync.MarkRunning();
            await _syncRepository.UpdateAsync(sync, cancellationToken);

            var supplier = await _supplierRepository.GetByIdAsync(sync.SupplierId, cancellationToken);
            if (supplier == null)
            {
                sync.Fail($"Supplier {sync.SupplierId} no longer exists.");
                await _syncRepository.UpdateAsync(sync, cancellationToken);
                return;
            }

            var jobId = await _scraper.StartExtractionAsync(supplier.ListingUrl, cancellationToken);
            sync.SetExternalJob(jobId);
            await _syncRepository.UpdateAsync(sync, cancellationToken);

            var extraction = await PollUntilFinishedAsync(jobId, cancellationToken);
            if (extraction.State != ExtractionState.Completed)
            {
                sync.Fail($"Firecrawl extraction {extraction.State.ToString().ToLowerInvariant()}"
                          + (extraction.ErrorMessage is null ? "." : $": {extraction.ErrorMessage}"));
                await _syncRepository.UpdateAsync(sync, cancellationToken);
                return;
            }

            var (found, imported) = await ImportProductsAsync(supplier, extraction.Products, cancellationToken);
            sync.Complete(found, imported);
            await _syncRepository.UpdateAsync(sync, cancellationToken);
            _logger.LogInformation("Sync {0} finished: found {1}, imported {2} ({3}).", sync.Id, found, imported, sync.Status);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Sync {0} failed: {1}", syncId, ex.Message);
            try
            {
                sync.Fail(ex.Message);
                await _syncRepository.UpdateAsync(sync, cancellationToken);
            }
            catch (Exception updateEx)
            {
                _logger.LogWarning("Could not persist failure state for sync {0}: {1}", syncId, updateEx.Message);
            }
        }
    }

    private async Task<ProductExtractionResult> PollUntilFinishedAsync(string jobId, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, _settings.PollTimeoutSeconds));
        var interval = TimeSpan.FromSeconds(Math.Max(1, _settings.PollIntervalSeconds));

        while (true)
        {
            var result = await _scraper.GetExtractionAsync(jobId, cancellationToken);
            if (result.IsFinished)
            {
                return result;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                return new ProductExtractionResult(ExtractionState.Failed, Array.Empty<ScrapedProduct>(),
                    $"Timed out after {_settings.PollTimeoutSeconds}s waiting for extraction to finish.");
            }

            await Task.Delay(interval, cancellationToken);
        }
    }

    private async Task<(int found, int imported)> ImportProductsAsync(
        Supplier supplier, IReadOnlyList<ScrapedProduct> products, CancellationToken cancellationToken)
    {
        int found = products.Count;
        int imported = 0;

        var typeId = await ResolveImportTypeIdAsync(cancellationToken);
        var brandCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var product in products)
        {
            var name = product.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                _logger.LogWarning("Skipping a product from supplier {0} with no name.", supplier.Id);
                continue;
            }

            if (product.Price is not { } price || price <= 0)
            {
                _logger.LogWarning("Skipping product '{0}' from supplier {1}: no usable price.", name, supplier.Id);
                continue;
            }

            var supplierItemKey = !string.IsNullOrWhiteSpace(product.ExternalId) ? product.ExternalId!.Trim() : name;
            var description = string.IsNullOrWhiteSpace(product.Description) ? name : product.Description!.Trim();
            var brandName = string.IsNullOrWhiteSpace(product.Brand) ? UnbrandedName : product.Brand!.Trim();
            var brandId = await ResolveBrandIdAsync(brandName, brandCache, cancellationToken);
            var storedName = Truncate(name, NameMaxLength);

            var existing = await _itemRepository.FirstOrDefaultAsync(
                new CatalogItemBySupplierItemKeySpecification(supplier.Id, supplierItemKey), cancellationToken);

            if (existing == null)
            {
                var item = new CatalogItem(typeId, brandId, description, storedName, price, string.Empty);
                item.SetSupplierReference(supplier.Id, supplierItemKey);
                item.UpdatePictureUri(DefaultPictureName);
                await _itemRepository.AddAsync(item, cancellationToken);
            }
            else
            {
                existing.UpdateDetails(new CatalogItem.CatalogItemDetails(storedName, description, price));
                existing.UpdateBrand(brandId);
                existing.UpdateType(typeId);
                await _itemRepository.UpdateAsync(existing, cancellationToken);
            }

            imported++;
        }

        return (found, imported);
    }

    private async Task<int> ResolveImportTypeIdAsync(CancellationToken cancellationToken)
    {
        var existing = await _typeRepository.FirstOrDefaultAsync(
            new CatalogTypeByNameSpecification(ImportCatalogTypeName), cancellationToken);
        if (existing != null)
        {
            return existing.Id;
        }

        var created = await _typeRepository.AddAsync(new CatalogType(ImportCatalogTypeName), cancellationToken);
        return created.Id;
    }

    private async Task<int> ResolveBrandIdAsync(string brandName, IDictionary<string, int> cache, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(brandName, out var cachedId))
        {
            return cachedId;
        }

        var existing = await _brandRepository.FirstOrDefaultAsync(
            new CatalogBrandByNameSpecification(brandName), cancellationToken);

        int id = existing?.Id
                 ?? (await _brandRepository.AddAsync(new CatalogBrand(brandName), cancellationToken)).Id;

        cache[brandName] = id;
        return id;
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value.Substring(0, maxLength);
}
