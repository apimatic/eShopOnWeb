using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.AnyOf;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Rq = MaxioAdvancedBilling.Requests;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public partial class MaxioBillingService
{
    // ---------------------------------------------------------------- F8 new plan

    public async Task<PlanDto> CreatePlan(CreatePlanRequest request, CancellationToken ct)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name)) throw new MaxioFailure(400, "name is required.");
        if (request.Price is not { } price || price <= 0) throw new MaxioFailure(400, "price must be greater than zero.");
        if (decimal.Round(price, 2) != price) throw new MaxioFailure(400, "price can have at most two decimals.");

        // the brand must be one of the shop's catalog brands
        var brands = await _db.CatalogBrands.AsNoTracking().ToListAsync(ct);
        var brand = request.BrandId is { } bid
            ? brands.FirstOrDefault(b => b.Id == bid)
            : brands.FirstOrDefault(b => string.Equals(b.Brand, request.Brand?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (request.BrandId == null && string.IsNullOrWhiteSpace(request.Brand)) throw new MaxioFailure(400, "brand is required.");
        if (brand == null) throw new MaxioFailure(400, "brand is not one of the shop's catalog brands.");

        var existing = await ReadCatalog(ct);
        if (existing.Plans.Any(p => p.BrandId == brand.Id && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new MaxioFailure(409, $"A plan named '{name}' already exists for brand '{brand.Brand}'.");

        var familyHandle = $"eshop-brand-{brand.Id}";
        // app-generated handle: it is the reference the unknown-outcome re-read searches by
        var slug = Slug(name);
        if (slug.Length > 30) slug = slug[..30];
        var productHandle = $"plan-{brand.Id}-{slug}-{Guid.NewGuid().ToString("N")[..8]}";

        return await Claimed($"plan:{brand.Id}:{name.ToLowerInvariant()}", async () =>
        {
            var familyId = await EnsureFamily(brand.Id, brand.Brand, familyHandle, ct);
            var created = await Run(t => _client.Products.CreateProduct(new Rq.Products.CreateProductRequest
            {
                ProductFamilyId = familyId.ToString(CultureInfo.InvariantCulture),
                Body = new CreateOrUpdateProductRequest
                {
                    Product = new CreateOrUpdateProduct
                    {
                        Name = name,
                        Description = name,
                        Handle = productHandle,
                        PriceInCents = (long)decimal.Round(price * 100m),
                        Interval = 1,
                        IntervalUnit = IntervalUnit.Month,
                        RequireCreditCard = false,
                    }
                }
            }, cancellationToken: t), ct,
            settle: t => OrNull(() => _client.Products.ReadProductByHandle(new Rq.Products.ReadProductByHandleRequest { ApiHandle = productHandle }, cancellationToken: t)));

            var p = created.Product;
            return new PlanDto(p.Id ?? 0, p.Handle ?? productHandle, p.Name ?? name, p.Description, (p.PriceInCents ?? 0) / 100m, brand.Brand, brand.Id);
        });
    }

    private async Task<int> EnsureFamily(int brandId, string brandName, string handle, CancellationToken ct)
    {
        async Task<int?> Find(CancellationToken t)
        {
            var all = await _client.ProductFamilies.ListProductFamilies(new Rq.ProductFamilies.ListProductFamiliesRequest(), cancellationToken: t);
            return all.Select(f => f.ProductFamily).FirstOrDefault(f => f.Handle == handle && f.ArchivedAt == null)?.Id;
        }

        var found = await Run(async t => new Box(await Find(t)), ct);
        if (found.Id != null) return found.Id.Value;

        var created = await Run(t => _client.ProductFamilies.CreateProductFamily(new Rq.ProductFamilies.CreateProductFamilyOperationRequest
        {
            Body = new CreateProductFamilyRequest { ProductFamily = new CreateProductFamily { Name = brandName, Handle = handle } }
        }, cancellationToken: t), ct,
        settle: async t =>
        {
            var id = await Find(t);
            return id == null ? null : new ProductFamilyResponse { ProductFamily = new ProductFamily { Id = id, Handle = handle } };
        });
        return created.ProductFamily.Id ?? throw new MaxioFailure(502, "Billing system returned no product family id.");
    }

    private sealed record Box(int? Id);

    // ---------------------------------------------------------------- F9 currency price

    public async Task<CurrencyPriceDto> SetCurrencyPrice(int planId, CurrencyPriceRequest request, CancellationToken ct)
    {
        var currency = request.Currency?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(currency) || currency.Length != 3 || !currency.All(char.IsAsciiLetter))
            throw new MaxioFailure(400, "currency must be a three-letter ISO code.");
        if (request.Price is not { } price || price <= 0) throw new MaxioFailure(400, "price must be greater than zero.");
        if (price != decimal.Truncate(price) || price > int.MaxValue) throw new MaxioFailure(400, "price must be a whole number of currency units.");

        await RequireOfferedId(planId, ct);

        var site = await Run(t => _client.Sites.ReadSite(cancellationToken: t), ct);
        var allowed = site.Site.NonPrimaryCurrencies ?? [];
        if (string.Equals(site.Site.Currency, currency, StringComparison.OrdinalIgnoreCase))
            throw new MaxioFailure(400, "That is the site's primary currency; the plan's monthly price already covers it.");
        if (!allowed.Contains(currency, StringComparer.OrdinalIgnoreCase))
            throw new MaxioFailure(422, allowed.Count == 0
                ? "The billing site has no additional currencies configured."
                : $"currency must be one of: {string.Join(", ", allowed)}.");

        var product = await Run(t => _client.Products.ReadProduct(new Rq.Products.ReadProductRequest { ProductId = planId }, cancellationToken: t), ct);
        var pricePointId = product.Product.DefaultProductPricePointId ?? throw new MaxioFailure(422, "The plan has no default price point.");

        return await Claimed($"cur:{planId}:{currency}", async () =>
        {
            async Task<ProductPricePointResponse> ReadPoint(CancellationToken t) =>
                await _client.ProductPricePoints.ReadProductPricePoint(new Rq.ProductPricePoints.ReadProductPricePointRequest
                {
                    ProductId = planId,
                    PricePointId = pricePointId,
                    CurrencyPrices = true,
                }, cancellationToken: t);

            var point = (await Run(t => ReadPoint(t), ct)).PricePoint;
            if ((point.TrialPriceInCents ?? 0) > 0 || (point.InitialChargeInCents ?? 0) > 0)
                throw new MaxioFailure(422, "The plan has trial or setup pricing; currency prices for it are not supported.");

            var current = (point.CurrencyPrices ?? []).FirstOrDefault(c =>
                string.Equals(c.Currency, currency, StringComparison.OrdinalIgnoreCase) && c.Role == CurrencyPriceRole.Baseline);

            async Task<CurrencyPricesResponse?> Settle(CancellationToken t)
            {
                var prices = (await ReadPoint(t)).PricePoint.CurrencyPrices ?? [];
                var hit = prices.Where(c => string.Equals(c.Currency, currency, StringComparison.OrdinalIgnoreCase) && c.Role == CurrencyPriceRole.Baseline
                                            && (decimal?)c.Price == price).ToList();
                return hit.Count == 0 ? null : new CurrencyPricesResponse { CurrencyPrices = hit };
            }

            CurrencyPricesResponse written;
            if (current?.Id is { } existingId)
            {
                written = await Run(t => _client.ProductPricePoints.UpdateProductCurrencyPrices(new Rq.ProductPricePoints.UpdateProductCurrencyPricesRequest
                {
                    ProductPricePointId = pricePointId,
                    Body = new UpdateCurrencyPricesRequest { CurrencyPrices = [new UpdateCurrencyPrice { Id = existingId, Price = (double)price }] },
                }, cancellationToken: t), ct, settle: Settle);
            }
            else
            {
                written = await Run(t => _client.ProductPricePoints.CreateProductCurrencyPrices(new Rq.ProductPricePoints.CreateProductCurrencyPricesOperationRequest
                {
                    ProductPricePointId = pricePointId,
                    Body = new CreateProductCurrencyPricesRequest
                    {
                        CurrencyPrices = [new CreateProductCurrencyPrice { Currency = currency, Price = (int)price, Role = CurrencyPriceRole.Baseline }]
                    },
                }, cancellationToken: t), ct, settle: Settle);
            }

            var cp = written.CurrencyPrices.FirstOrDefault(c => string.Equals(c.Currency, currency, StringComparison.OrdinalIgnoreCase));
            return new CurrencyPriceDto(planId, currency, (decimal)(cp?.Price ?? (double)price), cp?.FormattedPrice);
        });
    }
}
