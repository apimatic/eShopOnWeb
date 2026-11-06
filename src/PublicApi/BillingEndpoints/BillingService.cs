using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.ProductPricePoints;
using MaxioAdvancedBilling.Requests.Products;
using MaxioAdvancedBilling.Requests.SubscriptionComponents;
using MaxioAdvancedBilling.Requests.SubscriptionProducts;
using MaxioAdvancedBilling.Requests.SubscriptionStatus;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

/// <summary>
/// Subscription billing, with Maxio as the system of record. Every method either returns the result
/// or throws a <see cref="BillingException"/> with a 4xx/5xx status and a reason.
/// </summary>
public class BillingService
{
    /// <summary>Product families that hold the plans of a catalog brand have this handle prefix.</summary>
    private const string BRAND_FAMILY_HANDLE_PREFIX = "catalog-brand-";
    private const int PAGE_SIZE = 200;

    private readonly MaxioClientProvider _provider;
    private readonly IReadRepository<CatalogBrand> _brands;

    public BillingService(MaxioClientProvider provider, IReadRepository<CatalogBrand> brands)
    {
        _provider = provider;
        _brands = brands;
    }

    private MaxioAdvancedBillingClient Client => _provider.Client;

    // ---------------------------------------------------------------- identity

    /// <summary>Maxio customer reference for a signed-in user. User names are case-insensitive in Identity.</summary>
    public static string CustomerReference(string? userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new BillingException(401, "The token does not identify a user.");
        }
        return userName.Trim().ToLowerInvariant();
    }

    // ---------------------------------------------------------------- plans

    public async Task<List<PlanDto>> ListPlansAsync()
    {
        var plans = new List<PlanDto>();
        var families = await ListBrandFamiliesAsync();
        for (var page = 1; ; page++)
        {
            var currentPage = page;
            var batch = await MaxioErrors.CallAsync(() => Client.Products.ListProducts(new ListProductsRequest
            {
                Page = currentPage,
                PerPage = PAGE_SIZE,
                IncludeArchived = false,
            }));
            foreach (var item in batch)
            {
                var p = item.Product;
                if (p == null || !IsSubscribable(p)) continue;
                plans.Add(ToPlanDto(p, families));
            }
            if (batch.Count < PAGE_SIZE) break;
        }
        return plans;
    }

    private static bool IsSubscribable(Product p) => p.ArchivedAt == null && p.RequireCreditCard != true;

    private async Task<Dictionary<int, BrandRefDto>> ListBrandFamiliesAsync()
    {
        var result = new Dictionary<int, BrandRefDto>();
        var families = await MaxioErrors.CallAsync(() => Client.ProductFamilies.ListProductFamilies(new ListProductFamiliesRequest()));
        foreach (var f in families)
        {
            var family = f.ProductFamily;
            if (family?.Id == null || family.Handle == null || !family.Handle.StartsWith(BRAND_FAMILY_HANDLE_PREFIX, StringComparison.Ordinal)) continue;
            if (!int.TryParse(family.Handle.Substring(BRAND_FAMILY_HANDLE_PREFIX.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var brandId)) continue;
            result[family.Id.Value] = new BrandRefDto { Id = brandId, Name = family.Name ?? "" };
        }
        return result;
    }

    private static PlanDto ToPlanDto(Product p, IReadOnlyDictionary<int, BrandRefDto> brandFamilies)
    {
        BrandRefDto? brand = null;
        if (p.ProductFamily?.Id != null) brandFamilies.TryGetValue(p.ProductFamily.Id.Value, out brand);
        return new PlanDto
        {
            Id = p.Id ?? 0,
            Handle = p.Handle,
            Name = p.Name,
            Description = p.Description,
            PriceInCents = p.PriceInCents ?? 0,
            Price = (p.PriceInCents ?? 0) / 100m,
            Interval = p.Interval ?? 1,
            IntervalUnit = p.IntervalUnit?.Value,
            Brand = brand,
        };
    }

    /// <summary>Looks a plan up by handle; null when Maxio has none.</summary>
    private async Task<Product?> FindProductByHandleAsync(string handle)
    {
        try
        {
            return await MaxioErrors.CallAsync(async () => (await Client.Products.ReadProductByHandle(new ReadProductByHandleRequest { ApiHandle = handle })).Product);
        }
        catch (BillingException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
    }

    private async Task<Product> RequirePlanAsync(string? handle, bool forSubscribing)
    {
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new BillingException(400, "planHandle is required.");
        }
        var product = await FindProductByHandleAsync(handle.Trim());
        if (product == null)
        {
            throw new BillingException(404, $"There is no plan with handle '{handle}'.");
        }
        if (product.ArchivedAt != null)
        {
            throw new BillingException(409, $"Plan '{handle}' is no longer offered.");
        }
        if (forSubscribing && product.RequireCreditCard == true)
        {
            throw new BillingException(422, $"Plan '{handle}' requires card details, which this API does not take.");
        }
        return product;
    }

    // ---------------------------------------------------------------- shopper subscriptions

    public async Task<SubscriptionDto> SubscribeAsync(string? userName, string? planHandle)
    {
        var reference = CustomerReference(userName);
        var plan = await RequirePlanAsync(planHandle, forSubscribing: true);
        var customerId = await EnsureCustomerAsync(reference);

        var response = await MaxioErrors.CallAsync(() => Client.Subscriptions.CreateSubscription(new CreateSubscriptionOperationRequest
        {
            Body = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductId = plan.Id,
                    CustomerId = customerId,
                    // No card details are taken: the subscriber is invoiced and pays the invoice.
                    PaymentCollectionMethod = CollectionMethod.Remittance,
                },
            },
        }));
        return ToSubscriptionDto(RequireSubscription(response));
    }

    private async Task<int> EnsureCustomerAsync(string reference)
    {
        var existing = await FindCustomerAsync(reference);
        if (existing?.Id != null) return existing.Id.Value;

        var email = reference.Contains('@') ? reference : reference + "@eshoponweb.invalid";
        var local = email.Substring(0, email.IndexOf('@'));
        try
        {
            var created = await MaxioErrors.CallAsync(() => Client.Customers.CreateCustomer(new CreateCustomerOperationRequest
            {
                Body = new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = local,
                        LastName = "(eShopOnWeb)",
                        Email = email,
                        Reference = reference,
                    },
                },
            }));
            return created.Customer.Id ?? throw new BillingException(502, "The billing system did not return the customer.");
        }
        catch (BillingException)
        {
            // A concurrent first request may have created the customer in the meantime.
            var raced = await FindCustomerAsync(reference);
            if (raced?.Id != null) return raced.Id.Value;
            throw;
        }
    }

    private async Task<Customer?> FindCustomerAsync(string reference)
    {
        try
        {
            return await MaxioErrors.CallAsync(async () => (await Client.Customers.ReadCustomerByReference(new ReadCustomerByReferenceRequest { Reference = reference })).Customer);
        }
        catch (BillingException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
    }

    /// <summary>Loads a subscription the user owns. Someone else's subscription looks like a missing one.</summary>
    public async Task<Subscription> GetOwnedSubscriptionAsync(string? userName, int subscriptionId)
    {
        var reference = CustomerReference(userName);
        var subscription = await GetSubscriptionAsync(subscriptionId);
        var owner = subscription.Customer?.Reference;
        if (owner == null || !string.Equals(owner.Trim(), reference, StringComparison.OrdinalIgnoreCase))
        {
            throw new BillingException(404, $"There is no subscription {subscriptionId}.");
        }
        return subscription;
    }

    private async Task<Subscription> GetSubscriptionAsync(int subscriptionId)
    {
        try
        {
            var response = await MaxioErrors.CallAsync(() => Client.Subscriptions.ReadSubscription(new ReadSubscriptionRequest { SubscriptionId = subscriptionId }));
            return RequireSubscription(response);
        }
        catch (BillingException ex) when (ex.StatusCode == 404)
        {
            throw new BillingException(404, $"There is no subscription {subscriptionId}.", ex);
        }
    }

    private static Subscription RequireSubscription(SubscriptionResponse? response) =>
        response?.Subscription ?? throw new BillingException(502, "The billing system returned no subscription.");

    public async Task<SubscriptionDto> GetSubscriptionPageAsync(string? userName, int subscriptionId) =>
        ToSubscriptionDto(await GetOwnedSubscriptionAsync(userName, subscriptionId));

    public async Task<SubscriptionDto> PauseAsync(string? userName, int subscriptionId)
    {
        var sub = await GetOwnedSubscriptionAsync(userName, subscriptionId);
        if (IsPaused(sub)) throw new BillingException(409, "The subscription is already paused.");
        var response = await MaxioErrors.CallAsync(() => Client.SubscriptionStatus.PauseSubscription(new PauseSubscriptionRequest { SubscriptionId = subscriptionId }), conflictOn422: true);
        return ToSubscriptionDto(RequireSubscription(response));
    }

    public async Task<SubscriptionDto> ResumeAsync(string? userName, int subscriptionId)
    {
        var sub = await GetOwnedSubscriptionAsync(userName, subscriptionId);
        if (!IsPaused(sub)) throw new BillingException(409, "Only a paused subscription can be resumed.");
        var response = await MaxioErrors.CallAsync(() => Client.SubscriptionStatus.ResumeSubscription(new ResumeSubscriptionRequest { SubscriptionId = subscriptionId }), conflictOn422: true);
        return ToSubscriptionDto(RequireSubscription(response));
    }

    public async Task<SubscriptionDto> CancelAsync(string? userName, int subscriptionId)
    {
        var sub = await GetOwnedSubscriptionAsync(userName, subscriptionId);
        if (sub.State == SubscriptionState.Canceled) throw new BillingException(409, "The subscription is already canceled.");
        var response = await MaxioErrors.CallAsync(() => Client.SubscriptionStatus.CancelSubscription(new CancelSubscriptionRequest { SubscriptionId = subscriptionId }), conflictOn422: true);
        return ToSubscriptionDto(RequireSubscription(response));
    }

    public async Task<SubscriptionDto> RestartAsync(string? userName, int subscriptionId)
    {
        var sub = await GetOwnedSubscriptionAsync(userName, subscriptionId);
        if (sub.State != SubscriptionState.Canceled) throw new BillingException(409, "Only a canceled subscription can be restarted.");
        return ToSubscriptionDto(await ReactivateAsync(subscriptionId));
    }

    private async Task<Subscription> ReactivateAsync(int subscriptionId)
    {
        var response = await MaxioErrors.CallAsync(() => Client.SubscriptionStatus.ReactivateSubscription(new ReactivateSubscriptionOperationRequest { SubscriptionId = subscriptionId }), conflictOn422: true);
        return RequireSubscription(response);
    }

    private static bool IsPaused(Subscription s) => s.State == SubscriptionState.OnHold || s.State == SubscriptionState.Paused;

    public async Task<SubscriptionDto> SwitchPlanAsync(string? userName, int subscriptionId, string? planHandle)
    {
        var sub = await GetOwnedSubscriptionAsync(userName, subscriptionId);
        var plan = await RequirePlanAsync(planHandle, forSubscribing: true);
        if (sub.Product?.Id == plan.Id)
        {
            throw new BillingException(409, "The subscription is already on that plan.");
        }

        Subscription result;
        if (sub.State == SubscriptionState.Canceled)
        {
            // Maxio refuses to migrate a canceled subscription: change its plan, then restart it on the new plan.
            await ChangeProductAsync(subscriptionId, plan);
            result = await ReactivateAsync(subscriptionId);
        }
        else if (IsPaused(sub))
        {
            // Maxio refuses to migrate a paused subscription: change its plan and leave it paused.
            result = await ChangeProductAsync(subscriptionId, plan);
        }
        else
        {
            var response = await MaxioErrors.CallAsync(() => Client.SubscriptionProducts.MigrateSubscriptionProduct(new MigrateSubscriptionProductRequest
            {
                SubscriptionId = subscriptionId,
                Body = new SubscriptionProductMigrationRequest
                {
                    Migration = new SubscriptionProductMigration { ProductId = plan.Id },
                },
            }), conflictOn422: true);
            result = RequireSubscription(response);
        }

        if (result.Product?.Id != plan.Id)
        {
            throw new BillingException(502, "The billing system did not move the subscription to the new plan.");
        }
        return ToSubscriptionDto(result);
    }

    private async Task<Subscription> ChangeProductAsync(int subscriptionId, Product plan)
    {
        var response = await MaxioErrors.CallAsync(() => Client.Subscriptions.UpdateSubscription(new UpdateSubscriptionOperationRequest
        {
            SubscriptionId = subscriptionId,
            Body = new UpdateSubscriptionRequest
            {
                Subscription = new UpdateSubscription { ProductId = plan.Id },
            },
        }), conflictOn422: true);
        return RequireSubscription(response);
    }

    // ---------------------------------------------------------------- usage

    public async Task<DailyUsageResponse> GetUsageAsync(string? userName, int subscriptionId, string? date)
    {
        if (string.IsNullOrWhiteSpace(date) || !DateOnly.TryParseExact(date.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            throw new BillingException(400, "date is required, as YYYY-MM-DD.");
        }
        await GetOwnedSubscriptionAsync(userName, subscriptionId);

        var components = await MaxioErrors.CallAsync(() => Client.SubscriptionComponents.ListSubscriptionComponents(new ListSubscriptionComponentsRequest { SubscriptionId = subscriptionId }));
        var result = new DailyUsageResponse { SubscriptionId = subscriptionId, Date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };

        // Maxio's since/until dates are day filters whose edges depend on the site's time zone, so ask for a
        // generous window and keep exactly the records created on the requested day in the site's time zone
        // (Maxio reports timestamps in that zone).
        var since = new DateTimeOffset(day.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var until = new DateTimeOffset(day.AddDays(2).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        foreach (var c in components.Select(x => x.Component).Where(x => x != null && x.Kind == ComponentKind.MeteredComponent && x.ComponentId != null))
        {
            var componentId = c!.ComponentId!.Value;
            var dto = new ComponentUsageDto { ComponentId = componentId, ComponentName = c.Name, UnitName = c.UnitName };
            for (var page = 1; ; page++)
            {
                var currentPage = page;
                var usages = await MaxioErrors.CallAsync(() => Client.SubscriptionComponents.ListUsages(new ListUsagesRequest
                {
                    SubscriptionIdOrReference = subscriptionId,
                    ComponentId = componentId,
                    SinceDate = since,
                    UntilDate = until,
                    Page = currentPage,
                    PerPage = PAGE_SIZE,
                }));
                foreach (var u in usages.Select(x => x.Usage))
                {
                    if (u.CreatedAt == null || DateOnly.FromDateTime(u.CreatedAt.Value.DateTime) != day) continue;
                    var quantity = ReadQuantity(u);
                    dto.Records.Add(new UsageRecordDto { Id = u.Id, Quantity = quantity, Memo = u.Memo, RecordedAt = u.CreatedAt });
                    dto.Quantity += quantity;
                }
                if (usages.Count < PAGE_SIZE) break;
            }
            result.Components.Add(dto);
            result.TotalQuantity += dto.Quantity;
        }
        return result;
    }

    private static decimal ReadQuantity(Usage u)
    {
        if (u.Quantity == null) return 0;
        if (u.Quantity.TryGetInt(out var i)) return i;
        if (u.Quantity.TryGetString(out var s) && decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) return d;
        return 0;
    }

    // ---------------------------------------------------------------- staff: plans

    public async Task<PlanDto> CreatePlanAsync(string? name, decimal? price, int? brandId)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new BillingException(400, "name is required.");
        if (price == null || price <= 0) throw new BillingException(400, "price is required and must be greater than zero.");
        if (price != decimal.Round(price.Value, 2)) throw new BillingException(400, "price can have at most two decimals.");
        if (price > 10_000_000m) throw new BillingException(400, "price is too large.");
        if (brandId == null) throw new BillingException(400, "brandId is required.");

        var brand = await _brands.GetByIdAsync(brandId.Value);
        if (brand == null) throw new BillingException(400, $"There is no brand {brandId} in the catalog.");

        name = name.Trim();
        var handle = Slug(name);
        if (handle.Length == 0) throw new BillingException(400, "name must contain letters or digits.");
        if (await FindProductByHandleAsync(handle) != null)
        {
            throw new BillingException(409, $"A plan named '{name}' already exists.");
        }

        var family = await EnsureBrandFamilyAsync(brand);
        var cents = (long)decimal.Round(price.Value * 100m, 0);
        var response = await MaxioErrors.CallAsync(() => Client.Products.CreateProduct(new CreateProductRequest
        {
            ProductFamilyId = family.Id!.Value.ToString(CultureInfo.InvariantCulture),
            Body = new CreateOrUpdateProductRequest
            {
                Product = new CreateOrUpdateProduct
                {
                    Name = name,
                    Handle = handle,
                    Description = $"{name} ({brand.Brand})",
                    PriceInCents = cents,
                    Interval = 1,
                    IntervalUnit = IntervalUnit.Month,
                    RequireCreditCard = false,
                },
            },
        }), conflictOn422: true);

        return ToPlanDto(response.Product, new Dictionary<int, BrandRefDto> { [family.Id.Value] = new BrandRefDto { Id = brand.Id, Name = brand.Brand } });
    }

    private async Task<ProductFamily> EnsureBrandFamilyAsync(CatalogBrand brand)
    {
        var handle = BRAND_FAMILY_HANDLE_PREFIX + brand.Id.ToString(CultureInfo.InvariantCulture);
        var families = await MaxioErrors.CallAsync(() => Client.ProductFamilies.ListProductFamilies(new ListProductFamiliesRequest()));
        var existing = families.Select(f => f.ProductFamily).FirstOrDefault(f => f != null && f.Handle == handle && f.ArchivedAt == null);
        if (existing?.Id != null) return existing;

        var created = await MaxioErrors.CallAsync(() => Client.ProductFamilies.CreateProductFamily(new CreateProductFamilyOperationRequest
        {
            Body = new CreateProductFamilyRequest
            {
                ProductFamily = new CreateProductFamily { Name = brand.Brand, Handle = handle, Description = $"Plans of catalog brand {brand.Brand}" },
            },
        }), conflictOn422: true);
        var family = created.ProductFamily;
        if (family?.Id == null) throw new BillingException(502, "The billing system did not return the product family.");
        return family;
    }

    private static string Slug(string value)
    {
        var sb = new StringBuilder();
        foreach (var ch in value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9')) sb.Append(ch);
            else if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            else if (sb.Length > 0 && sb[sb.Length - 1] != '-') sb.Append('-');
        }
        return sb.ToString().Trim('-');
    }

    public async Task<CurrencyPriceResponse> AddCurrencyPriceAsync(int planId, string? currency, decimal? price)
    {
        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3 || !currency.Trim().All(char.IsLetter))
            throw new BillingException(400, "currency is required, as a three-letter ISO code such as EUR.");
        if (price == null || price <= 0) throw new BillingException(400, "price is required and must be greater than zero.");
        // Maxio's SDK sends currency prices as whole numbers.
        if (price != decimal.Truncate(price.Value) || price > int.MaxValue)
            throw new BillingException(400, "price must be a whole number of currency units.");

        Product product;
        try
        {
            product = await MaxioErrors.CallAsync(async () => (await Client.Products.ReadProduct(new ReadProductRequest { ProductId = planId })).Product);
        }
        catch (BillingException ex) when (ex.StatusCode == 404)
        {
            throw new BillingException(404, $"There is no plan {planId}.", ex);
        }
        var pricePointId = product.DefaultProductPricePointId;
        if (pricePointId == null) throw new BillingException(409, "The plan has no default price point.");

        var response = await MaxioErrors.CallAsync(() => Client.ProductPricePoints.CreateProductCurrencyPrices(new CreateProductCurrencyPricesOperationRequest
        {
            ProductPricePointId = pricePointId.Value,
            Body = new CreateProductCurrencyPricesRequest
            {
                CurrencyPrices = new[]
                {
                    new CreateProductCurrencyPrice { Currency = currency.Trim().ToUpperInvariant(), Price = (int)price.Value, Role = CurrencyPriceRole.Baseline },
                },
            },
        }), conflictOn422: true);

        return new CurrencyPriceResponse
        {
            PlanId = planId,
            CurrencyPrices = response.CurrencyPrices.Select(c => new CurrencyPriceDto { Currency = c.Currency, Price = c.Price == null ? null : (decimal)c.Price.Value, FormattedPrice = c.FormattedPrice }).ToList(),
        };
    }

    // ---------------------------------------------------------------- staff: next billing date

    public async Task<SubscriptionDto> SetNextBillingDateAsync(int subscriptionId, string? date)
    {
        if (string.IsNullOrWhiteSpace(date) || !DateOnly.TryParseExact(date.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            throw new BillingException(400, "date is required, as YYYY-MM-DD.");

        var sub = await GetSubscriptionAsync(subscriptionId);
        if (sub.NextAssessmentAt == null || sub.State == SubscriptionState.Canceled || sub.State == SubscriptionState.Expired)
            throw new BillingException(409, "The subscription has no next billing date to change; it is not billing.");

        // Keep the subscription's billing time of day and the site's time zone; only the date changes.
        var current = sub.NextAssessmentAt.Value;
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(current.Offset).DateTime);
        if (day <= today) throw new BillingException(400, "date must be in the future.");
        var target = new DateTimeOffset(day.ToDateTime(TimeOnly.FromDateTime(current.DateTime)), current.Offset);

        var response = await MaxioErrors.CallAsync(() => Client.Subscriptions.UpdateSubscription(new UpdateSubscriptionOperationRequest
        {
            SubscriptionId = subscriptionId,
            Body = new UpdateSubscriptionRequest { Subscription = new UpdateSubscription { NextBillingAt = target } },
        }), conflictOn422: true);
        var updated = RequireSubscription(response);
        if (updated.NextAssessmentAt == null || DateOnly.FromDateTime(updated.NextAssessmentAt.Value.DateTime) != day)
            throw new BillingException(409, "The billing system did not accept that billing date.");
        return ToSubscriptionDto(updated);
    }

    // ---------------------------------------------------------------- mapping

    private static SubscriptionDto ToSubscriptionDto(Subscription s) => new()
    {
        Id = s.Id ?? 0,
        State = s.State?.Value,
        // Maxio's balance is what is still owed on the subscription's invoices.
        BalanceInCents = s.BalanceInCents ?? 0,
        Paid = (s.BalanceInCents ?? 0) <= 0,
        Currency = s.Currency,
        PlanId = s.Product?.Id,
        PlanHandle = s.Product?.Handle,
        PlanName = s.Product?.Name,
        PlanPriceInCents = s.ProductPriceInCents,
        NextBillingAt = s.NextAssessmentAt,
        CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
        CanceledAt = s.CanceledAt,
        CreatedAt = s.CreatedAt,
    };
}
