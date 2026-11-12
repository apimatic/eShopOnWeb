using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.AnyOf;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.ProductPricePoints;
using MaxioAdvancedBilling.Requests.Products;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Plans are Maxio products. Each plan belongs to a Maxio product family that stands for one catalog brand:
/// the family's handle is "brand-{catalogBrandId}" and its name is the brand's name.
/// </summary>
public class PlanService
{
    private const int PAGE_SIZE = 200;
    // A hard stop so a misbehaving provider cannot keep a request walking pages forever.
    private const int MAX_PAGES = 100;
    private const string BRAND_FAMILY_PREFIX = "brand-";

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioErrors _errors;
    private readonly ILogger<PlanService> _logger;

    public PlanService(MaxioAdvancedBillingClient client, MaxioErrors errors, ILogger<PlanService> logger)
    {
        _client = client;
        _errors = errors;
        _logger = logger;
    }

    /// <summary>
    /// The plans a shopper can subscribe to: every product of the site that is not archived.
    /// Walks every page, so no plan is silently dropped.
    /// </summary>
    public Task<List<Product>> ListOfferedAsync(CancellationToken cancellationToken = default) =>
        _errors.RunAsync("ListProducts", async () =>
        {
            var offered = new List<Product>();
            for (var page = 1; page <= MAX_PAGES; page++)
            {
                var products = await _client.Products.ListProducts(
                    new ListProductsRequest { Page = page, PerPage = PAGE_SIZE, IncludeArchived = false },
                    cancellationToken: cancellationToken);

                offered.AddRange(products.Select(p => p.Product).Where(p => p.ArchivedAt is null));

                if (products.Count < PAGE_SIZE)
                {
                    return offered;
                }
            }

            // Never return a silently truncated list.
            throw new BillingException(502, "The billing system returned more plans than can be listed.");
        });

    /// <summary>The offered plan with this handle, or a 404 BillingException.</summary>
    public async Task<Product> GetOfferedAsync(string? handle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new BillingException(400, "A plan handle is required.");
        }

        var wanted = handle.Trim();
        var offered = await ListOfferedAsync(cancellationToken);
        return offered.FirstOrDefault(p => string.Equals(p.Handle, wanted, StringComparison.OrdinalIgnoreCase))
            ?? throw new BillingException(404, $"Plan '{wanted}' is not one of the plans on offer.");
    }

    public static PlanDto ToDto(Product product)
    {
        var dto = new PlanDto
        {
            Id = product.Id ?? 0,
            Handle = product.Handle ?? string.Empty,
            Name = product.Name ?? string.Empty,
            Description = product.Description,
            PriceInCents = product.PriceInCents ?? 0,
            Price = (product.PriceInCents ?? 0) / 100m,
            Interval = product.Interval ?? 0,
            IntervalUnit = product.IntervalUnit?.Value ?? string.Empty,
            Brand = product.ProductFamily?.Name
        };

        var familyHandle = product.ProductFamily?.Handle;
        if (familyHandle is not null
            && familyHandle.StartsWith(BRAND_FAMILY_PREFIX, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(familyHandle[BRAND_FAMILY_PREFIX.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var brandId))
        {
            dto.BrandId = brandId;
        }

        return dto;
    }

    /// <summary>Adds a plan to the product family of a catalog brand (created on first use).</summary>
    public async Task<PlanDto> CreatePlanAsync(string name, long priceInCents, CatalogBrand brand, CancellationToken cancellationToken = default)
    {
        var familyId = await EnsureBrandFamilyAsync(brand, cancellationToken);

        // An explicit unique handle is this write's own key: it lets an unknown outcome be settled by reading it back.
        var handle = $"{Slug(name)}-{Guid.NewGuid().ToString("N")[..6]}";
        var request = new CreateProductRequest
        {
            ProductFamilyId = familyId.ToString(CultureInfo.InvariantCulture),
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
                    RequireCreditCard = false
                }
            }
        };

        try
        {
            var created = await _errors.RunAsync("CreateProduct", () => _client.Products.CreateProduct(request, cancellationToken: cancellationToken));
            return ToDto(created.Product);
        }
        catch (BillingException ex) when (ex.InnerException is not null && MaxioErrors.IsUnknownOutcome(ex.InnerException))
        {
            // The connection failed after Maxio may have acted: settle it by reading the plan back by its handle.
            var existing = await FindProductByHandleAsync(handle, cancellationToken);
            if (existing is not null)
            {
                return ToDto(existing);
            }

            throw new BillingException(504, "The billing system did not confirm the new plan. It was not created; try again.", ex);
        }
    }

    private async Task<Product?> FindProductByHandleAsync(string handle, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Products.ReadProductByHandle(new ReadProductByHandleRequest { ApiHandle = handle }, cancellationToken: cancellationToken);
            return response.Product;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task<int> EnsureBrandFamilyAsync(CatalogBrand brand, CancellationToken cancellationToken)
    {
        var familyHandle = $"{BRAND_FAMILY_PREFIX}{brand.Id.ToString(CultureInfo.InvariantCulture)}";

        async Task<int?> FindAsync() =>
            (await _client.ProductFamilies.ListProductFamilies(new ListProductFamiliesRequest(), cancellationToken: cancellationToken))
                .Select(f => f.ProductFamily)
                .FirstOrDefault(f => f is not null && f.ArchivedAt is null
                    && string.Equals(f.Handle, familyHandle, StringComparison.OrdinalIgnoreCase))?.Id;

        return await _errors.RunAsync("EnsureBrandFamily", async () =>
        {
            var existing = await FindAsync();
            if (existing is not null)
            {
                return existing.Value;
            }

            try
            {
                var created = await _client.ProductFamilies.CreateProductFamily(
                    new CreateProductFamilyOperationRequest
                    {
                        Body = new CreateProductFamilyRequest
                        {
                            ProductFamily = new CreateProductFamily { Name = brand.Brand, Handle = familyHandle, Description = $"Plans of the {brand.Brand} brand" }
                        }
                    },
                    cancellationToken: cancellationToken);
                return created.ProductFamily?.Id ?? throw new BillingException(502, "The billing system did not return the new product family.");
            }
            catch (ApiException)
            {
                // Two staff members adding the first plan of a brand at once: the loser's create is refused, so read the winner's.
                var raced = await FindAsync();
                if (raced is not null)
                {
                    return raced.Value;
                }

                throw;
            }
        });
    }

    /// <summary>Prices a plan in another currency. The plan is a product id or handle.</summary>
    public async Task<CurrencyPriceDto> AddCurrencyPriceAsync(string planIdOrHandle, string currency, decimal price, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3 || !currency.Trim().All(char.IsLetter))
        {
            throw new BillingException(400, "The currency must be a three-letter ISO 4217 code such as EUR.");
        }

        if (price <= 0)
        {
            throw new BillingException(400, "The price must be greater than zero.");
        }

        // The SDK models a currency price as a whole number of currency units.
        if (price != decimal.Truncate(price) || price > int.MaxValue)
        {
            throw new BillingException(400, "The price must be a whole number of currency units.");
        }

        var code = currency.Trim().ToUpperInvariant();
        var productId = await ResolveProductIdAsync(planIdOrHandle, cancellationToken);
        var pricePointId = await DefaultPricePointIdAsync(productId, cancellationToken);

        var request = new CreateProductCurrencyPricesOperationRequest
        {
            ProductPricePointId = pricePointId,
            Body = new CreateProductCurrencyPricesRequest
            {
                CurrencyPrices = new[]
                {
                    new CreateProductCurrencyPrice { Currency = code, Price = (int)price, Role = CurrencyPriceRole.Baseline }
                }
            }
        };

        try
        {
            var response = await _errors.RunAsync("CreateProductCurrencyPrices",
                () => _client.ProductPricePoints.CreateProductCurrencyPrices(request, cancellationToken: cancellationToken));
            var created = response.CurrencyPrices.FirstOrDefault(c => string.Equals(c.Currency, code, StringComparison.OrdinalIgnoreCase));
            return new CurrencyPriceDto
            {
                PlanId = productId,
                Currency = code,
                Price = (decimal)(created?.Price ?? (double)price),
                FormattedPrice = created?.FormattedPrice
            };
        }
        catch (BillingException ex) when (ex.InnerException is not null && MaxioErrors.IsUnknownOutcome(ex.InnerException))
        {
            // Settle the unknown outcome by reading the price point's currency prices back.
            var points = await _errors.RunAsync("ListProductPricePoints",
                () => _client.ProductPricePoints.ListProductPricePoints(
                    new ListProductPricePointsRequest { ProductId = ProductIdModel.Int(productId), CurrencyPrices = true },
                    cancellationToken: cancellationToken));
            var existing = points.PricePoints.FirstOrDefault(p => p.Id == pricePointId)?.CurrencyPrices?
                .FirstOrDefault(c => string.Equals(c.Currency, code, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                return new CurrencyPriceDto { PlanId = productId, Currency = code, Price = (decimal)(existing.Price ?? (double)price), FormattedPrice = existing.FormattedPrice };
            }

            throw new BillingException(504, "The billing system did not confirm the currency price. It was not saved; try again.", ex);
        }
    }

    private async Task<int> ResolveProductIdAsync(string planIdOrHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(planIdOrHandle))
        {
            throw new BillingException(400, "A plan is required.");
        }

        var key = planIdOrHandle.Trim();
        if (int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            var response = await _errors.RunAsync("ReadProduct", () => _client.Products.ReadProduct(new ReadProductRequest { ProductId = id }, cancellationToken: cancellationToken));
            return response.Product.Id ?? id;
        }

        var byHandle = await _errors.RunAsync("ReadProductByHandle", () => _client.Products.ReadProductByHandle(new ReadProductByHandleRequest { ApiHandle = key }, cancellationToken: cancellationToken));
        return byHandle.Product.Id ?? throw new BillingException(404, $"Plan '{key}' was not found.");
    }

    private async Task<int> DefaultPricePointIdAsync(int productId, CancellationToken cancellationToken)
    {
        var points = await _errors.RunAsync("ListProductPricePoints",
            () => _client.ProductPricePoints.ListProductPricePoints(
                new ListProductPricePointsRequest { ProductId = ProductIdModel.Int(productId) },
                cancellationToken: cancellationToken));
        var live = points.PricePoints.Where(p => p.ArchivedAt is null && p.Id is not null).ToList();
        var chosen = live.FirstOrDefault(p => p.Type == PricePointType.Default) ?? live.FirstOrDefault();
        return chosen?.Id ?? throw new BillingException(422, "The plan has no price point to attach a currency price to.");
    }

    private static string Slug(string name)
    {
        var builder = new StringBuilder();
        foreach (var c in name.Trim().ToLowerInvariant())
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(c);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var slug = builder.ToString().Trim('-');
        if (slug.Length > 40)
        {
            slug = slug[..40].Trim('-');
        }

        return slug.Length == 0 ? "plan" : slug;
    }
}
