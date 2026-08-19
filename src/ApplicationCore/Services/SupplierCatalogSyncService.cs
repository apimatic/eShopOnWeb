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
/// Reads a supplier's listing via <see cref="ISupplierProductReader"/> and folds the products
/// it finds into the store catalog. Matching is by the supplier's own identifier (SKU, else
/// product URL, else name) so running the same sync twice updates existing catalog items
/// rather than creating duplicates.
/// </summary>
public class SupplierCatalogSyncService : ISupplierCatalogSyncService
{
    // Catalog items require a type; supplier listings rarely carry one, so imports without a
    // category fall back to this shared type.
    private const string DefaultTypeName = "Imported";

    // The catalog Name column is capped at 50 characters (see CatalogItemConfiguration).
    private const int MaxNameLength = 50;

    // Placeholder image used by the existing catalog-item create flow; remote supplier images
    // are not downloaded here, matching the app's existing behavior.
    private const string PlaceholderPicture = "eCatalog-item-default.png";

    private readonly IRepository<CatalogSync> _syncRepository;
    private readonly IRepository<Supplier> _supplierRepository;
    private readonly IRepository<SupplierProductLink> _linkRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IRepository<CatalogBrand> _brandRepository;
    private readonly IRepository<CatalogType> _typeRepository;
    private readonly ISupplierProductReader _reader;
    private readonly IAppLogger<SupplierCatalogSyncService> _logger;

    public SupplierCatalogSyncService(
        IRepository<CatalogSync> syncRepository,
        IRepository<Supplier> supplierRepository,
        IRepository<SupplierProductLink> linkRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IRepository<CatalogBrand> brandRepository,
        IRepository<CatalogType> typeRepository,
        ISupplierProductReader reader,
        IAppLogger<SupplierCatalogSyncService> logger)
    {
        _syncRepository = syncRepository;
        _supplierRepository = supplierRepository;
        _linkRepository = linkRepository;
        _catalogItemRepository = catalogItemRepository;
        _brandRepository = brandRepository;
        _typeRepository = typeRepository;
        _reader = reader;
        _logger = logger;
    }

    public async Task ExecuteAsync(Guid syncId, CancellationToken cancellationToken = default)
    {
        var sync = await _syncRepository.GetByIdAsync(syncId, cancellationToken);
        if (sync is null)
        {
            _logger.LogWarning($"Supplier sync {syncId} not found; nothing to run.");
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
            var read = await _reader.ReadListingAsync(supplier.ListingUrl, cancellationToken);
            if (!read.Succeeded)
            {
                sync.Fail(read.Detail ?? "The supplier listing could not be read.");
                await _syncRepository.UpdateAsync(sync, cancellationToken);
                _logger.LogWarning($"Supplier sync {syncId} failed to read listing: {sync.Detail}");
                return;
            }

            var products = DistinctByExternalId(supplier, read.Products);
            int found = products.Count;
            int imported = 0;
            int skipped = 0;

            foreach (var (externalId, product) in products)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var outcome = await ImportProductAsync(supplier, externalId, product, cancellationToken);
                if (outcome)
                {
                    imported++;
                }
                else
                {
                    skipped++;
                }
            }

            string? detail = skipped > 0
                ? $"{skipped} of {found} product(s) found in the listing could not be imported (missing name/brand or an unreadable price)."
                : null;

            sync.Complete(found, imported, detail);
            await _syncRepository.UpdateAsync(sync, cancellationToken);
            _logger.LogInformation(
                $"Supplier sync {syncId} finished: status={sync.Status}, found={found}, imported={imported}.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            sync.Fail($"Unexpected error during sync: {ex.Message}");
            await _syncRepository.UpdateAsync(sync, cancellationToken);
            _logger.LogWarning($"Supplier sync {syncId} errored: {ex}");
        }
    }

    /// <summary>
    /// Collapses products that share the same supplier identifier so the found/imported tallies
    /// count real products, not repeated listing rows. The last occurrence wins.
    /// </summary>
    private static List<(string ExternalId, SupplierProduct Product)> DistinctByExternalId(
        Supplier supplier, IReadOnlyList<SupplierProduct> products)
    {
        var byKey = new Dictionary<string, SupplierProduct>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();

        foreach (var product in products)
        {
            var externalId = ResolveExternalId(supplier, product);
            if (externalId is null)
            {
                continue; // unusable: no name, sku or url to key on
            }

            if (!byKey.ContainsKey(externalId))
            {
                order.Add(externalId);
            }
            byKey[externalId] = product;
        }

        return order.Select(k => (k, byKey[k])).ToList();
    }

    /// <summary>
    /// The supplier's own stable key for a product: prefer its SKU, then its product URL, then
    /// a normalized product name. Returns null when none is available.
    /// </summary>
    private static string? ResolveExternalId(Supplier supplier, SupplierProduct product)
    {
        if (!string.IsNullOrWhiteSpace(product.Sku))
        {
            return $"sku:{product.Sku.Trim().ToLowerInvariant()}";
        }

        if (!string.IsNullOrWhiteSpace(product.Url) &&
            Uri.TryCreate(product.Url.Trim(), UriKind.Absolute, out var uri))
        {
            return $"url:{uri.AbsoluteUri.ToLowerInvariant()}";
        }

        if (!string.IsNullOrWhiteSpace(product.Name))
        {
            return $"name:{supplier.Id:N}:{product.Name.Trim().ToLowerInvariant()}";
        }

        return null;
    }

    private async Task<bool> ImportProductAsync(
        Supplier supplier, string externalId, SupplierProduct product, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(product.Name) || string.IsNullOrWhiteSpace(product.Brand))
        {
            return false;
        }

        if (!PriceParser.TryParse(product.Price, out decimal price))
        {
            _logger.LogInformation(
                $"Skipping supplier product '{product.Name}' (external id {externalId}): unreadable price '{product.Price}'.");
            return false;
        }

        var name = Truncate(product.Name.Trim(), MaxNameLength);
        var description = string.IsNullOrWhiteSpace(product.Description) ? name : product.Description.Trim();

        var brand = await GetOrCreateBrandAsync(product.Brand.Trim(), cancellationToken);
        var typeName = string.IsNullOrWhiteSpace(product.Category) ? DefaultTypeName : product.Category.Trim();
        var type = await GetOrCreateTypeAsync(typeName, cancellationToken);

        var existingLink = await _linkRepository.FirstOrDefaultAsync(
            new SupplierProductLinkSpecification(supplier.Id, externalId), cancellationToken);

        if (existingLink is not null)
        {
            var existingItem = await _catalogItemRepository.GetByIdAsync(existingLink.CatalogItemId, cancellationToken);
            if (existingItem is not null)
            {
                existingItem.UpdateDetails(new CatalogItem.CatalogItemDetails(name, description, price));
                existingItem.UpdateBrand(brand.Id);
                existingItem.UpdateType(type.Id);
                await _catalogItemRepository.UpdateAsync(existingItem, cancellationToken);

                existingLink.Touch();
                await _linkRepository.UpdateAsync(existingLink, cancellationToken);
                return true;
            }

            // The link dangled (its catalog item is gone); drop it and re-create below.
            await _linkRepository.DeleteAsync(existingLink, cancellationToken);
        }

        var newItem = new CatalogItem(type.Id, brand.Id, description, name, price, PlaceholderPicture);
        newItem = await _catalogItemRepository.AddAsync(newItem, cancellationToken);

        var link = new SupplierProductLink(supplier.Id, externalId, newItem.Id);
        await _linkRepository.AddAsync(link, cancellationToken);
        return true;
    }

    private async Task<CatalogBrand> GetOrCreateBrandAsync(string brandName, CancellationToken cancellationToken)
    {
        var existing = await _brandRepository.FirstOrDefaultAsync(
            new CatalogBrandByNameSpecification(brandName), cancellationToken);
        return existing ?? await _brandRepository.AddAsync(new CatalogBrand(brandName), cancellationToken);
    }

    private async Task<CatalogType> GetOrCreateTypeAsync(string typeName, CancellationToken cancellationToken)
    {
        var existing = await _typeRepository.FirstOrDefaultAsync(
            new CatalogTypeByNameSpecification(typeName), cancellationToken);
        return existing ?? await _typeRepository.AddAsync(new CatalogType(typeName), cancellationToken);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value.Substring(0, maxLength);
}
