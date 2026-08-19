using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Runs one supplier sync: reads the supplier's listing via Firecrawl and upserts every product
/// found into the catalog. Matching is by (supplier, supplier product key) so re-running a sync
/// updates existing items instead of duplicating them.
/// </summary>
public class SupplierCatalogImporter : ISupplierCatalogImporter
{
    // Catalog items carry a required brand/type foreign key; imported products that don't name one
    // fall back to these, which are created on first use.
    private const string DefaultBrandName = "Unbranded";
    private const string DefaultTypeName = "Imported";
    private const int NameMaxLength = 50;   // matches CatalogItem.Name column
    private const int LabelMaxLength = 100; // matches CatalogBrand.Brand / CatalogType.Type columns

    private readonly IRepository<SupplierSync> _syncRepository;
    private readonly IRepository<Supplier> _supplierRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IRepository<CatalogBrand> _brandRepository;
    private readonly IRepository<CatalogType> _typeRepository;
    private readonly IFirecrawlClient _firecrawlClient;
    private readonly IAppLogger<SupplierCatalogImporter> _logger;

    public SupplierCatalogImporter(
        IRepository<SupplierSync> syncRepository,
        IRepository<Supplier> supplierRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IRepository<CatalogBrand> brandRepository,
        IRepository<CatalogType> typeRepository,
        IFirecrawlClient firecrawlClient,
        IAppLogger<SupplierCatalogImporter> logger)
    {
        _syncRepository = syncRepository;
        _supplierRepository = supplierRepository;
        _catalogItemRepository = catalogItemRepository;
        _brandRepository = brandRepository;
        _typeRepository = typeRepository;
        _firecrawlClient = firecrawlClient;
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

        var supplier = await _supplierRepository.GetByIdAsync(sync.SupplierId, cancellationToken);
        if (supplier is null)
        {
            sync.Fail($"Supplier {sync.SupplierId} no longer exists.");
            await _syncRepository.UpdateAsync(sync, cancellationToken);
            return;
        }

        sync.MarkRunning();
        await _syncRepository.UpdateAsync(sync, cancellationToken);

        var itemsFound = 0;
        var itemsImported = 0;
        try
        {
            var products = await _firecrawlClient.ScrapeProductsAsync(supplier.ProductListingUrl, cancellationToken);

            // Only products with a name are meaningful to import.
            var namedProducts = products.Where(p => !string.IsNullOrWhiteSpace(p.Name)).ToList();
            itemsFound = namedProducts.Count;

            var defaultTypeId = await GetOrCreateTypeIdAsync(DefaultTypeName, cancellationToken);

            foreach (var product in namedProducts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await UpsertProductAsync(supplier.Id, product, defaultTypeId, cancellationToken);
                    itemsImported++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Sync {syncId}: failed to import product '{product.Name}' from supplier {supplier.Id}: {ex.Message}");
                }
            }

            sync.Complete(itemsFound, itemsImported);
            await _syncRepository.UpdateAsync(sync, cancellationToken);
            _logger.LogInformation($"Sync {syncId} finished with status {sync.Status}: {itemsImported}/{itemsFound} imported.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Sync {syncId} failed for supplier {supplier.Id}: {ex.Message}");
            sync.Fail(ex.Message, itemsFound, itemsImported);
            await _syncRepository.UpdateAsync(sync, cancellationToken);
        }
    }

    private async Task UpsertProductAsync(int supplierId, ScrapedProduct product, int defaultTypeId, CancellationToken cancellationToken)
    {
        var key = BuildProductKey(product);
        var name = Truncate(product.Name!.Trim(), NameMaxLength);
        var price = ParsePrice(product.Price);
        var brandId = await GetOrCreateBrandIdAsync(product.Brand, cancellationToken);

        var existing = await _catalogItemRepository.FirstOrDefaultAsync(
            new CatalogItemBySupplierKeySpecification(supplierId, key), cancellationToken);

        if (existing is null)
        {
            var item = new CatalogItem(defaultTypeId, brandId, product.Description ?? string.Empty, name, price, string.Empty);
            item.AssignSupplierSource(supplierId, key);
            item.UpdatePictureUri("eCatalog-item-default.png");
            await _catalogItemRepository.AddAsync(item, cancellationToken);
        }
        else
        {
            existing.UpdateImportedDetails(name, product.Description, price);
            existing.UpdateBrand(brandId);
            existing.AssignSupplierSource(supplierId, key); // idempotent; keeps the match stable
            await _catalogItemRepository.UpdateAsync(existing, cancellationToken);
        }
    }

    /// <summary>
    /// The stable idempotency key for a product: the supplier's SKU, else its product URL, else its name.
    /// </summary>
    private static string BuildProductKey(ScrapedProduct product)
    {
        if (!string.IsNullOrWhiteSpace(product.Sku)) return product.Sku.Trim();
        if (!string.IsNullOrWhiteSpace(product.ProductUrl)) return product.ProductUrl.Trim();
        return product.Name!.Trim();
    }

    private async Task<int> GetOrCreateBrandIdAsync(string? brandName, CancellationToken cancellationToken)
    {
        var name = Truncate(string.IsNullOrWhiteSpace(brandName) ? DefaultBrandName : brandName.Trim(), LabelMaxLength);
        var existing = await _brandRepository.FirstOrDefaultAsync(new CatalogBrandByNameSpecification(name), cancellationToken);
        if (existing is not null) return existing.Id;

        var created = await _brandRepository.AddAsync(new CatalogBrand(name), cancellationToken);
        return created.Id;
    }

    private async Task<int> GetOrCreateTypeIdAsync(string typeName, CancellationToken cancellationToken)
    {
        var name = Truncate(typeName.Trim(), LabelMaxLength);
        var existing = await _typeRepository.FirstOrDefaultAsync(new CatalogTypeByNameSpecification(name), cancellationToken);
        if (existing is not null) return existing.Id;

        var created = await _typeRepository.AddAsync(new CatalogType(name), cancellationToken);
        return created.Id;
    }

    /// <summary>
    /// Parses a price as it appeared on a page (e.g. "$1,299.99", "€19,99") into a decimal.
    /// Returns 0 when no number can be read, so the product is still imported.
    /// </summary>
    internal static decimal ParsePrice(string? rawPrice)
    {
        if (string.IsNullOrWhiteSpace(rawPrice)) return 0m;

        // Keep only digits and separators, then normalise to an invariant decimal.
        var cleaned = Regex.Replace(rawPrice, @"[^\d.,-]", string.Empty);
        if (string.IsNullOrEmpty(cleaned)) return 0m;

        var hasComma = cleaned.Contains(',');
        var hasDot = cleaned.Contains('.');

        if (hasComma && hasDot)
        {
            // The right-most separator is the decimal point; the other groups thousands.
            if (cleaned.LastIndexOf(',') > cleaned.LastIndexOf('.'))
            {
                cleaned = cleaned.Replace(".", string.Empty).Replace(',', '.'); // e.g. 1.299,99 -> 1299.99
            }
            else
            {
                cleaned = cleaned.Replace(",", string.Empty); // e.g. 1,299.99 -> 1299.99
            }
        }
        else if (hasComma)
        {
            // A lone comma is a decimal separator only when it looks like one (1-2 trailing digits).
            var idx = cleaned.LastIndexOf(',');
            var decimals = cleaned.Length - idx - 1;
            cleaned = decimals is 1 or 2
                ? cleaned.Replace(',', '.')
                : cleaned.Replace(",", string.Empty);
        }

        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) && value >= 0
            ? value
            : 0m;
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value.Substring(0, maxLength);
}
