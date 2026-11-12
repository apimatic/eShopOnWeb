using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.ProductPricePoints;
using MaxioAdvancedBilling.Requests.Products;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using SdkProduct = MaxioAdvancedBilling.Models.Product;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>A plan the shop offers: a Maxio product that lives in the product family of one of the catalog brands.</summary>
public record OfferedPlan(
    int Id,
    string Name,
    string Handle,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    int CatalogBrandId,
    string CatalogBrandName,
    int ProductFamilyId,
    int? PricePointId);

/// <summary>
/// The plans this application offers. Maxio is the system of record: a plan is a Maxio product whose product family
/// is the family of a catalog brand (handle <c>catalog-brand-{brandId}</c>). Everything a caller may subscribe to or
/// switch to must be one of these.
/// </summary>
public class PlanCatalog
{
    private const int PageSize = 200;
    private const int MaxPages = 50;
    private static readonly Regex BrandFamilyHandle = new(@"^catalog-brand-(\d+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly IReadRepository<CatalogBrand> _brands;
    private readonly ILogger<PlanCatalog> _logger;

    public PlanCatalog(MaxioAdvancedBillingClient client, IReadRepository<CatalogBrand> brands, ILogger<PlanCatalog> logger)
    {
        _client = client;
        _brands = brands;
        _logger = logger;
    }

    public static string FamilyHandle(int brandId) => $"catalog-brand-{brandId}";

    /// <summary>All plans a shopper can subscribe to.</summary>
    public async Task<IReadOnlyList<OfferedPlan>> ListOfferedAsync(CancellationToken ct = default)
    {
        var brands = (await _brands.ListAsync(ct)).ToDictionary(b => b.Id);
        IReadOnlyList<ProductFamilyResponse> families;
        try
        {
            families = await _client.ProductFamilies.ListProductFamilies(new ListProductFamiliesRequest(), cancellationToken: ct);
        }
        catch (Exception ex) when (ex is SdkException)
        {
            throw MaxioErrors.Map(ex, "list the plans");
        }

        var plans = new List<OfferedPlan>();
        foreach (var family in families.Select(f => f.ProductFamily).Where(f => f is { Id: not null }))
        {
            var match = BrandFamilyHandle.Match(family!.Handle ?? string.Empty);
            if (!match.Success || family.ArchivedAt is not null) continue;
            if (!brands.TryGetValue(int.Parse(match.Groups[1].Value), out var brand)) continue;

            foreach (var product in await ListProductsAsync(family.Id!.Value, ct))
            {
                var plan = ToPlan(product, brand);
                if (plan is not null) plans.Add(plan);
            }
        }

        return plans.OrderBy(p => p.CatalogBrandId).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Id).ToList();
    }

    /// <summary>The offered plan with this handle, or null when the shop does not offer such a plan.</summary>
    public async Task<OfferedPlan?> FindByHandleAsync(string handle, CancellationToken ct = default)
    {
        handle = handle.Trim();
        try
        {
            var response = await _client.Products.ReadProductByHandle(new ReadProductByHandleRequest { ApiHandle = handle }, cancellationToken: ct);
            return await ToOfferedAsync(response.Product, ct);
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex) when (ex is SdkException)
        {
            throw MaxioErrors.Map(ex, "read the plan");
        }
    }

    /// <summary>The offered plan with this id, or null when the shop does not offer such a plan.</summary>
    public async Task<OfferedPlan?> FindByIdAsync(int planId, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.Products.ReadProduct(new ReadProductRequest { ProductId = planId }, cancellationToken: ct);
            return await ToOfferedAsync(response.Product, ct);
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex) when (ex is SdkException)
        {
            throw MaxioErrors.Map(ex, "read the plan");
        }
    }

    /// <summary>Creates a monthly plan in the product family of the given catalog brand.</summary>
    public async Task<OfferedPlan> CreateAsync(string name, long priceInCents, int catalogBrandId, CancellationToken ct = default)
    {
        var brand = await _brands.GetByIdAsync(catalogBrandId, ct)
            ?? throw MaxioProblemException.NotFound($"Catalog brand {catalogBrandId} does not exist.");
        var familyId = await EnsureFamilyAsync(brand, ct);

        var slug = Slugify(name);
        foreach (var handle in new[] { slug, $"{slug}-{brand.Id}" })
        {
            try
            {
                var created = await _client.Products.CreateProduct(new CreateProductRequest
                {
                    ProductFamilyId = familyId.ToString(),
                    Body = new CreateOrUpdateProductRequest
                    {
                        Product = new CreateOrUpdateProduct
                        {
                            Name = name,
                            Handle = handle,
                            Description = name,
                            PriceInCents = priceInCents,
                            Interval = 1,
                            IntervalUnit = IntervalUnit.Month,
                            RequireCreditCard = false,
                        }
                    }
                }, cancellationToken: ct);
                return ToPlan(created.Product, brand)
                    ?? throw new MaxioProblemException(HttpStatusCode.BadGateway, "Maxio created the plan but answered without its details.");
            }
            catch (ApiException<CreateProductError> ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity
                && handle == slug
                && MaxioErrors.Describe(ex).Contains("handle", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Plan handle {Handle} is taken, retrying with the brand suffix", handle);
            }
            catch (ApiException<CreateProductError> ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity
                && MaxioErrors.Describe(ex).Contains("handle", StringComparison.OrdinalIgnoreCase))
            {
                throw MaxioProblemException.Conflict($"A plan named '{name}' already exists for this brand.");
            }
            catch (Exception ex) when (ex is SdkException)
            {
                throw MaxioErrors.Map(ex, "create the plan", writeMayHaveLanded: true);
            }
        }
        throw MaxioProblemException.Conflict($"A plan named '{name}' already exists.");
    }

    /// <summary>Prices an offered plan in another currency (the plan's baseline price in that currency).</summary>
    public async Task<IReadOnlyList<CurrencyPrice>> AddCurrencyPriceAsync(int planId, string currency, int price, CancellationToken ct = default)
    {
        var plan = await FindByIdAsync(planId, ct)
            ?? throw MaxioProblemException.NotFound($"There is no plan with id {planId}.");
        var pricePointId = plan.PricePointId
            ?? throw new MaxioProblemException(HttpStatusCode.BadGateway, "Maxio did not report the plan's price point.");
        try
        {
            var response = await _client.ProductPricePoints.CreateProductCurrencyPrices(new CreateProductCurrencyPricesOperationRequest
            {
                ProductPricePointId = pricePointId,
                Body = new CreateProductCurrencyPricesRequest
                {
                    CurrencyPrices = new[] { new CreateProductCurrencyPrice { Currency = currency, Price = price, Role = CurrencyPriceRole.Baseline } }
                }
            }, cancellationToken: ct);
            return response.CurrencyPrices;
        }
        catch (Exception ex) when (ex is SdkException)
        {
            throw MaxioErrors.Map(ex, "price the plan in that currency", writeMayHaveLanded: true);
        }
    }

    private async Task<int> EnsureFamilyAsync(CatalogBrand brand, CancellationToken ct)
    {
        var handle = FamilyHandle(brand.Id);
        var existing = await FindFamilyAsync(handle, ct);
        if (existing is not null) return existing.Value;

        foreach (var familyName in new[] { brand.Brand, $"{brand.Brand} (catalog brand {brand.Id})" })
        {
            try
            {
                var created = await _client.ProductFamilies.CreateProductFamily(new CreateProductFamilyOperationRequest
                {
                    Body = new CreateProductFamilyRequest
                    {
                        ProductFamily = new CreateProductFamily { Name = familyName, Handle = handle, Description = $"Plans of the {brand.Brand} catalog brand" }
                    }
                }, cancellationToken: ct);
                return created.ProductFamily?.Id ?? throw new MaxioProblemException(HttpStatusCode.BadGateway, "Maxio created the product family but answered without its id.");
            }
            catch (ApiException<CreateProductFamilyError> ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                // Someone may have created it concurrently, or the name is taken: look again before giving up.
                existing = await FindFamilyAsync(handle, ct);
                if (existing is not null) return existing.Value;
                _logger.LogInformation("Product family creation for brand {BrandId} refused: {Reason}", brand.Id, MaxioErrors.Describe(ex));
            }
            catch (Exception ex) when (ex is SdkException)
            {
                throw MaxioErrors.Map(ex, "create the brand's product family", writeMayHaveLanded: true);
            }
        }
        throw MaxioProblemException.Conflict($"Could not create the product family of catalog brand {brand.Id}.");
    }

    private async Task<int?> FindFamilyAsync(string handle, CancellationToken ct)
    {
        try
        {
            var families = await _client.ProductFamilies.ListProductFamilies(new ListProductFamiliesRequest(), cancellationToken: ct);
            return families.Select(f => f.ProductFamily)
                .FirstOrDefault(f => f is not null && f.ArchivedAt is null && string.Equals(f.Handle, handle, StringComparison.Ordinal))?.Id;
        }
        catch (Exception ex) when (ex is SdkException)
        {
            throw MaxioErrors.Map(ex, "list the product families");
        }
    }

    private async Task<List<SdkProduct>> ListProductsAsync(int familyId, CancellationToken ct)
    {
        var products = new List<SdkProduct>();
        for (var page = 1; page <= MaxPages; page++)
        {
            IReadOnlyList<ProductResponse> batch;
            try
            {
                batch = await _client.ProductFamilies.ListProductsForProductFamily(
                    new ListProductsForProductFamilyRequest { ProductFamilyId = familyId.ToString(), Page = page, PerPage = PageSize },
                    cancellationToken: ct);
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return products;
            }
            catch (Exception ex) when (ex is SdkException)
            {
                throw MaxioErrors.Map(ex, "list the plans");
            }

            products.AddRange(batch.Select(b => b.Product));
            if (batch.Count < PageSize) return products;
        }
        throw new MaxioIncompleteResultException("plan list");
    }

    private async Task<OfferedPlan?> ToOfferedAsync(SdkProduct product, CancellationToken ct)
    {
        var match = BrandFamilyHandle.Match(product.ProductFamily?.Handle ?? string.Empty);
        if (!match.Success || product.ProductFamily?.ArchivedAt is not null) return null;
        var brand = await _brands.GetByIdAsync(int.Parse(match.Groups[1].Value), ct);
        return brand is null ? null : ToPlan(product, brand);
    }

    private static OfferedPlan? ToPlan(SdkProduct product, CatalogBrand brand)
    {
        if (product.Id is null || product.ArchivedAt is not null || string.IsNullOrWhiteSpace(product.Handle)) return null;
        return new OfferedPlan(
            product.Id.Value,
            product.Name ?? product.Handle,
            product.Handle,
            product.Description,
            product.PriceInCents ?? 0,
            product.Interval ?? 1,
            product.IntervalUnit is null ? "month" : product.IntervalUnit.Match(() => "day", () => "month", other => other),
            brand.Id,
            brand.Brand,
            product.ProductFamily?.Id ?? 0,
            product.DefaultProductPricePointId ?? product.ProductPricePointId);
    }

    internal static string Slugify(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name.Trim().ToLowerInvariant())
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        var slug = sb.ToString().Trim('-');
        if (slug.Length > 60) slug = slug[..60].Trim('-');
        return slug.Length == 0 ? "plan" : slug;
    }
}
