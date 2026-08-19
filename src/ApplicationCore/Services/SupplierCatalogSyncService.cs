using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Firecrawl;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Reads a supplier's product listing through Firecrawl and imports the products it finds into
/// the store's own catalog. Imports are idempotent: each product is matched to the catalog by
/// the supplier's own product code, so re-running a sync updates existing items rather than
/// duplicating them.
/// </summary>
public class SupplierCatalogSyncService : ISupplierCatalogSyncService
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IRepository<CatalogSync> _syncRepository;
    private readonly IRepository<Supplier> _supplierRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IRepository<CatalogBrand> _brandRepository;
    private readonly IRepository<CatalogType> _typeRepository;
    private readonly IFirecrawlClient _firecrawlClient;
    private readonly SupplierSyncOptions _options;
    private readonly IAppLogger<SupplierCatalogSyncService> _logger;

    public SupplierCatalogSyncService(
        IRepository<CatalogSync> syncRepository,
        IRepository<Supplier> supplierRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IRepository<CatalogBrand> brandRepository,
        IRepository<CatalogType> typeRepository,
        IFirecrawlClient firecrawlClient,
        SupplierSyncOptions options,
        IAppLogger<SupplierCatalogSyncService> logger)
    {
        _syncRepository = syncRepository;
        _supplierRepository = supplierRepository;
        _catalogItemRepository = catalogItemRepository;
        _brandRepository = brandRepository;
        _typeRepository = typeRepository;
        _firecrawlClient = firecrawlClient;
        _options = options;
        _logger = logger;
    }

    public async Task ExecuteAsync(int syncId, CancellationToken cancellationToken = default)
    {
        var sync = await _syncRepository.GetByIdAsync(syncId, cancellationToken);
        if (sync is null)
        {
            _logger.LogWarning("Sync {0} not found; nothing to run.", syncId);
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

            _logger.LogInformation("Sync {0}: reading listing for supplier '{1}' at {2}.",
                sync.Id, supplier.Name, supplier.ProductListingUrl);

            var products = await ReadListingAsync(supplier.ProductListingUrl, cancellationToken);

            var (found, imported) = await ImportProductsAsync(supplier.Id, products, cancellationToken);

            sync.MarkCompleted(found, imported);
            await _syncRepository.UpdateAsync(sync, cancellationToken);

            _logger.LogInformation("Sync {0} finished with status {1}: {2} found, {3} imported.",
                sync.Id, sync.Status, found, imported);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Sync {0} failed: {1}", sync.Id, ex.Message);
            sync.MarkFailed(ex.Message, sync.ItemsFound, sync.ItemsImported);
            await _syncRepository.UpdateAsync(sync, cancellationToken);
        }
    }

    /// <summary>
    /// Drives a Firecrawl extract job against the listing and returns the products it reads.
    /// </summary>
    private async Task<IReadOnlyList<ExtractedProduct>> ReadListingAsync(
        string listingUrl, CancellationToken cancellationToken)
    {
        var schema = BuildProductSchema();
        var request = new FirecrawlExtractRequest
        {
            Urls = BuildExtractionUrls(listingUrl),
            Prompt = _options.ExtractionPrompt,
            Schema = schema
        };

        var job = await _firecrawlClient.StartExtractAsync(request, cancellationToken);
        if (!job.Success || string.IsNullOrWhiteSpace(job.Id))
        {
            throw new FirecrawlException("Firecrawl did not accept the extract request.");
        }

        var result = await PollUntilTerminalAsync(job.Id, cancellationToken);

        switch (result.Status)
        {
            case FirecrawlJobStatus.Completed:
                return ParseProducts(result.Data);
            case FirecrawlJobStatus.Failed:
            case FirecrawlJobStatus.Cancelled:
                throw new FirecrawlException($"Firecrawl extract job {job.Id} ended as {result.Status}.");
            default:
                throw new FirecrawlException(
                    $"Firecrawl extract job {job.Id} did not complete within {_options.TimeoutSeconds}s.");
        }
    }

    private async Task<FirecrawlExtractResult> PollUntilTerminalAsync(
        string jobId, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(_options.TimeoutSeconds);
        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds));

        while (true)
        {
            var result = await _firecrawlClient.GetExtractStatusAsync(jobId, cancellationToken);
            if (result.Status != FirecrawlJobStatus.Processing)
            {
                return result;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                return result; // still processing at deadline -> treated as a timeout by the caller
            }

            await Task.Delay(interval, cancellationToken);
        }
    }

    /// <summary>
    /// Imports the found products into the catalog. Returns how many distinct products were found
    /// versus how many were actually created or updated.
    /// </summary>
    private async Task<(int found, int imported)> ImportProductsAsync(
        int supplierId, IReadOnlyList<ExtractedProduct> products, CancellationToken cancellationToken)
    {
        int found = 0;
        int imported = 0;
        var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var product in products)
        {
            var code = NormalizeCode(product.Sku) ?? NormalizeCode(product.Url);

            // Skip exact duplicates of a product we've already handled in this run.
            if (code is not null && !seenCodes.Add(code))
            {
                continue;
            }

            found++;

            // Without a stable supplier identifier we cannot import idempotently, so the product
            // is counted as found but left out of the catalog.
            if (code is null)
            {
                _logger.LogWarning("Supplier {0}: skipping a product with no SKU or URL to key on.", supplierId);
                continue;
            }

            var name = product.Name?.Trim();
            var description = product.Description?.Trim();
            var price = product.PriceAmount;

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(description) ||
                !price.HasValue || price.Value <= 0)
            {
                _logger.LogWarning(
                    "Supplier {0}: product '{1}' ({2}) is missing a name, description or usable price ('{3}') and was not imported.",
                    supplierId, name ?? "(unnamed)", code, product.PriceText ?? "");
                continue;
            }

            await UpsertCatalogItemAsync(supplierId, code, name!, description!, price.Value,
                product.Brand, cancellationToken);
            imported++;
        }

        return (found, imported);
    }

    private async Task UpsertCatalogItemAsync(
        int supplierId, string code, string name, string description, decimal price,
        string? brandName, CancellationToken cancellationToken)
    {
        var brandId = await EnsureBrandIdAsync(brandName, cancellationToken);
        var typeId = await EnsureTypeIdAsync(_options.CatalogTypeName, cancellationToken);

        var existing = await _catalogItemRepository.FirstOrDefaultAsync(
            new CatalogItemBySupplierProductSpecification(supplierId, code), cancellationToken);

        if (existing is null)
        {
            var item = new CatalogItem(typeId, brandId, description, name, price, string.Empty);
            item.SetSupplierReference(supplierId, code);
            item.UpdatePictureUri(_options.DefaultPictureName);
            await _catalogItemRepository.AddAsync(item, cancellationToken);
        }
        else
        {
            existing.UpdateDetails(new CatalogItem.CatalogItemDetails(name, description, price));
            existing.UpdateBrand(brandId);
            existing.UpdateType(typeId);
            await _catalogItemRepository.UpdateAsync(existing, cancellationToken);
        }
    }

    private async Task<int> EnsureBrandIdAsync(string? brandName, CancellationToken cancellationToken)
    {
        var name = string.IsNullOrWhiteSpace(brandName) ? _options.DefaultBrandName : brandName.Trim();

        var existing = await _brandRepository.FirstOrDefaultAsync(
            new CatalogBrandByNameSpecification(name), cancellationToken);
        if (existing is not null)
        {
            return existing.Id;
        }

        var brand = await _brandRepository.AddAsync(new CatalogBrand(name), cancellationToken);
        return brand.Id;
    }

    private async Task<int> EnsureTypeIdAsync(string typeName, CancellationToken cancellationToken)
    {
        var existing = await _typeRepository.FirstOrDefaultAsync(
            new CatalogTypeByNameSpecification(typeName), cancellationToken);
        if (existing is not null)
        {
            return existing.Id;
        }

        var type = await _typeRepository.AddAsync(new CatalogType(typeName), cancellationToken);
        return type.Id;
    }

    private static string? NormalizeCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        return value.Trim();
    }

    /// <summary>
    /// The exact listing URL plus a glob over its folder, so a listing spanning several
    /// (paginated) pages is read in full.
    /// </summary>
    private static IReadOnlyList<string> BuildExtractionUrls(string listingUrl)
    {
        var urls = new List<string> { listingUrl };

        if (!listingUrl.Contains('*') && Uri.TryCreate(listingUrl, UriKind.Absolute, out var uri))
        {
            var path = uri.AbsolutePath;
            var lastSlash = path.LastIndexOf('/');
            var directory = lastSlash >= 0 ? path.Substring(0, lastSlash + 1) : "/";
            var glob = $"{uri.Scheme}://{uri.Authority}{directory}*";
            if (!urls.Contains(glob))
            {
                urls.Add(glob);
            }
        }

        return urls;
    }

    private static object BuildProductSchema()
    {
        return new
        {
            type = "object",
            properties = new
            {
                products = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            name = new { type = "string" },
                            description = new { type = "string" },
                            brand = new { type = "string" },
                            sku = new { type = "string" },
                            priceAmount = new { type = new[] { "number", "null" } },
                            priceText = new { type = "string" },
                            url = new { type = "string" }
                        },
                        required = new[] { "name", "sku" }
                    }
                }
            },
            required = new[] { "products" }
        };
    }

    private static IReadOnlyList<ExtractedProduct> ParseProducts(JsonElement? data)
    {
        if (data is null || data.Value.ValueKind != JsonValueKind.Object)
        {
            return Array.Empty<ExtractedProduct>();
        }

        var payload = data.Value.Deserialize<ExtractedCatalog>(s_jsonOptions);
        return payload?.Products ?? new List<ExtractedProduct>();
    }

    private sealed class ExtractedCatalog
    {
        [JsonPropertyName("products")]
        public List<ExtractedProduct>? Products { get; set; }
    }

    private sealed class ExtractedProduct
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("brand")]
        public string? Brand { get; set; }

        [JsonPropertyName("sku")]
        public string? Sku { get; set; }

        [JsonPropertyName("priceAmount")]
        public decimal? PriceAmount { get; set; }

        [JsonPropertyName("priceText")]
        public string? PriceText { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }
    }
}
