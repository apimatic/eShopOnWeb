using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
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
using MaxioAdvancedBilling.Requests.SubscriptionStatus;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Subscription billing on top of Maxio (the system of record). Nothing is stored locally:
/// a shopper is a Maxio customer whose <c>reference</c> is their (lower-cased) user name.
/// </summary>
public class BillingService : IBillingService
{
    private const int PageSize = 200;
    private const string FamilySuffix = " plans";

    private readonly MaxioAdvancedBillingClient _maxio;
    private readonly IReadRepository<CatalogBrand> _brands;

    public BillingService(MaxioAdvancedBillingClient maxio, IReadRepository<CatalogBrand> brands)
    {
        _maxio = maxio;
        _brands = brands;
    }

    // ---------------------------------------------------------------- shopper features

    public async Task<IReadOnlyList<PlanDto>> ListPlansAsync(CancellationToken ct)
    {
        var products = await ListAvailableProductsAsync(ct);
        var brandNames = await BrandNamesAsync();
        return products.Select(p => ToPlan(p, brandNames)).ToList();
    }

    public async Task<SubscriptionDto> SubscribeAsync(string userName, string? planHandle, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
            throw new BillingException(400, "A plan handle is required.");

        var plan = await GetSubscribablePlanAsync(planHandle.Trim(), ct);
        var customerId = await EnsureCustomerAsync(userName, ct);

        // Remittance collection: the shopper is invoiced and never has to supply card details.
        var created = await Call("create the subscription", () => _maxio.Subscriptions.CreateSubscription(
            new CreateSubscriptionOperationRequest
            {
                Body = new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductId = plan.Id,
                        CustomerId = customerId,
                        PaymentCollectionMethod = CollectionMethod.Remittance,
                    }
                }
            }, cancellationToken: ct));
        return await ToDtoAsync(created.Subscription, ct);
    }

    public async Task<SubscriptionDto> GetSubscriptionAsync(string userName, int subscriptionId, CancellationToken ct)
    {
        var sub = await GetOwnedAsync(userName, subscriptionId, ct);
        return await ToDtoAsync(sub, ct);
    }

    public async Task<SubscriptionDto> PauseAsync(string userName, int subscriptionId, CancellationToken ct)
    {
        var sub = await GetOwnedAsync(userName, subscriptionId, ct);
        var state = StateOf(sub);
        if (state is "on_hold" or "paused")
            throw new BillingException(409, "The subscription is already paused.");
        if (state is "canceled" or "expired" or "failed_to_create" or "pending")
            throw new BillingException(409, $"A subscription in state '{state}' cannot be paused.");

        var result = await Call("pause the subscription", () => _maxio.SubscriptionStatus.PauseSubscription(
            new PauseSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: ct));
        return await ToDtoAsync(result.Subscription, ct);
    }

    public async Task<SubscriptionDto> ResumeAsync(string userName, int subscriptionId, CancellationToken ct)
    {
        var sub = await GetOwnedAsync(userName, subscriptionId, ct);
        var state = StateOf(sub);
        if (state is not ("on_hold" or "paused"))
            throw new BillingException(409, $"Only a paused subscription can be resumed (current state: '{state}').");

        var result = await Call("resume the subscription", () => _maxio.SubscriptionStatus.ResumeSubscription(
            new ResumeSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: ct));
        return await ToDtoAsync(result.Subscription, ct);
    }

    public async Task<SubscriptionDto> CancelAsync(string userName, int subscriptionId, CancellationToken ct)
    {
        var sub = await GetOwnedAsync(userName, subscriptionId, ct);
        var state = StateOf(sub);
        if (state is "canceled")
            throw new BillingException(409, "The subscription is already cancelled.");
        if (state is "expired" or "failed_to_create")
            throw new BillingException(409, $"A subscription in state '{state}' cannot be cancelled.");

        var result = await Call("cancel the subscription", () => _maxio.SubscriptionStatus.CancelSubscription(
            new CancelSubscriptionRequest
            {
                SubscriptionId = subscriptionId,
                Body = new CancellationRequest
                {
                    Subscription = new CancellationOptions { CancellationMessage = "Cancelled by the shopper." }
                }
            }, cancellationToken: ct));
        return await ToDtoAsync(result.Subscription, ct);
    }

    public async Task<SubscriptionDto> RestartAsync(string userName, int subscriptionId, CancellationToken ct)
    {
        var sub = await GetOwnedAsync(userName, subscriptionId, ct);
        var state = StateOf(sub);
        if (state is not "canceled")
            throw new BillingException(409, $"Only a cancelled subscription can be restarted (current state: '{state}').");

        var result = await ReactivateAsync(subscriptionId, ct);
        return await ToDtoAsync(result, ct);
    }

    public async Task<SubscriptionDto> SwitchPlanAsync(string userName, int subscriptionId, string? planHandle, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
            throw new BillingException(400, "A plan handle is required.");

        var sub = await GetOwnedAsync(userName, subscriptionId, ct);
        var plan = await GetSubscribablePlanAsync(planHandle.Trim(), ct);
        if (sub.Product?.Id == plan.Id)
            throw new BillingException(409, "The subscription is already on that plan.");

        var state = StateOf(sub);
        if (state is "expired" or "failed_to_create" or "pending")
            throw new BillingException(409, $"A subscription in state '{state}' cannot be switched to another plan.");

        // Maxio only migrates an active subscription, so a cancelled one is restarted and a paused one
        // resumed first. A failed switch puts the subscription back the way it was, and a paused
        // subscription is paused again once it is on the new plan.
        var wasCancelled = state is "canceled";
        var wasPaused = state is "on_hold" or "paused";
        if (wasCancelled)
        {
            await ReactivateAsync(subscriptionId, ct);
        }
        else if (wasPaused)
        {
            await Call("resume the subscription before switching it", () => _maxio.SubscriptionStatus.ResumeSubscription(
                new ResumeSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: ct));
        }

        SubscriptionResponse migrated;
        try
        {
            migrated = await Call("switch the subscription to the new plan", () => _maxio.SubscriptionProducts.MigrateSubscriptionProduct(
                new MigrateSubscriptionProductRequest
                {
                    SubscriptionId = subscriptionId,
                    Body = new SubscriptionProductMigrationRequest
                    {
                        Migration = new SubscriptionProductMigration { ProductId = plan.Id }
                    }
                }, cancellationToken: ct));
            if (migrated.Subscription.Product?.Id != plan.Id)
                throw new BillingException(502, "The billing system did not move the subscription to the requested plan.");
        }
        catch when (wasCancelled || wasPaused)
        {
            try
            {
                if (wasCancelled)
                {
                    await _maxio.SubscriptionStatus.CancelSubscription(new CancelSubscriptionRequest
                    {
                        SubscriptionId = subscriptionId,
                        Body = new CancellationRequest { Subscription = new CancellationOptions { CancellationMessage = "Plan switch failed; restored cancelled state." } }
                    }, cancellationToken: CancellationToken.None);
                }
                else
                {
                    await _maxio.SubscriptionStatus.PauseSubscription(
                        new PauseSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: CancellationToken.None);
                }
            }
            catch
            {
                // best effort
            }
            throw;
        }

        var result = migrated.Subscription;
        if (wasPaused)
        {
            try
            {
                result = (await _maxio.SubscriptionStatus.PauseSubscription(
                    new PauseSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: ct)).Subscription;
            }
            catch (SdkException)
            {
                // The switch itself succeeded; the response reports the state the subscription is really in.
            }
        }
        return await ToDtoAsync(result, ct);
    }

    public async Task<UsageDto> GetUsageAsync(string userName, int subscriptionId, string? date, CancellationToken ct)
    {
        var day = ParseDate(date, "date");
        await GetOwnedAsync(userName, subscriptionId, ct);

        var components = await Call("read the subscription's components", () => _maxio.SubscriptionComponents.ListSubscriptionComponents(
            new ListSubscriptionComponentsRequest { SubscriptionId = subscriptionId }, cancellationToken: ct));

        var result = new UsageDto { SubscriptionId = subscriptionId, Date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
        var usageKinds = new[] { ComponentKind.MeteredComponent.Value, ComponentKind.PrepaidUsageComponent.Value };
        foreach (var c in components.Select(x => x.Component).Where(x => x?.ComponentId != null && x.Kind != null && usageKinds.Contains(x.Kind.Value)))
        {
            var componentId = c!.ComponentId!.Value;
            // Maxio's date filter is "created_at >= since 00:00" and "created_at <= until 00:00" in the site's
            // time zone, so ask for a window around the day and keep exactly the entries stamped on that day.
            var since = new DateTimeOffset(day.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var until = new DateTimeOffset(day.AddDays(2).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var entries = new List<UsageEntryDto>();
            for (var page = 1; ; page++)
            {
                var p = page;
                var batch = await Call("read the subscription's usage", () => _maxio.SubscriptionComponents.ListUsages(
                    new ListUsagesRequest
                    {
                        SubscriptionIdOrReference = subscriptionId,
                        ComponentId = componentId,
                        SinceDate = since,
                        UntilDate = until,
                        Page = p,
                        PerPage = PageSize,
                    }, cancellationToken: ct));
                foreach (var u in batch.Select(x => x.Usage))
                {
                    if (u.CreatedAt is { } at && DateOnly.FromDateTime(at.DateTime) == day)
                    {
                        entries.Add(new UsageEntryDto { Id = u.Id ?? 0, CreatedAt = at, Quantity = QuantityOf(u.Quantity), Memo = u.Memo });
                    }
                }
                if (batch.Count < PageSize) break;
            }

            result.Components.Add(new ComponentUsageDto
            {
                ComponentId = componentId,
                Name = c.Name,
                UnitName = c.UnitName,
                TotalQuantity = entries.Sum(e => e.Quantity),
                Entries = entries.OrderBy(e => e.CreatedAt).ToList(),
            });
        }
        return result;
    }

    // ---------------------------------------------------------------- staff features

    public async Task<PlanDto> CreatePlanAsync(string? name, decimal? monthlyPrice, int? brandId, string? brand, CancellationToken ct)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name)) throw new BillingException(400, "A plan name is required.");
        if (name.Length > 255) throw new BillingException(400, "The plan name is too long (255 characters at most).");
        if (monthlyPrice is null) throw new BillingException(400, "A monthly price is required.");
        if (monthlyPrice < 0 || monthlyPrice > 10_000_000m)
            throw new BillingException(400, "The monthly price must be between 0 and 10,000,000.");
        if (decimal.Round(monthlyPrice.Value, 2) != monthlyPrice.Value)
            throw new BillingException(400, "The monthly price can have at most two decimal places.");
        if (brandId is null && string.IsNullOrWhiteSpace(brand))
            throw new BillingException(400, "A brand (brandId or brand name) is required.");

        var catalogBrands = await _brands.ListAsync(ct);
        CatalogBrand? catalogBrand = null;
        if (brandId != null)
        {
            catalogBrand = catalogBrands.FirstOrDefault(b => b.Id == brandId);
            if (catalogBrand != null && !string.IsNullOrWhiteSpace(brand) &&
                !string.Equals(catalogBrand.Brand, brand.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new BillingException(400, "brandId and brand refer to different catalog brands.");
        }
        else
        {
            catalogBrand = catalogBrands.FirstOrDefault(b => string.Equals(b.Brand, brand!.Trim(), StringComparison.OrdinalIgnoreCase));
        }
        if (catalogBrand == null)
            throw new BillingException(422, "The brand is not one of the brands in the shop's catalog.");

        var handle = Slug(name);
        if (handle.Length == 0)
            throw new BillingException(400, "The plan name must contain letters or digits.");
        if (await ProductByHandleOrNullAsync(handle, ct) != null)
            throw new BillingException(409, $"A plan with handle '{handle}' already exists.");

        var familyId = await EnsureFamilyAsync(catalogBrand.Brand + FamilySuffix, ct);
        var priceInCents = (long)decimal.Round(monthlyPrice.Value * 100m, 0);

        var created = await Call("create the plan", () => _maxio.Products.CreateProduct(new CreateProductRequest
        {
            ProductFamilyId = familyId.ToString(CultureInfo.InvariantCulture),
            Body = new CreateOrUpdateProductRequest
            {
                Product = new CreateOrUpdateProduct
                {
                    Name = name,
                    Handle = handle,
                    Description = $"{name} ({catalogBrand.Brand})",
                    PriceInCents = priceInCents,
                    Interval = 1,
                    IntervalUnit = IntervalUnit.Month,
                    // Shoppers subscribe without card details, so the plan must not demand one.
                    RequireCreditCard = false,
                    AutoCreateSignupPage = false,
                }
            }
        }, cancellationToken: ct));
        return ToPlan(created.Product, new[] { catalogBrand.Brand });
    }

    public async Task<CurrencyPricesDto> AddCurrencyPriceAsync(int planId, string? currency, decimal? price, CancellationToken ct)
    {
        currency = currency?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(currency) || !Regex.IsMatch(currency, "^[A-Z]{3}$"))
            throw new BillingException(400, "The currency must be a three-letter ISO code such as EUR.");
        if (price is null || price <= 0)
            throw new BillingException(400, "The price must be greater than zero.");
        // The Maxio SDK models a currency price as a whole number of currency units.
        if (price != decimal.Truncate(price.Value) || price > int.MaxValue)
            throw new BillingException(400, "The price must be a whole number of currency units.");

        var product = (await Call("read the plan", () => _maxio.Products.ReadProduct(
            new ReadProductRequest { ProductId = planId }, cancellationToken: ct))).Product;
        if (product.ArchivedAt != null)
            throw new BillingException(409, "The plan is archived.");
        if (product.DefaultProductPricePointId is not { } pricePointId)
            throw new BillingException(422, "The plan has no default price point to price in another currency.");

        var site = (await Call("read the billing site", () => _maxio.Sites.ReadSite(cancellationToken: ct))).Site;
        if (string.Equals(site.Currency, currency, StringComparison.OrdinalIgnoreCase))
            throw new BillingException(409, $"{currency} is the billing site's primary currency; the plan's price already applies.");
        var enabled = site.NonPrimaryCurrencies ?? new List<string>();
        if (!enabled.Contains(currency, StringComparer.OrdinalIgnoreCase))
        {
            var list = enabled.Count == 0 ? "none" : string.Join(", ", enabled);
            throw new BillingException(422, $"{currency} is not enabled on the billing site (additional currencies enabled: {list}).");
        }

        var created = await Call("price the plan in that currency", () => _maxio.ProductPricePoints.CreateProductCurrencyPrices(
            new CreateProductCurrencyPricesOperationRequest
            {
                ProductPricePointId = pricePointId,
                Body = new CreateProductCurrencyPricesRequest
                {
                    CurrencyPrices = new[]
                    {
                        new CreateProductCurrencyPrice { Currency = currency, Price = (int)price.Value, Role = CurrencyPriceRole.Baseline }
                    }
                }
            }, cancellationToken: ct));
        return new CurrencyPricesDto
        {
            PlanId = planId,
            CurrencyPrices = created.CurrencyPrices
                .Select(c => new CurrencyPriceDto { Currency = c.Currency, Price = (decimal)(c.Price ?? 0) })
                .ToList(),
        };
    }

    public async Task<NextBillingDateDto> SetNextBillingDateAsync(int subscriptionId, string? date, CancellationToken ct)
    {
        var day = ParseDate(date, "date");
        var sub = (await Call("read the subscription", () => _maxio.Subscriptions.ReadSubscription(
            new ReadSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: ct))).Subscription;

        var state = StateOf(sub);
        if (state is "canceled" or "expired" or "on_hold" or "paused" or "failed_to_create" or "pending")
            throw new BillingException(409, $"A subscription in state '{state}' is not billed, so its next billing date cannot be set.");

        // Keep the existing time of day (in the site's own offset); only the day moves.
        var basis = sub.NextAssessmentAt ?? sub.CurrentPeriodEndsAt ?? DateTimeOffset.UtcNow;
        var target = new DateTimeOffset(day.ToDateTime(TimeOnly.FromTimeSpan(basis.TimeOfDay)), basis.Offset);
        if (target <= DateTimeOffset.UtcNow)
            throw new BillingException(400, "The next billing date must be in the future.");

        var updated = (await Call("set the next billing date", () => _maxio.Subscriptions.UpdateSubscription(
            new UpdateSubscriptionOperationRequest
            {
                SubscriptionId = subscriptionId,
                Body = new UpdateSubscriptionRequest { Subscription = new UpdateSubscription { NextBillingAt = target } }
            }, cancellationToken: ct))).Subscription;

        if (updated.NextAssessmentAt is not { } applied || DateOnly.FromDateTime(applied.DateTime) != day)
            throw new BillingException(502, "The billing system did not apply the requested next billing date.");

        return new NextBillingDateDto
        {
            SubscriptionId = subscriptionId,
            Date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            NextBillingAt = applied,
        };
    }

    // ---------------------------------------------------------------- helpers

    private static string StateOf(Subscription sub) => sub.State?.Value ?? "unknown";

    private static DateOnly ParseDate(string? value, string field)
    {
        if (!DateOnly.TryParseExact(value?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            throw new BillingException(400, $"'{field}' is required and must be a date in the form YYYY-MM-DD.");
        return d;
    }

    private static string Slug(string name)
    {
        var sb = new StringBuilder();
        foreach (var ch in name.Trim().ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        return sb.ToString().Trim('-');
    }

    private static decimal QuantityOf(Quantity1? q)
    {
        if (q == null) return 0;
        if (q.TryGetInt(out var i)) return i;
        if (q.TryGetString(out var s) && decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
        return 0;
    }

    private static bool IsAvailable(Product p) =>
        p.ArchivedAt == null && !string.IsNullOrWhiteSpace(p.Handle) && p.RequireCreditCard != true;

    private static PlanDto ToPlan(Product p, IEnumerable<string> brandNames)
    {
        var family = p.ProductFamily?.Name;
        return new PlanDto
        {
            Id = p.Id ?? 0,
            Handle = p.Handle,
            Name = p.Name,
            Description = p.Description,
            MonthlyPrice = (p.PriceInCents ?? 0) / 100m,
            Interval = p.Interval ?? 1,
            IntervalUnit = p.IntervalUnit?.Value,
            ProductFamily = family,
            Brand = family == null ? null : brandNames.FirstOrDefault(b =>
                string.Equals(b + FamilySuffix, family, StringComparison.OrdinalIgnoreCase)),
        };
    }

    private async Task<IReadOnlyList<string>> BrandNamesAsync() =>
        (await _brands.ListAsync()).Select(b => b.Brand).ToList();

    private async Task<List<Product>> ListAvailableProductsAsync(CancellationToken ct)
    {
        var all = new List<Product>();
        for (var page = 1; ; page++)
        {
            var p = page;
            var batch = await Call("list the plans", () => _maxio.Products.ListProducts(
                new ListProductsRequest { Page = p, PerPage = PageSize, IncludeArchived = false }, cancellationToken: ct));
            all.AddRange(batch.Select(x => x.Product));
            if (batch.Count < PageSize) break;
        }
        return all.Where(IsAvailable).ToList();
    }

    private async Task<Product?> ProductByHandleOrNullAsync(string handle, CancellationToken ct)
    {
        try
        {
            return (await _maxio.Products.ReadProductByHandle(new ReadProductByHandleRequest { ApiHandle = handle }, cancellationToken: ct)).Product;
        }
        catch (ApiException e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (ApiException e)
        {
            throw Map("look up the plan", e);
        }
        catch (SdkException e)
        {
            throw MapTransport("look up the plan", e);
        }
    }

    private async Task<Product> GetSubscribablePlanAsync(string handle, CancellationToken ct)
    {
        var product = await ProductByHandleOrNullAsync(handle, ct)
            ?? throw new BillingException(404, $"There is no plan with handle '{handle}'.");
        if (!IsAvailable(product))
            throw new BillingException(422, $"The plan '{handle}' is not available for subscription without card details.");
        return product;
    }

    private async Task<int> EnsureFamilyAsync(string familyName, CancellationToken ct)
    {
        async Task<int?> Find()
        {
            var families = await Call("list product families", () => _maxio.ProductFamilies.ListProductFamilies(
                new ListProductFamiliesRequest(), cancellationToken: ct));
            return families.Select(f => f.ProductFamily)
                .FirstOrDefault(f => f?.ArchivedAt == null && string.Equals(f?.Name, familyName, StringComparison.OrdinalIgnoreCase))?.Id;
        }

        if (await Find() is { } existing) return existing;
        try
        {
            var created = await Call("create the brand's product family", () => _maxio.ProductFamilies.CreateProductFamily(
                new CreateProductFamilyOperationRequest
                {
                    Body = new CreateProductFamilyRequest
                    {
                        ProductFamily = new CreateProductFamily { Name = familyName, Handle = Slug(familyName) }
                    }
                }, cancellationToken: ct));
            return created.ProductFamily?.Id ?? throw new BillingException(502, "The billing system returned no product family.");
        }
        catch (BillingException)
        {
            // Possibly lost a race with a concurrent request that created the same family.
            if (await Find() is { } raced) return raced;
            throw;
        }
    }

    private static string Reference(string userName)
    {
        var reference = userName?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(reference))
            throw new BillingException(401, "The caller's identity could not be determined.");
        return reference;
    }

    private async Task<int> EnsureCustomerAsync(string userName, CancellationToken ct)
    {
        var reference = Reference(userName);
        try
        {
            var existing = await _maxio.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = reference }, cancellationToken: ct);
            if (existing.Customer.Id is { } id) return id;
        }
        catch (ApiException e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // first purchase: fall through and create the customer
        }
        catch (ApiException e)
        {
            throw Map("look up the customer", e);
        }
        catch (SdkException e)
        {
            throw MapTransport("look up the customer", e);
        }

        var at = reference.IndexOf('@');
        var local = at > 0 ? reference[..at] : reference;
        var email = at > 0 ? reference : $"{Slug(reference)}@shoppers.eshoponweb.invalid";
        var created = await Call("create the customer", () => _maxio.Customers.CreateCustomer(new CreateCustomerOperationRequest
        {
            Body = new CreateCustomerRequest
            {
                Customer = new CreateCustomer { FirstName = local, LastName = "Shopper", Email = email, Reference = reference }
            }
        }, cancellationToken: ct));
        return created.Customer.Id ?? throw new BillingException(502, "The billing system returned no customer id.");
    }

    private async Task<Subscription> GetOwnedAsync(string userName, int subscriptionId, CancellationToken ct)
    {
        var reference = Reference(userName);
        var sub = (await Call("read the subscription", () => _maxio.Subscriptions.ReadSubscription(
            new ReadSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: ct))).Subscription;
        // Someone else's subscription looks exactly like a missing one.
        if (!string.Equals(sub.Customer?.Reference, reference, StringComparison.OrdinalIgnoreCase))
            throw new BillingException(404, "Subscription not found.");
        return sub;
    }

    private async Task<Subscription> ReactivateAsync(int subscriptionId, CancellationToken ct)
    {
        var result = await Call("restart the subscription", () => _maxio.SubscriptionStatus.ReactivateSubscription(
            new ReactivateSubscriptionOperationRequest { SubscriptionId = subscriptionId }, cancellationToken: ct));
        return result.Subscription;
    }

    /// <summary>Money still owed on the subscription: the larger of its balance and its open invoices' amount due.</summary>
    private async Task<decimal> AmountOwedAsync(Subscription sub, CancellationToken ct)
    {
        var fromBalance = (sub.BalanceInCents ?? 0) / 100m;
        decimal fromInvoices = 0;
        for (var page = 1; ; page++)
        {
            var p = page;
            var batch = await Call("read the subscription's invoices", () => _maxio.Invoices.ListInvoices(
                new ListInvoicesRequest { SubscriptionId = sub.Id, Status = InvoiceStatus.Open, Page = p, PerPage = PageSize }, cancellationToken: ct));
            foreach (var invoice in batch.Invoices)
            {
                if (decimal.TryParse(invoice.DueAmount, NumberStyles.Float, CultureInfo.InvariantCulture, out var due) && due > 0)
                    fromInvoices += due;
            }
            if (batch.Invoices.Count < PageSize) break;
        }
        return Math.Max(Math.Max(fromBalance, fromInvoices), 0);
    }

    private async Task<SubscriptionDto> ToDtoAsync(Subscription sub, CancellationToken ct)
    {
        var owed = await AmountOwedAsync(sub, ct);
        return new SubscriptionDto
        {
            Id = sub.Id ?? 0,
            State = sub.State?.Value,
            Plan = sub.Product == null ? null : ToPlan(sub.Product, await BrandNamesAsync()),
            Paid = owed <= 0,
            AmountOwed = owed,
            NextBillingAt = sub.NextAssessmentAt,
            CreatedAt = sub.CreatedAt,
            CanceledAt = sub.CanceledAt,
        };
    }

    // ---------------------------------------------------------------- error translation

    private static async Task<T> Call<T>(string action, Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (ApiException e)
        {
            throw Map(action, e);
        }
        catch (SdkException e)
        {
            throw MapTransport(action, e);
        }
    }

    private static BillingException Map(string action, ApiException e)
    {
        var status = (int)e.StatusCode;
        return status switch
        {
            404 => new BillingException(404, $"Could not {action}: not found.", e),
            409 => new BillingException(409, $"Could not {action}: the billing system reports a conflict.", e),
            400 or 422 => new BillingException(422, $"Could not {action}: the billing system rejected the request.", e),
            429 => new BillingException(503, "The billing system is busy; try again shortly.", e),
            401 or 403 => new BillingException(502, "The billing system rejected this application's credentials.", e),
            _ => new BillingException(502, $"Could not {action}: the billing system failed to answer.", e),
        };
    }

    private static BillingException MapTransport(string action, SdkException e) =>
        e is SdkTimeoutException
            ? new BillingException(504, $"Could not {action}: the billing system timed out.", e)
            : new BillingException(502, $"Could not {action}: the billing system is unreachable.", e);
}
