using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// The shop's subscription billing, with Maxio Advanced Billing as the system of record.
/// Every failure the app can explain is raised as a <see cref="BillingException"/> (a 4xx/5xx with a reason).
/// </summary>
public class BillingService
{
    private const int PAGE_SIZE = 200;
    private const int MAX_PAGES = 50;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly IReadRepository<CatalogBrand> _brands;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SubscribeClaims _claims;
    private readonly ILogger<BillingService> _logger;

    public BillingService(MaxioAdvancedBillingClient client, IReadRepository<CatalogBrand> brands,
        UserManager<ApplicationUser> userManager, SubscribeClaims claims, ILogger<BillingService> logger)
    {
        _client = client;
        _brands = brands;
        _userManager = userManager;
        _claims = claims;
        _logger = logger;
    }

    public static string BrandFamilyHandle(int brandId) => $"catalog-brand-{brandId}";

    // ---------------------------------------------------------------- plans

    /// <summary>The plans the shop offers: live Maxio products in the product family of a catalog brand.</summary>
    public async Task<List<PlanDto>> ListPlansAsync()
    {
        var brands = await _brands.ListAsync();
        var brandByFamilyHandle = brands.ToDictionary(b => BrandFamilyHandle(b.Id), StringComparer.OrdinalIgnoreCase);

        var families = await Call(() => _client.ProductFamilies.ListProductFamilies(new ListProductFamiliesRequest()));
        var familyBrand = new Dictionary<int, CatalogBrand>();
        foreach (var f in families.Select(x => x.ProductFamily))
        {
            if (f?.Id is int id && f.ArchivedAt is null && f.Handle is not null
                && brandByFamilyHandle.TryGetValue(f.Handle, out var brand))
            {
                familyBrand[id] = brand;
            }
        }

        var products = await ReadAllPagesAsync(page => _client.Products.ListProducts(
            new ListProductsRequest { Page = page, PerPage = PAGE_SIZE, IncludeArchived = false }));

        var currency = (await GetSiteAsync()).Currency;
        return products
            .Select(p => p.Product)
            .Where(p => p.ArchivedAt is null && p.Id is not null && p.ProductFamily?.Id is int fid && familyBrand.ContainsKey(fid))
            .Select(p =>
            {
                var brand = familyBrand[p.ProductFamily!.Id!.Value];
                return new PlanDto
                {
                    Id = p.Id!.Value,
                    Handle = p.Handle ?? string.Empty,
                    Name = p.Name ?? string.Empty,
                    Description = p.Description,
                    PriceInCents = p.PriceInCents ?? 0,
                    Price = (p.PriceInCents ?? 0) / 100m,
                    Currency = currency,
                    Interval = p.Interval ?? 1,
                    IntervalUnit = p.IntervalUnit?.Value,
                    BrandId = brand.Id,
                    Brand = brand.Brand,
                };
            })
            .OrderBy(p => p.Brand, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<PlanDto> GetOfferedPlanAsync(string? handle)
    {
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new BillingException(400, "A plan handle is required.");
        }
        var plans = await ListPlansAsync();
        return plans.FirstOrDefault(p => string.Equals(p.Handle, handle.Trim(), StringComparison.OrdinalIgnoreCase))
               ?? throw new BillingException(404, $"Plan '{handle.Trim()}' is not one of the plans on offer.");
    }

    private async Task<PlanDto> GetOfferedPlanAsync(int planId)
    {
        var plans = await ListPlansAsync();
        return plans.FirstOrDefault(p => p.Id == planId)
               ?? throw new BillingException(404, $"Plan {planId} is not one of the plans on offer.");
    }

    // ---------------------------------------------------------------- shopper: subscriptions

    public async Task<SubscriptionDto> SubscribeAsync(string userName, string? planHandle)
    {
        var plan = await GetOfferedPlanAsync(planHandle);

        using var claim = _claims.TryClaim(userName, plan.Handle)
                          ?? throw new BillingException(409, $"A subscription to plan '{plan.Handle}' is already being created for you.");

        var startedAt = DateTimeOffset.UtcNow;
        var site = await GetSiteAsync();
        // No card is collected: bill by remittance (Relationship Invoicing) or invoice (statement sites).
        var method = site.RelationshipInvoicingEnabled == true ? CollectionMethod.Remittance : CollectionMethod.Invoice;

        var customer = await FindCustomerAsync(userName);
        var subscription = new CreateSubscription { ProductHandle = plan.Handle, PaymentCollectionMethod = method };
        if (customer?.Id is int customerId)
        {
            subscription = subscription with { CustomerId = customerId };
        }
        else
        {
            var email = (await _userManager.FindByNameAsync(userName))?.Email
                        ?? (userName.Contains('@') ? userName : $"{userName}@users.eshoponweb.invalid");
            var local = email.Split('@')[0];
            subscription = subscription with
            {
                CustomerAttributes = new CustomerAttributes
                {
                    Reference = userName,
                    Email = email,
                    FirstName = string.IsNullOrWhiteSpace(local) ? "Shopper" : local,
                    LastName = "Shopper",
                },
            };
        }

        try
        {
            var created = await _client.Subscriptions.CreateSubscription(new CreateSubscriptionOperationRequest
            {
                Body = new CreateSubscriptionRequest { Subscription = subscription },
            });
            return await ToDtoAsync(created.Subscription ?? throw new BillingException(502, "The billing system returned no subscription."));
        }
        catch (Exception ex) when (ex is SdkConnectionException or SdkTimeoutException)
        {
            // The connection failed after Maxio may have acted: settle the outcome by looking for the subscription.
            _logger.LogWarning(ex, "Subscribe for {User} to {Plan} had an unknown outcome; reconciling.", userName, plan.Handle);
            var found = await FindRecentSubscriptionAsync(userName, plan.Handle, startedAt);
            if (found is not null)
            {
                return await ToDtoAsync(found);
            }
            throw new BillingException(504, "The billing system did not answer, and no subscription could be confirmed. Check your subscriptions before trying again.", ex);
        }
        catch (SdkException ex)
        {
            throw Refused(ex, "subscribe");
        }
    }

    public async Task<SubscriptionDto> GetSubscriptionAsync(string userName, int subscriptionId)
        => await ToDtoAsync(await GetOwnedSubscriptionAsync(userName, subscriptionId));

    public async Task<SubscriptionDto> PauseAsync(string userName, int subscriptionId)
    {
        var sub = await GetOwnedSubscriptionAsync(userName, subscriptionId);
        if (sub.State == SubscriptionState.OnHold)
        {
            throw new BillingException(409, "The subscription is already paused.");
        }
        RequireNotEnded(sub, "paused");
        var result = await Call(() => _client.SubscriptionStatus.PauseSubscription(new PauseSubscriptionRequest { SubscriptionId = subscriptionId }));
        return await ToDtoAsync(result.Subscription ?? sub);
    }

    public async Task<SubscriptionDto> ResumeAsync(string userName, int subscriptionId)
    {
        var sub = await GetOwnedSubscriptionAsync(userName, subscriptionId);
        if (sub.State != SubscriptionState.OnHold)
        {
            throw new BillingException(409, $"The subscription is {StateName(sub)}; only a paused subscription can be resumed.");
        }
        var result = await Call(() => _client.SubscriptionStatus.ResumeSubscription(new ResumeSubscriptionRequest { SubscriptionId = subscriptionId }));
        return await ToDtoAsync(result.Subscription ?? sub);
    }

    public async Task<SubscriptionDto> CancelAsync(string userName, int subscriptionId)
    {
        var sub = await GetOwnedSubscriptionAsync(userName, subscriptionId);
        if (sub.State == SubscriptionState.Canceled)
        {
            throw new BillingException(409, "The subscription is already canceled.");
        }
        RequireNotEnded(sub, "canceled");
        var result = await Call(() => _client.SubscriptionStatus.CancelSubscription(new CancelSubscriptionRequest { SubscriptionId = subscriptionId }));
        return await ToDtoAsync(result.Subscription ?? sub);
    }

    public async Task<SubscriptionDto> RestartAsync(string userName, int subscriptionId)
    {
        var sub = await GetOwnedSubscriptionAsync(userName, subscriptionId);
        if (sub.State != SubscriptionState.Canceled)
        {
            throw new BillingException(409, $"The subscription is {StateName(sub)}; only a canceled subscription can be restarted.");
        }
        var result = await Call(() => _client.SubscriptionStatus.ReactivateSubscription(new ReactivateSubscriptionOperationRequest { SubscriptionId = subscriptionId }));
        return await ToDtoAsync(result.Subscription ?? sub);
    }

    public async Task<SubscriptionDto> SwitchPlanAsync(string userName, int subscriptionId, string? planHandle)
    {
        var plan = await GetOfferedPlanAsync(planHandle);
        var sub = await GetOwnedSubscriptionAsync(userName, subscriptionId);
        if (string.Equals(sub.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase))
        {
            throw new BillingException(409, $"The subscription is already on plan '{plan.Handle}'.");
        }

        SubscriptionResponse result;
        if (sub.State == SubscriptionState.Active || sub.State == SubscriptionState.Trialing)
        {
            // Maxio migrates only active or trialing subscriptions.
            result = await Call(() => _client.SubscriptionProducts.MigrateSubscriptionProduct(new MigrateSubscriptionProductRequest
            {
                SubscriptionId = subscriptionId,
                Body = new SubscriptionProductMigrationRequest
                {
                    Migration = new SubscriptionProductMigration { ProductHandle = plan.Handle },
                },
            }));
        }
        else
        {
            // Canceled, paused or otherwise inactive: change the product now, without waiting for a renewal.
            result = await Call(() => _client.Subscriptions.UpdateSubscription(new UpdateSubscriptionOperationRequest
            {
                SubscriptionId = subscriptionId,
                Body = new UpdateSubscriptionRequest
                {
                    Subscription = new UpdateSubscription { ProductHandle = plan.Handle, ProductChangeDelayed = false },
                },
            }));
        }

        var updated = result.Subscription ?? throw new BillingException(502, "The billing system returned no subscription.");
        if (!string.Equals(updated.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase))
        {
            throw new BillingException(502, "The billing system did not put the subscription on the new plan.");
        }
        return await ToDtoAsync(updated);
    }

    /// <summary>A shopper's metered usage on one day (the site's calendar day).</summary>
    public async Task<UsageDayDto> GetUsageAsync(string userName, int subscriptionId, string? date)
    {
        if (!DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            throw new BillingException(400, "Query parameter 'date' is required in the form YYYY-MM-DD.");
        }
        day = day.Date;

        var sub = await GetOwnedSubscriptionAsync(userName, subscriptionId);
        var components = await Call(() => _client.SubscriptionComponents.ListSubscriptionComponents(
            new ListSubscriptionComponentsRequest { SubscriptionId = subscriptionId }));

        // Maxio timestamps carry the site's offset; the day is that site-local calendar day.
        var offset = sub.CreatedAt?.Offset ?? TimeSpan.Zero;
        var dayStart = new DateTimeOffset(day, offset);
        var since = dayStart.AddDays(-1);
        var until = dayStart.AddDays(2);

        var result = new UsageDayDto { SubscriptionId = subscriptionId, Date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
        foreach (var c in components.Select(x => x.Component).Where(c => c?.ComponentId is not null))
        {
            // Maxio lists usage only for metered and prepaid components.
            if (c!.Kind != ComponentKind.MeteredComponent && c.Kind != ComponentKind.PrepaidUsageComponent)
            {
                continue;
            }
            var componentId = c.ComponentId!.Value;
            var usages = await ReadAllPagesAsync(page => _client.SubscriptionComponents.ListUsages(new ListUsagesRequest
            {
                SubscriptionIdOrReference = subscriptionId,
                ComponentId = componentId,
                SinceDate = since,
                UntilDate = until,
                Page = page,
                PerPage = PAGE_SIZE,
            }));

            var entry = new ComponentUsageDto { ComponentId = componentId, Name = c.Name, UnitName = c.UnitName, Kind = c.Kind?.Value };
            foreach (var u in usages.Select(x => x.Usage).Where(u => u.CreatedAt is { } at && at.Date == day))
            {
                var quantity = ReadQuantity(u.Quantity);
                entry.Usages.Add(new UsageEntryDto { Id = u.Id, Quantity = quantity, Memo = u.Memo, CreatedAt = u.CreatedAt });
                entry.Quantity += quantity;
            }
            result.Components.Add(entry);
            result.TotalQuantity += entry.Quantity;
        }
        return result;
    }

    // ---------------------------------------------------------------- staff

    public async Task<PlanDto> CreatePlanAsync(string? name, decimal? price, int? brandId, string? brandName)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 255)
        {
            throw new BillingException(400, "A plan name of 1 to 255 characters is required.");
        }
        if (price is null || price < 0 || price > 1_000_000m || decimal.Round(price.Value, 2) != price.Value)
        {
            throw new BillingException(400, "The monthly price must be zero or more, at most 1000000, with at most two decimals.");
        }
        if (brandId is null && string.IsNullOrWhiteSpace(brandName))
        {
            throw new BillingException(400, "A brand (brandId or brand) is required.");
        }

        var brands = await _brands.ListAsync();
        var brand = brandId is not null
            ? brands.FirstOrDefault(b => b.Id == brandId)
            : brands.FirstOrDefault(b => string.Equals(b.Brand, brandName!.Trim(), StringComparison.OrdinalIgnoreCase))
              ?? (int.TryParse(brandName!.Trim(), out var asId) ? brands.FirstOrDefault(b => b.Id == asId) : null);
        if (brand is null)
        {
            throw new BillingException(422, "The brand is not one of the brands in the catalog.");
        }

        var familyId = await GetOrCreateFamilyAsync(brand);

        var handle = Slug(name);
        var existing = await FindProductAsync(handle);
        if (existing is not null)
        {
            if (existing.ProductFamily?.Id == familyId)
            {
                throw new BillingException(409, $"Brand '{brand.Brand}' already has a plan named '{name}'.");
            }
            handle = $"{handle}-{Slug(brand.Brand)}";
            if (await FindProductAsync(handle) is not null)
            {
                throw new BillingException(409, $"A plan with handle '{handle}' already exists.");
            }
        }

        var cents = (long)(price.Value * 100m);
        var created = await Call(() => _client.Products.CreateProduct(new CreateProductRequest
        {
            ProductFamilyId = familyId.ToString(CultureInfo.InvariantCulture),
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
        }));

        var p = created.Product;
        return new PlanDto
        {
            Id = p.Id ?? 0,
            Handle = p.Handle ?? handle,
            Name = p.Name ?? name,
            Description = p.Description,
            PriceInCents = p.PriceInCents ?? cents,
            Price = (p.PriceInCents ?? cents) / 100m,
            Currency = (await GetSiteAsync()).Currency,
            Interval = p.Interval ?? 1,
            IntervalUnit = p.IntervalUnit?.Value,
            BrandId = brand.Id,
            Brand = brand.Brand,
        };
    }

    public async Task<CurrencyPriceDto> SetCurrencyPriceAsync(int planId, string? currency, decimal? price)
    {
        currency = currency?.Trim().ToUpperInvariant();
        if (currency is null || !Regex.IsMatch(currency, "^[A-Z]{3}$"))
        {
            throw new BillingException(400, "A three-letter ISO currency code is required.");
        }
        if (price is null || price <= 0 || price > 1_000_000m || decimal.Round(price.Value, 3) != price.Value)
        {
            throw new BillingException(400, "The price must be greater than zero, at most 1000000, with at most three decimals.");
        }

        await GetOfferedPlanAsync(planId);

        var site = await Call(() => _client.Sites.ReadSite());
        if (string.Equals(site.Site.Currency, currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new BillingException(422, $"{currency} is the site's own currency; its price is the plan's price.");
        }
        var defined = site.Site.NonPrimaryCurrencies ?? Array.Empty<string>();
        if (!defined.Contains(currency, StringComparer.OrdinalIgnoreCase))
        {
            throw new BillingException(422, $"Currency {currency} is not defined on the billing site (defined: {(defined.Count == 0 ? "none besides " + site.Site.Currency : string.Join(", ", defined))}).");
        }

        var product = (await Call(() => _client.Products.ReadProduct(new ReadProductRequest { ProductId = planId }))).Product;
        var pricePointId = product.DefaultProductPricePointId ?? product.ProductPricePointId
                           ?? throw new BillingException(422, "The plan has no price point to price in another currency.");

        var pricePoint = (await Call(() => _client.ProductPricePoints.ReadProductPricePoint(new ReadProductPricePointRequest
        {
            ProductId = planId,
            PricePointId = pricePointId,
            CurrencyPrices = true,
        }))).PricePoint;
        var current = pricePoint.CurrencyPrices?.FirstOrDefault(c => string.Equals(c.Currency, currency, StringComparison.OrdinalIgnoreCase));

        CurrencyPrice? saved;
        if (current?.Id is int existingId)
        {
            saved = await UpdateCurrencyPriceAsync(pricePointId, existingId, price.Value, currency);
        }
        else
        {
            // Maxio's create call takes whole units; a fractional price is set exactly by the update that follows.
            var whole = Math.Max(1, (int)Math.Round(price.Value, MidpointRounding.AwayFromZero));
            var created = await Call(() => _client.ProductPricePoints.CreateProductCurrencyPrices(new CreateProductCurrencyPricesOperationRequest
            {
                ProductPricePointId = pricePointId,
                Body = new CreateProductCurrencyPricesRequest
                {
                    CurrencyPrices = new[] { new CreateProductCurrencyPrice { Currency = currency, Price = whole, Role = CurrencyPriceRole.Baseline } },
                },
            }));
            saved = created.CurrencyPrices.FirstOrDefault(c => string.Equals(c.Currency, currency, StringComparison.OrdinalIgnoreCase));
            if (saved?.Id is int newId && (decimal)whole != price.Value)
            {
                saved = await UpdateCurrencyPriceAsync(pricePointId, newId, price.Value, currency);
            }
        }

        return new CurrencyPriceDto
        {
            PlanId = planId,
            Currency = currency,
            Price = saved?.Price is double d ? (decimal)d : price.Value,
            FormattedPrice = saved?.FormattedPrice,
        };
    }

    private async Task<CurrencyPrice?> UpdateCurrencyPriceAsync(int pricePointId, int currencyPriceId, decimal price, string currency)
    {
        var updated = await Call(() => _client.ProductPricePoints.UpdateProductCurrencyPrices(new UpdateProductCurrencyPricesRequest
        {
            ProductPricePointId = pricePointId,
            Body = new UpdateCurrencyPricesRequest
            {
                CurrencyPrices = new[] { new UpdateCurrencyPrice { Id = currencyPriceId, Price = (double)price } },
            },
        }));
        return updated.CurrencyPrices.FirstOrDefault(c => string.Equals(c.Currency, currency, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<NextBillingDto> SetNextBillingDateAsync(int subscriptionId, string? date)
    {
        if (!DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            throw new BillingException(400, "A date in the form YYYY-MM-DD is required.");
        }
        day = day.Date;

        var sub = (await Call(() => _client.Subscriptions.ReadSubscription(new ReadSubscriptionRequest { SubscriptionId = subscriptionId }))).Subscription
                  ?? throw new BillingException(404, $"Subscription {subscriptionId} was not found.");

        var billable = sub.State == SubscriptionState.Active || sub.State == SubscriptionState.Trialing
                       || sub.State == SubscriptionState.PastDue || sub.State == SubscriptionState.SoftFailure;
        if (!billable)
        {
            throw new BillingException(409, $"The subscription is {StateName(sub)}; only a subscription that is being billed can be given a next billing date.");
        }

        // Maxio shows timestamps in the site's offset; the date is a site-local calendar date, billed at noon.
        var offset = sub.CreatedAt?.Offset ?? TimeSpan.Zero;
        var today = DateTimeOffset.UtcNow.ToOffset(offset).Date;
        if (day <= today)
        {
            throw new BillingException(422, "The next billing date must be a future date.");
        }

        var result = await Call(() => _client.Subscriptions.UpdateSubscription(new UpdateSubscriptionOperationRequest
        {
            SubscriptionId = subscriptionId,
            Body = new UpdateSubscriptionRequest
            {
                Subscription = new UpdateSubscription { NextBillingAt = new DateTimeOffset(day.AddHours(12), offset) },
            },
        }));

        // The response carries no next_billing_at: the new date shows as the end of the current period.
        var updated = result.Subscription ?? throw new BillingException(502, "The billing system returned no subscription.");
        var applied = updated.NextAssessmentAt ?? updated.CurrentPeriodEndsAt;
        if (applied is null || applied.Value.Date != day)
        {
            throw new BillingException(502, "The billing system did not apply the requested billing date.");
        }
        return new NextBillingDto
        {
            SubscriptionId = subscriptionId,
            NextBillingDate = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            NextBillingAt = applied,
        };
    }

    // ---------------------------------------------------------------- helpers

    private async Task<Subscription> GetOwnedSubscriptionAsync(string userName, int subscriptionId)
    {
        Subscription? sub;
        try
        {
            sub = (await _client.Subscriptions.ReadSubscription(new ReadSubscriptionRequest { SubscriptionId = subscriptionId })).Subscription;
        }
        catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            sub = null;
        }
        catch (SdkException ex)
        {
            throw Refused(ex, "read subscription");
        }

        // A subscription that is not yours looks exactly like one that does not exist.
        if (sub is null || !string.Equals(sub.Customer?.Reference, userName, StringComparison.OrdinalIgnoreCase))
        {
            throw new BillingException(404, $"Subscription {subscriptionId} was not found.");
        }
        return sub;
    }

    private async Task<Customer?> FindCustomerAsync(string userName)
    {
        try
        {
            return (await _client.Customers.ReadCustomerByReference(new ReadCustomerByReferenceRequest { Reference = userName })).Customer;
        }
        catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException ex)
        {
            throw Refused(ex, "look up customer");
        }
    }

    private async Task<Subscription?> FindRecentSubscriptionAsync(string userName, string planHandle, DateTimeOffset startedAt)
    {
        try
        {
            var customer = await FindCustomerAsync(userName);
            if (customer?.Id is not int id)
            {
                return null;
            }
            var list = await _client.Subscriptions.ListSubscriptions(new ListSubscriptionsRequest { CustomerId = id, PerPage = 50, Direction = SortingDirection.Desc });
            return list.Select(s => s.Subscription).FirstOrDefault(s => s is not null
                && string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase)
                && s.CreatedAt is { } at && at >= startedAt.AddSeconds(-30));
        }
        catch (Exception ex) when (ex is SdkException or BillingException)
        {
            _logger.LogWarning(ex, "Could not reconcile the subscribe outcome for {User}.", userName);
            return null;
        }
    }

    private async Task<SubscriptionDto> ToDtoAsync(Subscription sub)
    {
        var owed = (sub.BalanceInCents ?? 0) > 0;
        if (!owed && sub.Id is int id)
        {
            // A subscription can owe money on an invoice that is open but not yet in its balance.
            var open = await ReadAllPagesAsync<Invoice>(async page => (await _client.Invoices.ListInvoices(
                new ListInvoicesRequest { SubscriptionId = id, Status = InvoiceStatus.Open, Page = page, PerPage = PAGE_SIZE })).Invoices);
            owed = open.Any(i => decimal.TryParse(i.DueAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out var due) && due > 0);
        }

        return new SubscriptionDto
        {
            Id = sub.Id ?? 0,
            State = sub.State?.Value,
            Paid = !owed,
            BalanceDue = Math.Max(0, sub.BalanceInCents ?? 0) / 100m,
            Currency = sub.Currency,
            PlanId = sub.Product?.Id,
            PlanHandle = sub.Product?.Handle,
            PlanName = sub.Product?.Name,
            NextBillingAt = sub.NextAssessmentAt ?? sub.CurrentPeriodEndsAt,
            CreatedAt = sub.CreatedAt,
            CanceledAt = sub.CanceledAt,
            PaymentCollectionMethod = sub.PaymentCollectionMethod?.Value,
        };
    }

    private async Task<int> GetOrCreateFamilyAsync(CatalogBrand brand)
    {
        var handle = BrandFamilyHandle(brand.Id);
        async Task<int?> Find()
        {
            var families = await Call(() => _client.ProductFamilies.ListProductFamilies(new ListProductFamiliesRequest()));
            return families.Select(f => f.ProductFamily)
                .FirstOrDefault(f => f is not null && f.ArchivedAt is null && string.Equals(f.Handle, handle, StringComparison.OrdinalIgnoreCase))?.Id;
        }

        if (await Find() is int existing)
        {
            return existing;
        }
        try
        {
            var created = await _client.ProductFamilies.CreateProductFamily(new CreateProductFamilyOperationRequest
            {
                Body = new CreateProductFamilyRequest { ProductFamily = new CreateProductFamily { Name = brand.Brand, Handle = handle } },
            });
            return created.ProductFamily?.Id ?? throw new BillingException(502, "The billing system returned no product family.");
        }
        catch (ApiException ex) when ((int)ex.StatusCode == 422)
        {
            // Someone created it between our look and our write.
            return await Find() ?? throw MaxioErrorReader.ToBillingException(ex);
        }
        catch (SdkException ex)
        {
            throw Refused(ex, "create product family");
        }
    }

    private async Task<Product?> FindProductAsync(string handle)
    {
        try
        {
            return (await _client.Products.ReadProductByHandle(new ReadProductByHandleRequest { ApiHandle = handle })).Product;
        }
        catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException ex)
        {
            throw Refused(ex, "look up product");
        }
    }

    private async Task<Site> GetSiteAsync() => (await Call(() => _client.Sites.ReadSite())).Site;

    private async Task<List<T>> ReadAllPagesAsync<T>(Func<int, Task<IReadOnlyList<T>>> readPage)
    {
        var all = new List<T>();
        for (var page = 1; page <= MAX_PAGES; page++)
        {
            var items = await Call(() => readPage(page));
            all.AddRange(items);
            if (items.Count < PAGE_SIZE)
            {
                return all;
            }
        }
        // Never answer with a silently truncated list.
        throw new BillingException(502, "The billing system returned more records than can be read in one request.");
    }

    private async Task<T> Call<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (SdkException ex)
        {
            throw Refused(ex, "call");
        }
    }

    private BillingException Refused(SdkException ex, string what)
    {
        var billingException = MaxioErrorReader.ToBillingException(ex);
        _logger.LogWarning("Maxio {What} was refused: {Status} {Reason}", what, billingException.StatusCode, billingException.Message);
        return billingException;
    }

    private static void RequireNotEnded(Subscription sub, string action)
    {
        if (sub.State == SubscriptionState.Canceled || sub.State == SubscriptionState.Expired)
        {
            throw new BillingException(409, $"The subscription is {StateName(sub)} and cannot be {action}.");
        }
    }

    private static string StateName(Subscription sub) => sub.State?.Value.Replace('_', ' ') ?? "in an unknown state";

    private static decimal ReadQuantity(Quantity1? quantity)
    {
        if (quantity is null)
        {
            return 0;
        }
        if (quantity.TryGetInt(out var i))
        {
            return i;
        }
        return quantity.TryGetString(out var s) && decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : 0;
    }

    private static string Slug(string text)
    {
        var sb = new StringBuilder();
        foreach (var ch in text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                sb.Append(ch);
            }
            else if (sb.Length > 0 && sb[^1] != '-' && ch is ' ' or '-' or '_' or '.')
            {
                sb.Append('-');
            }
        }
        var slug = sb.ToString().Trim('-');
        return slug.Length == 0 ? "plan" : slug.Length > 100 ? slug.Substring(0, 100).Trim('-') : slug;
    }
}
