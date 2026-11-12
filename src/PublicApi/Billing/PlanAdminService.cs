using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.ProductPricePoints;
using MaxioAdvancedBilling.Requests.Products;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Staff operations on plans (Maxio products).
/// </summary>
public class PlanAdminService
{
    private readonly MaxioClientProvider _clientProvider;
    private readonly MaxioCall _call;
    private readonly PlanCatalog _plans;
    private readonly IReadRepository<CatalogBrand> _brands;

    public PlanAdminService(MaxioClientProvider clientProvider, MaxioCall call, PlanCatalog plans, IReadRepository<CatalogBrand> brands)
    {
        _clientProvider = clientProvider;
        _call = call;
        _plans = plans;
        _brands = brands;
    }

    private MaxioAdvancedBillingClient Client => _clientProvider.GetClient();

    // ---- new plan -------------------------------------------------------------------------------

    public async Task<PlanDto> CreatePlanAsync(string? name, decimal? price, string? brandName, int? brandId, string? description,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name)) throw BillingException.BadRequest("A plan name is required.");
        name = name.Trim();
        if (name.Length > 255) throw BillingException.BadRequest("The plan name is too long (255 characters at most).");
        if (price == null) throw BillingException.BadRequest("A monthly price is required.");
        if (price < 0m) throw BillingException.BadRequest("The monthly price cannot be negative.");
        var cents = price.Value * 100m;
        if (cents != decimal.Truncate(cents)) throw BillingException.BadRequest("The monthly price can have at most two decimals.");
        if (cents > int.MaxValue) throw BillingException.BadRequest("The monthly price is too large.");

        var brand = await ResolveBrandAsync(brandName, brandId, ct);

        var handle = Slugify(name);
        if (handle.Length == 0) throw BillingException.BadRequest("The plan name must contain letters or digits.");
        var existing = (await _plans.ListAllProductsAsync(ct))
            .FirstOrDefault(p => string.Equals(p.Handle, handle, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            throw BillingException.Conflict($"A plan with handle '{handle}' already exists (plan {existing.Id}).");
        }

        var family = await FindOrCreateFamilyAsync(brand, ct);
        var created = await _call.RunAsync(() => Client.Products.CreateProduct(
            new CreateProductRequest
            {
                ProductFamilyId = family.Id!.Value.ToString(CultureInfo.InvariantCulture),
                Body = new CreateOrUpdateProductRequest
                {
                    Product = new CreateOrUpdateProduct
                    {
                        Name = name,
                        Handle = handle,
                        Description = string.IsNullOrWhiteSpace(description) ? name : description.Trim(),
                        PriceInCents = (long)cents,
                        Interval = 1,
                        IntervalUnit = IntervalUnit.Month,
                        // Shoppers subscribe without card details.
                        RequireCreditCard = false,
                    }
                }
            }, cancellationToken: ct));

        var dto = PlanCatalog.ToDto(created.Product);
        dto.Brand ??= family.Name;
        return dto;
    }

    private async Task<string> ResolveBrandAsync(string? brandName, int? brandId, CancellationToken ct)
    {
        var brands = await _brands.ListAsync(ct);
        CatalogBrand? match = null;
        if (brandId != null)
        {
            match = brands.FirstOrDefault(b => b.Id == brandId);
        }
        else if (!string.IsNullOrWhiteSpace(brandName))
        {
            match = brands.FirstOrDefault(b => string.Equals(b.Brand, brandName.Trim(), StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            throw BillingException.BadRequest("A brand is required (brand name or brandId).");
        }

        if (match == null)
        {
            throw BillingException.BadRequest(
                $"The brand is not in the shop's catalog. Brands: {string.Join(", ", brands.Select(b => b.Brand))}.");
        }
        return match.Brand;
    }

    /// <summary>A brand's plans live in the Maxio product family named after it ("Azure" or "Azure plans").</summary>
    private async Task<ProductFamily> FindOrCreateFamilyAsync(string brand, CancellationToken ct)
    {
        var families = await _call.RunAsync(() => Client.ProductFamilies.ListProductFamilies(
            new ListProductFamiliesRequest(), cancellationToken: ct));
        var wanted = new[] { brand, brand + " plans" };
        var found = families
            .Select(f => f.ProductFamily)
            .Where(f => f?.Id != null && f.ArchivedAt == null)
            .FirstOrDefault(f => wanted.Any(w => string.Equals(f!.Name, w, StringComparison.OrdinalIgnoreCase)));
        if (found != null) return found;

        var created = await _call.RunAsync(() => Client.ProductFamilies.CreateProductFamily(
            new CreateProductFamilyOperationRequest
            {
                Body = new CreateProductFamilyRequest { ProductFamily = new CreateProductFamily { Name = brand + " plans" } }
            }, cancellationToken: ct));
        return created.ProductFamily ?? throw new BillingException(HttpStatusCode.BadGateway, "The billing system did not return the new product family.");
    }

    public static string Slugify(string name)
    {
        var builder = new StringBuilder();
        foreach (var c in name.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9') builder.Append(c);
            else if (char.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) builder.Append('-');
        }
        return Regex.Replace(builder.ToString(), "-{2,}", "-").Trim('-');
    }

    // ---- currency price -------------------------------------------------------------------------

    public async Task<object> AddCurrencyPriceAsync(int planId, string? currency, decimal? price, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(currency) || !Regex.IsMatch(currency.Trim(), "^[A-Za-z]{3}$"))
        {
            throw BillingException.BadRequest("The currency must be a three-letter ISO code such as EUR.");
        }
        var code = currency.Trim().ToUpperInvariant();
        if (price == null || price <= 0m) throw BillingException.BadRequest("The price must be greater than zero.");
        if (price != decimal.Truncate(price.Value) || price > int.MaxValue)
        {
            throw BillingException.BadRequest("The price must be a whole amount in the currency's main unit.");
        }

        var product = (await _call.RunAsync(() => Client.Products.ReadProduct(
            new ReadProductRequest { ProductId = planId }, cancellationToken: ct))).Product;
        if (product.ArchivedAt != null) throw BillingException.NotFound($"Plan {planId} is archived.");
        if (product.DefaultProductPricePointId == null)
        {
            throw BillingException.Conflict($"Plan {planId} has no default price point to price.");
        }

        var site = (await _call.RunAsync(() => Client.Sites.ReadSite(cancellationToken: ct))).Site;
        if (string.Equals(site.Currency, code, StringComparison.OrdinalIgnoreCase))
        {
            throw BillingException.BadRequest($"{code} is already the plan's own currency.");
        }
        var enabled = site.NonPrimaryCurrencies ?? Array.Empty<string>();
        if (!enabled.Contains(code, StringComparer.OrdinalIgnoreCase))
        {
            var list = enabled.Count == 0 ? "none" : string.Join(", ", enabled);
            throw BillingException.UnprocessableEntity(
                $"Currency {code} is not enabled on the billing site (additional currencies enabled: {list}).");
        }

        var response = await _call.RunAsync(() => Client.ProductPricePoints.CreateProductCurrencyPrices(
            new CreateProductCurrencyPricesOperationRequest
            {
                ProductPricePointId = product.DefaultProductPricePointId.Value,
                Body = new CreateProductCurrencyPricesRequest
                {
                    CurrencyPrices = new[]
                    {
                        new CreateProductCurrencyPrice { Currency = code, Price = (int)price.Value, Role = CurrencyPriceRole.Baseline }
                    }
                }
            }, cancellationToken: ct));

        return new
        {
            planId,
            currency = code,
            price = price.Value,
            currencyPrices = response.CurrencyPrices.Select(c => new { c.Currency, c.Price, role = c.Role?.Value })
        };
    }
}
