using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.AnyOf;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.Invoices;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.ProductPricePoints;
using MaxioAdvancedBilling.Requests.Products;
using MaxioAdvancedBilling.Requests.SubscriptionComponents;
using MaxioAdvancedBilling.Requests.SubscriptionProducts;
using MaxioAdvancedBilling.Requests.Subscriptions;
using MaxioAdvancedBilling.Requests.SubscriptionStatus;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Subscription billing on top of Maxio. Maxio is the system of record: nothing is cached or
/// stored locally, every answer is read from (or written to) Maxio.
/// </summary>
public class BillingService
{
    private const int PAGE_SIZE = 200;
    private const string BRAND_FAMILY_PREFIX = "brand-";
    private const string SUBSCRIPTION_NOT_FOUND = "Subscription not found.";

    private readonly MaxioClientProvider _provider;
    private readonly IReadRepository<CatalogBrand> _brandRepository;
    private readonly UserManager<ApplicationUser> _userManager;

    public BillingService(MaxioClientProvider provider, IReadRepository<CatalogBrand> brandRepository,
        UserManager<ApplicationUser> userManager)
    {
        _provider = provider;
        _brandRepository = brandRepository;
        _userManager = userManager;
    }

    private MaxioAdvancedBillingClient Maxio => _provider.Client;

    // ---------------------------------------------------------------- plans (shopper)

    public async Task<List<PlanDto>> ListPlansAsync(CancellationToken ct)
    {
        var brands = await GetBrandNamesAsync();
        var plans = new List<PlanDto>();
        for (var page = 1; ; page++)
        {
            var batch = await MaxioCall.RunAsync(() => Maxio.Products.ListProducts(
                new ListProductsRequest { Page = page, PerPage = PAGE_SIZE }, cancellationToken: ct));
            foreach (var response in batch)
            {
                var product = response.Product;
                if (product.ArchivedAt != null || product.ProductFamily?.ArchivedAt != null) continue;
                plans.Add(ToPlan(product, brands));
            }
            if (batch.Count < PAGE_SIZE) break;
        }
        return plans;
    }

    // ---------------------------------------------------------------- subscribe

    public async Task<SubscriptionDto> SubscribeAsync(string userName, string? planHandle, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
            throw new BillingException(400, "A plan handle is required.");

        var product = await GetActivePlanAsync(planHandle.Trim(), ct);
        var customerId = await EnsureCustomerAsync(userName, ct);

        var created = await MaxioCall.RunAsync(() => Maxio.Subscriptions.CreateSubscription(
            new CreateSubscriptionOperationRequest
            {
                Body = new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductId = product.Id,
                        CustomerId = customerId,
                        // No card details are collected: the plan is billed by invoice.
                        PaymentCollectionMethod = CollectionMethod.Remittance,
                    }
                }
            }, cancellationToken: ct));

        var subscription = created.Subscription
            ?? throw new BillingException(502, "The billing system did not return the new subscription.");
        return await ToSubscriptionAsync(subscription, ct);
    }

    // ---------------------------------------------------------------- read

    public async Task<SubscriptionDto> GetSubscriptionAsync(string userName, int subscriptionId, CancellationToken ct)
    {
        var subscription = await LoadOwnedAsync(userName, subscriptionId, ct);
        return await ToSubscriptionAsync(subscription, ct);
    }

    // ---------------------------------------------------------------- pause / resume

    public async Task<SubscriptionDto> PauseAsync(string userName, int subscriptionId, CancellationToken ct)
    {
        var current = await LoadOwnedAsync(userName, subscriptionId, ct);
        if (IsPaused(current)) throw new BillingException(409, "The subscription is already paused.");
        if (IsCanceled(current) || IsEnded(current))
            throw new BillingException(409, $"A {StateOf(current)} subscription cannot be paused.");

        var result = await MaxioCall.RunAsync(() => Maxio.SubscriptionStatus.PauseSubscription(
            new PauseSubscriptionRequest { SubscriptionId = subscriptionId, Body = new PauseRequest() },
            cancellationToken: ct));
        return await ToSubscriptionAsync(result.Subscription ?? current, ct);
    }

    public async Task<SubscriptionDto> ResumeAsync(string userName, int subscriptionId, CancellationToken ct)
    {
        var current = await LoadOwnedAsync(userName, subscriptionId, ct);
        if (!IsPaused(current)) throw new BillingException(409, "Only a paused subscription can be resumed.");

        var result = await MaxioCall.RunAsync(() => Maxio.SubscriptionStatus.ResumeSubscription(
            new ResumeSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: ct));
        return await ToSubscriptionAsync(result.Subscription ?? current, ct);
    }

    // ---------------------------------------------------------------- cancel / restart

    public async Task<SubscriptionDto> CancelAsync(string userName, int subscriptionId, CancellationToken ct)
    {
        var current = await LoadOwnedAsync(userName, subscriptionId, ct);
        if (IsCanceled(current)) throw new BillingException(409, "The subscription is already canceled.");
        if (IsEnded(current)) throw new BillingException(409, $"A {StateOf(current)} subscription cannot be canceled.");

        var result = await MaxioCall.RunAsync(() => Maxio.SubscriptionStatus.CancelSubscription(
            new CancelSubscriptionRequest
            {
                SubscriptionId = subscriptionId,
                Body = new CancellationRequest { Subscription = new CancellationOptions() }
            }, cancellationToken: ct));
        return await ToSubscriptionAsync(result.Subscription ?? current, ct);
    }

    public async Task<SubscriptionDto> RestartAsync(string userName, int subscriptionId, CancellationToken ct)
    {
        var current = await LoadOwnedAsync(userName, subscriptionId, ct);
        if (!IsCanceled(current)) throw new BillingException(409, "Only a canceled subscription can be restarted.");

        var result = await MaxioCall.RunAsync(() => Maxio.SubscriptionStatus.ReactivateSubscription(
            new ReactivateSubscriptionOperationRequest
            {
                SubscriptionId = subscriptionId,
                Body = new ReactivateSubscriptionRequest()
            }, cancellationToken: ct));
        return await ToSubscriptionAsync(result.Subscription ?? current, ct);
    }

    // ---------------------------------------------------------------- switch plan

    public async Task<SubscriptionDto> SwitchPlanAsync(string userName, int subscriptionId, string? planHandle, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
            throw new BillingException(400, "A plan handle is required.");

        var current = await LoadOwnedAsync(userName, subscriptionId, ct);
        var target = await GetActivePlanAsync(planHandle.Trim(), ct);
        if (current.Product?.Id == target.Id)
            throw new BillingException(409, "The subscription is already on that plan.");

        // Maxio refuses a prorated migration for canceled or paused subscriptions; for those the
        // plan is changed directly on the subscription, leaving its state as it is.
        var direct = IsCanceled(current) || IsPaused(current) || IsEnded(current);
        if (!direct)
        {
            try
            {
                await MaxioCall.RunAsync(() => Maxio.SubscriptionProducts.MigrateSubscriptionProduct(
                    new MigrateSubscriptionProductRequest
                    {
                        SubscriptionId = subscriptionId,
                        Body = new SubscriptionProductMigrationRequest
                        {
                            Migration = new SubscriptionProductMigration { ProductId = target.Id }
                        }
                    }, cancellationToken: ct));
            }
            catch (BillingException ex) when (ex.StatusCode == 422 &&
                                              ex.Message.Contains("not eligible", StringComparison.OrdinalIgnoreCase))
            {
                direct = true;
            }
        }

        if (direct)
        {
            await MaxioCall.RunAsync(() => Maxio.Subscriptions.UpdateSubscription(
                new UpdateSubscriptionOperationRequest
                {
                    SubscriptionId = subscriptionId,
                    Body = new UpdateSubscriptionRequest
                    {
                        Subscription = new UpdateSubscription { ProductId = target.Id, ProductChangeDelayed = false }
                    }
                }, cancellationToken: ct));
        }

        // Do not trust the call alone: confirm the subscription now sits on the new plan.
        var fresh = await LoadOwnedAsync(userName, subscriptionId, ct);
        if (fresh.Product?.Id != target.Id)
            throw new BillingException(409, "The billing system did not move the subscription to the requested plan.");
        return await ToSubscriptionAsync(fresh, ct);
    }

    // ---------------------------------------------------------------- usage

    public async Task<DailyUsageDto> GetUsageAsync(string userName, int subscriptionId, DateOnly date, CancellationToken ct)
    {
        await LoadOwnedAsync(userName, subscriptionId, ct);

        var components = new List<SubscriptionComponent>();
        var componentResponses = await MaxioCall.RunAsync(() => Maxio.SubscriptionComponents.ListSubscriptionComponents(
            new ListSubscriptionComponentsRequest { SubscriptionId = subscriptionId }, cancellationToken: ct));
        foreach (var response in componentResponses)
        {
            var component = response.Component;
            if (component?.ComponentId != null && component.Kind == ComponentKind.MeteredComponent)
                components.Add(component);
        }

        var result = new DailyUsageDto
        {
            SubscriptionId = subscriptionId,
            Date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        };

        // Maxio treats since/until as dates in the site's time zone, until being exclusive. Ask for
        // a wider window and keep exactly the usages recorded on the requested (site-local) day.
        var since = new DateTimeOffset(date.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var until = new DateTimeOffset(date.AddDays(2).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        foreach (var component in components)
        {
            var componentId = component.ComponentId!.Value;
            var entry = new ComponentUsageDto
            {
                ComponentId = componentId,
                ComponentHandle = component.ComponentHandle,
                Name = component.Name,
                UnitName = component.UnitName,
            };
            for (var page = 1; ; page++)
            {
                var batch = await MaxioCall.RunAsync(() => Maxio.SubscriptionComponents.ListUsages(
                    new ListUsagesRequest
                    {
                        SubscriptionIdOrReference = subscriptionId,
                        ComponentId = componentId,
                        SinceDate = since,
                        UntilDate = until,
                        Page = page,
                        PerPage = PAGE_SIZE,
                    }, cancellationToken: ct));
                foreach (var usageResponse in batch)
                {
                    var usage = usageResponse.Usage;
                    if (usage.CreatedAt is not { } createdAt) continue;
                    if (DateOnly.FromDateTime(createdAt.DateTime) != date) continue;
                    var quantity = QuantityOf(usage.Quantity);
                    entry.Entries.Add(new UsageEntryDto
                    {
                        Id = usage.Id,
                        Quantity = quantity,
                        Memo = usage.Memo,
                        RecordedAt = createdAt,
                    });
                    entry.TotalQuantity += quantity;
                }
                if (batch.Count < PAGE_SIZE) break;
            }
            entry.Entries = entry.Entries.OrderBy(e => e.RecordedAt).ThenBy(e => e.Id).ToList();
            result.Components.Add(entry);
        }
        return result;
    }

    // ---------------------------------------------------------------- staff: plans

    public async Task<PlanDto> CreatePlanAsync(string? name, decimal? price, int? brandId, string? brandName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new BillingException(400, "A plan name is required.");
        if (price is null) throw new BillingException(400, "A monthly price is required.");
        if (brandId is null && string.IsNullOrWhiteSpace(brandName)) throw new BillingException(400, "A brand is required.");
        var cents = ToCents(price.Value);

        CatalogBrand? brand;
        if (brandId is not null)
        {
            brand = await _brandRepository.GetByIdAsync(brandId.Value, ct);
        }
        else
        {
            var wanted = brandName!.Trim();
            brand = int.TryParse(wanted, NumberStyles.None, CultureInfo.InvariantCulture, out var numeric)
                ? await _brandRepository.GetByIdAsync(numeric, ct)
                : null;
            brand ??= (await _brandRepository.ListAsync(ct))
                .FirstOrDefault(b => string.Equals(b.Brand, wanted, StringComparison.OrdinalIgnoreCase));
        }
        if (brand is null)
            throw new BillingException(422, "The brand is not in the shop's catalog.");

        var familyId = await EnsureBrandFamilyAsync(brand, ct);
        var trimmedName = name.Trim();
        var handle = Slugify(trimmedName);

        try
        {
            var created = await MaxioCall.RunAsync(() => Maxio.Products.CreateProduct(
                new CreateProductRequest
                {
                    ProductFamilyId = familyId.ToString(CultureInfo.InvariantCulture),
                    Body = new CreateOrUpdateProductRequest
                    {
                        Product = new CreateOrUpdateProduct
                        {
                            Name = trimmedName,
                            Handle = handle,
                            Description = trimmedName,
                            PriceInCents = cents,
                            Interval = 1,
                            IntervalUnit = IntervalUnit.Month,
                            RequireCreditCard = false,
                        }
                    }
                }, cancellationToken: ct));
            var brands = new Dictionary<int, string> { [brand.Id] = brand.Brand };
            return ToPlan(created.Product, brands);
        }
        catch (BillingException ex) when (ex.StatusCode == 422 &&
                                          (ex.Message.Contains("taken", StringComparison.OrdinalIgnoreCase) ||
                                           ex.Message.Contains("already", StringComparison.OrdinalIgnoreCase) ||
                                           ex.Message.Contains("unique", StringComparison.OrdinalIgnoreCase)))
        {
            throw new BillingException(409, $"A plan named '{trimmedName}' already exists. {ex.Message}", ex);
        }
    }

    public async Task<CurrencyPriceDto> AddCurrencyPriceAsync(int planId, string? currency, decimal? price, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(currency)) throw new BillingException(400, "A currency is required.");
        var code = currency.Trim().ToUpperInvariant();
        if (code.Length != 3 || !code.All(c => c is >= 'A' and <= 'Z'))
            throw new BillingException(400, "The currency must be a three-letter ISO 4217 code.");
        if (price is null) throw new BillingException(400, "A price is required.");
        if (price.Value <= 0) throw new BillingException(400, "The price must be greater than zero.");
        ToCents(price.Value); // validates scale and range

        var product = (await MaxioCall.RunAsync(() => Maxio.Products.ReadProduct(
            new ReadProductRequest { ProductId = planId }, cancellationToken: ct), "Plan not found.")).Product;
        if (product.ArchivedAt != null) throw new BillingException(409, "The plan is archived.");
        var pricePointId = product.DefaultProductPricePointId ?? product.ProductPricePointId
            ?? throw new BillingException(422, "The plan has no price point to attach a currency price to.");

        // The create call only takes whole currency units; the exact amount is set with an update.
        var whole = (int)Math.Min(int.MaxValue, Math.Ceiling(price.Value));
        try
        {
            await MaxioCall.RunAsync(() => Maxio.ProductPricePoints.CreateProductCurrencyPrices(
                new CreateProductCurrencyPricesOperationRequest
                {
                    ProductPricePointId = pricePointId,
                    Body = new CreateProductCurrencyPricesRequest
                    {
                        CurrencyPrices = new[]
                        {
                            new CreateProductCurrencyPrice { Currency = code, Price = whole, Role = CurrencyPriceRole.Baseline }
                        }
                    }
                }, cancellationToken: ct));
        }
        catch (BillingException ex) when (ex.StatusCode == 422 && ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            // The plan is already priced in this currency: replace that price.
            await UpdateExistingCurrencyPriceAsync(planId, pricePointId, code, price.Value, ex, ct);
            return new CurrencyPriceDto { PlanId = planId, Currency = code, Price = price.Value };
        }

        if (whole != price.Value)
            await UpdateExistingCurrencyPriceAsync(planId, pricePointId, code, price.Value, null, ct);
        return new CurrencyPriceDto { PlanId = planId, Currency = code, Price = price.Value };
    }

    private async Task UpdateExistingCurrencyPriceAsync(int productId, int pricePointId, string code, decimal price,
        BillingException? original, CancellationToken ct)
    {
        var pricePoint = (await MaxioCall.RunAsync(() => Maxio.ProductPricePoints.ReadProductPricePoint(
            new ReadProductPricePointRequest
            {
                ProductId = productId,
                PricePointId = pricePointId,
                CurrencyPrices = true,
            }, cancellationToken: ct))).PricePoint;
        var existing = pricePoint.CurrencyPrices?.FirstOrDefault(p =>
            string.Equals(p.Currency, code, StringComparison.OrdinalIgnoreCase) && p.Id != null);
        if (existing is null)
            throw original ?? new BillingException(422, $"The plan could not be priced in {code}.");

        await MaxioCall.RunAsync(() => Maxio.ProductPricePoints.UpdateProductCurrencyPrices(
            new UpdateProductCurrencyPricesRequest
            {
                ProductPricePointId = pricePointId,
                Body = new UpdateCurrencyPricesRequest
                {
                    CurrencyPrices = new[] { new UpdateCurrencyPrice { Id = existing.Id!.Value, Price = (double)price } }
                }
            }, cancellationToken: ct));
    }

    // ---------------------------------------------------------------- staff: next billing date

    public async Task<SubscriptionDto> SetNextBillingDateAsync(int subscriptionId, DateOnly? date, CancellationToken ct)
    {
        if (date is null) throw new BillingException(400, "A date is required.");

        var subscription = (await MaxioCall.RunAsync(() => Maxio.Subscriptions.ReadSubscription(
            new ReadSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: ct), SUBSCRIPTION_NOT_FOUND)).Subscription
            ?? throw new BillingException(404, SUBSCRIPTION_NOT_FOUND);

        // The billing day is a date in the site's time zone (the offset Maxio reports). Bill at
        // midday so the date survives any offset or daylight-saving shift.
        var offset = subscription.NextAssessmentAt?.Offset
            ?? subscription.CurrentPeriodEndsAt?.Offset
            ?? subscription.CreatedAt?.Offset
            ?? TimeSpan.Zero;
        var billAt = new DateTimeOffset(date.Value.ToDateTime(new TimeOnly(12, 0)), offset);
        if (billAt <= DateTimeOffset.UtcNow)
            throw new BillingException(422, "The next billing date must be in the future.");

        await MaxioCall.RunAsync(() => Maxio.Subscriptions.UpdateSubscription(
            new UpdateSubscriptionOperationRequest
            {
                SubscriptionId = subscriptionId,
                Body = new UpdateSubscriptionRequest { Subscription = new UpdateSubscription { NextBillingAt = billAt } }
            }, cancellationToken: ct), SUBSCRIPTION_NOT_FOUND);

        var fresh = (await MaxioCall.RunAsync(() => Maxio.Subscriptions.ReadSubscription(
            new ReadSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: ct), SUBSCRIPTION_NOT_FOUND)).Subscription
            ?? subscription;
        if (fresh.NextAssessmentAt is { } next && DateOnly.FromDateTime(next.DateTime) != date.Value)
            throw new BillingException(409, "The billing system did not apply the requested billing date.");
        return await ToSubscriptionAsync(fresh, ct);
    }

    // ---------------------------------------------------------------- helpers

    private async Task<Dictionary<int, string>> GetBrandNamesAsync()
    {
        var brands = await _brandRepository.ListAsync();
        return brands.ToDictionary(b => b.Id, b => b.Brand);
    }

    private async Task<Product> GetActivePlanAsync(string handle, CancellationToken ct)
    {
        var product = (await MaxioCall.RunAsync(() => Maxio.Products.ReadProductByHandle(
            new ReadProductByHandleRequest { ApiHandle = handle }, cancellationToken: ct),
            $"Plan '{handle}' not found.")).Product;
        if (product.Id is null) throw new BillingException(404, $"Plan '{handle}' not found.");
        if (product.ArchivedAt != null || product.ProductFamily?.ArchivedAt != null)
            throw new BillingException(409, $"Plan '{handle}' is no longer available.");
        return product;
    }

    private async Task<int> EnsureCustomerAsync(string userName, CancellationToken ct)
    {
        var reference = ReferenceFor(userName);
        var existing = await FindCustomerAsync(reference, ct);
        if (existing != null) return existing.Value;

        var user = await _userManager.FindByNameAsync(userName);
        var email = user?.Email;
        if (string.IsNullOrWhiteSpace(email)) email = userName.Contains('@') ? userName : $"{reference}@example.com";
        var local = email.Split('@')[0];

        try
        {
            var created = await MaxioCall.RunAsync(() => Maxio.Customers.CreateCustomer(
                new CreateCustomerOperationRequest
                {
                    Body = new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = string.IsNullOrWhiteSpace(local) ? "Shopper" : local,
                            LastName = "Shopper",
                            Email = email,
                            Reference = reference,
                        }
                    }
                }, cancellationToken: ct));
            return created.Customer?.Id ?? throw new BillingException(502, "The billing system did not return the customer.");
        }
        catch (BillingException)
        {
            // Possibly created concurrently by another request of the same shopper.
            var raced = await FindCustomerAsync(reference, ct);
            if (raced != null) return raced.Value;
            throw;
        }
    }

    private async Task<int?> FindCustomerAsync(string reference, CancellationToken ct)
    {
        try
        {
            var found = await MaxioCall.RunAsync(() => Maxio.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = reference }, cancellationToken: ct));
            return found.Customer?.Id;
        }
        catch (BillingException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
    }

    /// <summary>
    /// Loads a subscription and checks it belongs to the caller. Someone else's subscription is
    /// reported exactly like one that does not exist.
    /// </summary>
    private async Task<Subscription> LoadOwnedAsync(string userName, int subscriptionId, CancellationToken ct)
    {
        var response = await MaxioCall.RunAsync(() => Maxio.Subscriptions.ReadSubscription(
            new ReadSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: ct), SUBSCRIPTION_NOT_FOUND);
        var subscription = response.Subscription;
        if (subscription is null ||
            !string.Equals(subscription.Customer?.Reference, ReferenceFor(userName), StringComparison.Ordinal))
            throw new BillingException(404, SUBSCRIPTION_NOT_FOUND);
        return subscription;
    }

    internal static string ReferenceFor(string userName) => userName.Trim().ToLowerInvariant();

    private async Task<SubscriptionDto> ToSubscriptionAsync(Subscription s, CancellationToken ct)
    {
        var due = await AmountDueAsync(s, ct);
        return new SubscriptionDto
        {
            Id = s.Id ?? 0,
            State = StateOf(s),
            Paused = IsPaused(s),
            Paid = due <= 0,
            BalanceDue = due > 0 ? due : 0,
            PlanId = s.Product?.Id,
            PlanHandle = s.Product?.Handle,
            PlanName = s.Product?.Name,
            Price = s.Product?.PriceInCents is { } cents ? cents / 100m : null,
            Currency = s.Currency,
            NextBillingAt = s.NextAssessmentAt,
            CreatedAt = s.CreatedAt,
            CanceledAt = s.CanceledAt,
        };
    }

    /// <summary>What the shopper still owes: the larger of Maxio's balance and the open invoices' due amounts.</summary>
    private async Task<decimal> AmountDueAsync(Subscription s, CancellationToken ct)
    {
        var balance = (s.BalanceInCents ?? 0) / 100m;
        decimal invoiced = 0;
        if (s.Id is { } id)
        {
            for (var page = 1; ; page++)
            {
                var batch = await MaxioCall.RunAsync(() => Maxio.Invoices.ListInvoices(
                    new ListInvoicesRequest { SubscriptionId = id, Status = InvoiceStatus.Open, Page = page, PerPage = PAGE_SIZE },
                    cancellationToken: ct));
                foreach (var invoice in batch.Invoices)
                {
                    if (decimal.TryParse(invoice.DueAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) && d > 0)
                        invoiced += d;
                }
                if (batch.Invoices.Count < PAGE_SIZE) break;
            }
        }
        return Math.Max(balance, invoiced);
    }

    private async Task<int> EnsureBrandFamilyAsync(CatalogBrand brand, CancellationToken ct)
    {
        var handle = BRAND_FAMILY_PREFIX + brand.Id.ToString(CultureInfo.InvariantCulture);
        var id = await FindFamilyAsync(handle, ct);
        if (id != null) return id.Value;

        try
        {
            var created = await MaxioCall.RunAsync(() => Maxio.ProductFamilies.CreateProductFamily(
                new CreateProductFamilyOperationRequest
                {
                    Body = new CreateProductFamilyRequest
                    {
                        ProductFamily = new CreateProductFamily { Name = brand.Brand, Handle = handle }
                    }
                }, cancellationToken: ct));
            return created.ProductFamily?.Id ?? throw new BillingException(502, "The billing system did not return the product family.");
        }
        catch (BillingException)
        {
            var raced = await FindFamilyAsync(handle, ct);
            if (raced != null) return raced.Value;
            throw;
        }
    }

    private async Task<int?> FindFamilyAsync(string handle, CancellationToken ct)
    {
        var families = await MaxioCall.RunAsync(() => Maxio.ProductFamilies.ListProductFamilies(
            new ListProductFamiliesRequest(), cancellationToken: ct));
        var match = families.FirstOrDefault(f => string.Equals(f.ProductFamily.Handle, handle, StringComparison.OrdinalIgnoreCase));
        if (match is null) return null;
        if (match.ProductFamily.ArchivedAt != null)
            throw new BillingException(409, "The brand's product family is archived in the billing system.");
        return match.ProductFamily.Id;
    }

    private static PlanDto ToPlan(Product product, IReadOnlyDictionary<int, string> brands)
    {
        int? brandId = null;
        string? brandName = null;
        var familyHandle = product.ProductFamily?.Handle;
        if (familyHandle != null && familyHandle.StartsWith(BRAND_FAMILY_PREFIX, StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(familyHandle[BRAND_FAMILY_PREFIX.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            brandId = parsed;
            brands.TryGetValue(parsed, out brandName);
        }
        brandName ??= product.ProductFamily?.Name;

        var cents = product.PriceInCents ?? 0;
        return new PlanDto
        {
            Id = product.Id ?? 0,
            Handle = product.Handle,
            Name = product.Name,
            Description = product.Description,
            Price = cents / 100m,
            PriceInCents = cents,
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit?.Value,
            BrandId = brandId,
            Brand = brandName,
        };
    }

    private static long ToCents(decimal price)
    {
        if (price < 0) throw new BillingException(400, "The price cannot be negative.");
        if (price > 10_000_000m) throw new BillingException(400, "The price is too large.");
        var cents = price * 100m;
        if (cents != decimal.Truncate(cents))
            throw new BillingException(400, "The price can have at most two decimal places.");
        return (long)cents;
    }

    private static string Slugify(string name)
    {
        var sb = new StringBuilder();
        var lastDash = true;
        foreach (var ch in name.Trim().ToLowerInvariant())
        {
            if (ch is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            {
                sb.Append(ch);
                lastDash = false;
            }
            else if (!lastDash)
            {
                sb.Append('-');
                lastDash = true;
            }
        }
        var slug = sb.ToString().Trim('-');
        return slug.Length == 0 ? "plan-" + Guid.NewGuid().ToString("N")[..12] : slug;
    }

    private static decimal QuantityOf(Quantity1? quantity)
    {
        if (quantity is null) return 0;
        if (quantity.TryGetInt(out var i)) return i;
        if (quantity.TryGetString(out var s) &&
            decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)) return d;
        return 0;
    }

    private static string StateOf(Subscription s) => s.State?.Value ?? "unknown";

    private static bool IsPaused(Subscription s) =>
        s.State == SubscriptionState.OnHold || s.State == SubscriptionState.Paused;

    private static bool IsCanceled(Subscription s) => s.State == SubscriptionState.Canceled;

    private static bool IsEnded(Subscription s) =>
        s.State == SubscriptionState.Expired || s.State == SubscriptionState.FailedToCreate;
}
