using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
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
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioBillingService : IMaxioBillingService
{
    private const int PAGE_SIZE = 200;
    private const string BRAND_FAMILY_HANDLE_PREFIX = "catalog-brand-";
    private static readonly Regex CurrencyPattern = new("^[A-Za-z]{3}$", RegexOptions.Compiled);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly IReadRepository<CatalogBrand> _brandRepository;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(MaxioAdvancedBillingClient client, IReadRepository<CatalogBrand> brandRepository,
        ILogger<MaxioBillingService> logger)
    {
        _client = client;
        _brandRepository = brandRepository;
        _logger = logger;
    }

    // ------------------------------------------------------------------ plans

    public Task<IReadOnlyList<PlanDto>> ListPlansAsync(CancellationToken ct) => Run(async () =>
    {
        var products = await ListOfferedProductsAsync(ct);
        return (IReadOnlyList<PlanDto>)products.Select(ToPlanDto).ToList();
    });

    /// <summary>
    /// The plans a shopper may subscribe to: not archived, and not requiring card details,
    /// because shoppers subscribe without giving any.
    /// </summary>
    private async Task<List<Product>> ListOfferedProductsAsync(CancellationToken ct)
    {
        var offered = new List<Product>();
        for (var page = 1; ; page++)
        {
            var batch = await _client.Products.ListProducts(
                new ListProductsRequest { Page = page, PerPage = PAGE_SIZE, IncludeArchived = false }, null, ct);
            offered.AddRange(batch.Select(b => b.Product)
                .Where(p => p.ArchivedAt is null && p.RequireCreditCard != true));
            if (batch.Count < PAGE_SIZE) break;
        }
        return offered;
    }

    private async Task<Product> RequireOfferedPlanAsync(string? planHandle, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
            throw BillingException.BadRequest("The plan handle is required.");
        var plan = (await ListOfferedProductsAsync(ct))
            .FirstOrDefault(p => string.Equals(p.Handle, planHandle.Trim(), StringComparison.OrdinalIgnoreCase));
        if (plan is null)
            throw BillingException.NotFound($"There is no plan '{planHandle}' you can subscribe to.");
        return plan;
    }

    private static PlanDto ToPlanDto(Product p)
    {
        var dto = new PlanDto
        {
            Id = p.Id ?? 0,
            Handle = p.Handle,
            Name = p.Name,
            Description = p.Description,
            Price = (p.PriceInCents ?? 0) / 100m,
            Interval = p.Interval is null ? null : $"{p.Interval} {p.IntervalUnit?.Value}",
        };
        var family = p.ProductFamily;
        if (family?.Handle is { } handle && handle.StartsWith(BRAND_FAMILY_HANDLE_PREFIX, StringComparison.Ordinal)
            && int.TryParse(handle.AsSpan(BRAND_FAMILY_HANDLE_PREFIX.Length), out var brandId))
        {
            dto.BrandId = brandId;
            dto.Brand = family.Name;
        }
        return dto;
    }

    // ------------------------------------------------------------ subscribe

    public Task<SubscriptionDto> SubscribeAsync(string userName, string planHandle, CancellationToken ct) => Run(async () =>
    {
        var plan = await RequireOfferedPlanAsync(planHandle, ct);
        var reference = Guid.NewGuid().ToString("N");

        var customerId = await FindCustomerIdAsync(userName, ct);
        var subscription = new CreateSubscription
        {
            ProductHandle = plan.Handle,
            // No card details are collected: the shopper is invoiced.
            PaymentCollectionMethod = CollectionMethod.Remittance,
            Reference = reference,
        };
        subscription = customerId is { } id
            ? subscription with { CustomerId = id }
            : subscription with { CustomerAttributes = NewCustomer(userName) };

        try
        {
            var created = await _client.Subscriptions.CreateSubscription(
                new CreateSubscriptionOperationRequest { Body = new CreateSubscriptionRequest { Subscription = subscription } }, null, ct);
            return await ToDtoAsync(created.Subscription!, ct);
        }
        catch (Exception ex) when (ex is SdkConnectionException or SdkTimeoutException)
        {
            // The connection failed after Maxio may have acted: settle the outcome by the reference we stamped.
            _logger.LogWarning(ex, "Subscribe to {Plan} had an unknown outcome; looking the subscription up by reference", plan.Handle);
            try
            {
                var found = await _client.Subscriptions.FindSubscription(new FindSubscriptionRequest { Reference = reference }, null, CancellationToken.None);
                if (found.Subscription is { } existing)
                    return await ToDtoAsync(existing, CancellationToken.None);
            }
            catch (ApiException)
            {
                // not found: the subscription was not created
            }
            throw;
        }
    });

    private async Task<int?> FindCustomerIdAsync(string userName, CancellationToken ct)
    {
        try
        {
            var customer = await _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = userName }, null, ct);
            return customer.Customer?.Id;
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private static CustomerAttributes NewCustomer(string userName)
    {
        var email = userName.Contains('@') ? userName : $"{userName}@shopper.invalid";
        var local = email[..email.IndexOf('@')];
        return new CustomerAttributes
        {
            FirstName = string.IsNullOrWhiteSpace(local) ? "Shopper" : local,
            LastName = "Shopper",
            Email = email,
            Reference = userName,
        };
    }

    // ----------------------------------------------------- read, lifecycle

    public Task<SubscriptionDto> GetSubscriptionAsync(string userName, int subscriptionId, CancellationToken ct) => Run(async () =>
        await ToDtoAsync(await GetOwnedSubscriptionAsync(userName, subscriptionId, ct), ct));

    public Task<SubscriptionDto> PauseAsync(string userName, int subscriptionId, CancellationToken ct) => Run(async () =>
    {
        var sub = await GetOwnedSubscriptionAsync(userName, subscriptionId, ct);
        if (sub.State == SubscriptionState.OnHold)
            throw BillingException.Conflict("The subscription is already paused.");
        if (sub.State != SubscriptionState.Active && sub.State != SubscriptionState.Trialing)
            throw BillingException.Conflict($"A subscription that is {sub.State?.Value} cannot be paused.");
        var paused = await _client.SubscriptionStatus.PauseSubscription(
            new PauseSubscriptionRequest { SubscriptionId = subscriptionId }, null, ct);
        return await ToDtoAsync(paused.Subscription!, ct);
    });

    public Task<SubscriptionDto> ResumeAsync(string userName, int subscriptionId, CancellationToken ct) => Run(async () =>
    {
        var sub = await GetOwnedSubscriptionAsync(userName, subscriptionId, ct);
        if (sub.State != SubscriptionState.OnHold)
            throw BillingException.Conflict($"Only a paused subscription can be resumed; this one is {sub.State?.Value}.");
        var resumed = await _client.SubscriptionStatus.ResumeSubscription(
            new ResumeSubscriptionRequest { SubscriptionId = subscriptionId }, null, ct);
        return await ToDtoAsync(resumed.Subscription!, ct);
    });

    public Task<SubscriptionDto> CancelAsync(string userName, int subscriptionId, CancellationToken ct) => Run(async () =>
    {
        var sub = await GetOwnedSubscriptionAsync(userName, subscriptionId, ct);
        if (sub.State == SubscriptionState.Canceled)
            throw BillingException.Conflict("The subscription is already canceled.");
        var canceled = await _client.SubscriptionStatus.CancelSubscription(
            new CancelSubscriptionRequest { SubscriptionId = subscriptionId }, null, ct);
        return await ToDtoAsync(canceled.Subscription!, ct);
    });

    public Task<SubscriptionDto> RestartAsync(string userName, int subscriptionId, CancellationToken ct) => Run(async () =>
    {
        var sub = await GetOwnedSubscriptionAsync(userName, subscriptionId, ct);
        if (sub.State != SubscriptionState.Canceled)
            throw BillingException.Conflict($"Only a canceled subscription can be restarted; this one is {sub.State?.Value}.");
        var restarted = await _client.SubscriptionStatus.ReactivateSubscription(
            new ReactivateSubscriptionOperationRequest { SubscriptionId = subscriptionId }, null, ct);
        return await ToDtoAsync(restarted.Subscription!, ct);
    });

    // ---------------------------------------------------------- switch plan

    public Task<SubscriptionDto> SwitchPlanAsync(string userName, int subscriptionId, string planHandle, CancellationToken ct) => Run(async () =>
    {
        var plan = await RequireOfferedPlanAsync(planHandle, ct);
        var sub = await GetOwnedSubscriptionAsync(userName, subscriptionId, ct);
        if (sub.Product?.Id == plan.Id)
            throw BillingException.Conflict("The subscription is already on that plan.");

        SubscriptionResponse switched;
        if (sub.State == SubscriptionState.Active || sub.State == SubscriptionState.Trialing)
        {
            switched = await _client.SubscriptionProducts.MigrateSubscriptionProduct(
                new MigrateSubscriptionProductRequest
                {
                    SubscriptionId = subscriptionId,
                    Body = new SubscriptionProductMigrationRequest
                    {
                        Migration = new SubscriptionProductMigration { ProductHandle = plan.Handle },
                    },
                }, null, ct);
        }
        else
        {
            // Maxio only migrates active or trialing subscriptions (a canceled or paused one is refused),
            // so for those the product is changed on the subscription itself; its state is left as it is.
            switched = await _client.Subscriptions.UpdateSubscription(
                new UpdateSubscriptionOperationRequest
                {
                    SubscriptionId = subscriptionId,
                    Body = new UpdateSubscriptionRequest
                    {
                        Subscription = new UpdateSubscription { ProductHandle = plan.Handle },
                    },
                }, null, ct);
        }

        var after = switched.Subscription!;
        if (after.Product?.Id != plan.Id)
        {
            // Never report a switch that Maxio did not apply.
            after = await GetSubscriptionByIdAsync(subscriptionId, ct);
            if (after.Product?.Id != plan.Id)
                throw new BillingException(HttpStatusCode.BadGateway, "The billing system did not move the subscription to the new plan.");
        }
        return await ToDtoAsync(after, ct);
    });

    // ----------------------------------------------------------------- usage

    public Task<UsageDto> GetUsageAsync(string userName, int subscriptionId, DateOnly date, CancellationToken ct) => Run(async () =>
    {
        await GetOwnedSubscriptionAsync(userName, subscriptionId, ct);

        var components = await _client.SubscriptionComponents.ListSubscriptionComponents(
            new ListSubscriptionComponentsRequest { SubscriptionId = subscriptionId }, null, ct);
        var metered = components.Select(c => c.Component)
            .Where(c => c is { ComponentId: not null } && (c.Kind == ComponentKind.MeteredComponent || c.Kind == ComponentKind.PrepaidUsageComponent))
            .ToList();

        // since_date / until_date are whole dates (until is inclusive up to midnight), so the window is widened
        // by a day on each side and every record is then kept only if it was created on the requested
        // calendar date. created_at carries the site's own UTC offset, so its Date is the site-local date.
        var since = new DateTimeOffset(date.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var until = new DateTimeOffset(date.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var day = date.ToDateTime(TimeOnly.MinValue);

        var result = new UsageDto { SubscriptionId = subscriptionId, Date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
        foreach (var component in metered)
        {
            var quantity = 0m;
            var records = 0;
            for (var page = 1; ; page++)
            {
                var batch = await _client.SubscriptionComponents.ListUsages(
                    new ListUsagesRequest
                    {
                        SubscriptionIdOrReference = subscriptionId,
                        ComponentId = component!.ComponentId!.Value,
                        SinceDate = since,
                        UntilDate = until,
                        Page = page,
                        PerPage = PAGE_SIZE,
                    }, null, ct);
                foreach (var usage in batch.Select(b => b.Usage))
                {
                    if (usage.CreatedAt is not { } createdAt || createdAt.Date != day) continue;
                    quantity += QuantityOf(usage.Quantity);
                    records++;
                }
                if (batch.Count < PAGE_SIZE) break;
            }
            result.Components.Add(new UsageComponentDto
            {
                ComponentId = component!.ComponentId!.Value,
                Name = component.Name,
                UnitName = component.UnitName,
                Quantity = quantity,
                Records = records,
            });
        }
        return result;
    });

    private static decimal QuantityOf(Quantity1? quantity)
    {
        if (quantity is null) return 0m;
        if (quantity.TryGetInt(out var whole)) return whole;
        if (quantity.TryGetString(out var text)
            && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return 0m;
    }

    // ----------------------------------------------------------------- staff

    public Task<PlanDto> CreatePlanAsync(string name, decimal monthlyPrice, int brandId, string? description, CancellationToken ct) => Run(async () =>
    {
        if (string.IsNullOrWhiteSpace(name))
            throw BillingException.BadRequest("The plan name is required.");
        if (monthlyPrice < 0)
            throw BillingException.BadRequest("The monthly price cannot be negative.");
        var cents = monthlyPrice * 100m;
        if (cents != decimal.Truncate(cents))
            throw BillingException.BadRequest("The monthly price cannot have more than two decimals.");

        var brand = await _brandRepository.GetByIdAsync(brandId, ct);
        if (brand is null)
            throw BillingException.Unprocessable($"Brand {brandId} is not a brand of the catalog.");

        var familyId = await EnsureBrandFamilyAsync(brand, ct);
        var created = await _client.Products.CreateProduct(
            new CreateProductRequest
            {
                ProductFamilyId = familyId.ToString(CultureInfo.InvariantCulture),
                Body = new CreateOrUpdateProductRequest
                {
                    Product = new CreateOrUpdateProduct
                    {
                        Name = name.Trim(),
                        Handle = Slugify(name),
                        Description = string.IsNullOrWhiteSpace(description) ? name.Trim() : description.Trim(),
                        PriceInCents = (long)cents,
                        Interval = 1,
                        IntervalUnit = IntervalUnit.Month,
                        // Shoppers subscribe without card details, so a plan must not require one.
                        RequireCreditCard = false,
                    },
                },
            }, null, ct);
        return ToPlanDto(created.Product);
    });

    private static string Slugify(string name)
    {
        var slug = Regex.Replace(name.Trim().ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        if (slug.Length == 0) slug = "plan";
        return slug.Length > 100 ? slug[..100].TrimEnd('-') : slug;
    }

    /// <summary>A brand is a Maxio product family, found by the handle "catalog-brand-{id}" (created on first use).</summary>
    private async Task<int> EnsureBrandFamilyAsync(CatalogBrand brand, CancellationToken ct)
    {
        var handle = BRAND_FAMILY_HANDLE_PREFIX + brand.Id.ToString(CultureInfo.InvariantCulture);
        var existing = await FindFamilyAsync(handle, ct);
        if (existing is { } id) return id;
        try
        {
            var created = await _client.ProductFamilies.CreateProductFamily(
                new CreateProductFamilyOperationRequest
                {
                    Body = new CreateProductFamilyRequest
                    {
                        ProductFamily = new CreateProductFamily
                        {
                            Name = brand.Brand,
                            Handle = handle,
                            Description = $"Plans for the {brand.Brand} brand",
                        },
                    },
                }, null, ct);
            return created.ProductFamily!.Id!.Value;
        }
        catch (ApiException)
        {
            // Another request may have created the family a moment ago.
            if (await FindFamilyAsync(handle, ct) is { } raced) return raced;
            throw;
        }
    }

    private async Task<int?> FindFamilyAsync(string handle, CancellationToken ct)
    {
        var families = await _client.ProductFamilies.ListProductFamilies(new ListProductFamiliesRequest(), null, ct);
        return families.Select(f => f.ProductFamily)
            .FirstOrDefault(f => f is not null && f.ArchivedAt is null && string.Equals(f.Handle, handle, StringComparison.Ordinal))?.Id;
    }

    public Task<CurrencyPriceDto> SetCurrencyPriceAsync(int planId, string currency, decimal price, CancellationToken ct) => Run(async () =>
    {
        if (string.IsNullOrWhiteSpace(currency) || !CurrencyPattern.IsMatch(currency.Trim()))
            throw BillingException.BadRequest("The currency must be a three-letter ISO code such as EUR.");
        if (price <= 0)
            throw BillingException.BadRequest("The price must be greater than zero.");
        var code = currency.Trim().ToUpperInvariant();

        Product product;
        try
        {
            product = (await _client.Products.ReadProduct(new ReadProductRequest { ProductId = planId }, null, ct)).Product;
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw BillingException.NotFound($"There is no plan {planId}.");
        }
        var pricePointId = product.DefaultProductPricePointId ?? product.ProductPricePointId
            ?? throw BillingException.Unprocessable("The plan has no price point to price in another currency.");

        var pricePoint = (await _client.ProductPricePoints.ReadProductPricePoint(
            new ReadProductPricePointRequest { ProductId = planId, PricePointId = pricePointId, CurrencyPrices = true }, null, ct));
        var existing = pricePoint.PricePoint?.CurrencyPrices?
            .FirstOrDefault(p => string.Equals(p.Currency, code, StringComparison.OrdinalIgnoreCase) && p.Role == CurrencyPriceRole.Baseline);

        CurrencyPrice? saved;
        if (existing?.Id is { } existingId)
        {
            var updated = await _client.ProductPricePoints.UpdateProductCurrencyPrices(
                new UpdateProductCurrencyPricesRequest
                {
                    ProductPricePointId = pricePointId,
                    Body = new UpdateCurrencyPricesRequest
                    {
                        CurrencyPrices = [new UpdateCurrencyPrice { Id = existingId, Price = (double)price }],
                    },
                }, null, ct);
            saved = updated.CurrencyPrices.FirstOrDefault(p => p.Id == existingId) ?? updated.CurrencyPrices.FirstOrDefault();
        }
        else
        {
            if (pricePoint.PricePoint is { TrialPriceInCents: > 0 } or { InitialChargeInCents: > 0 })
                throw BillingException.Unprocessable("The plan has a trial or setup fee; pricing it in another currency needs a price for each of them.");
            if (price != decimal.Truncate(price))
                throw BillingException.Unprocessable("A first price in a new currency must be a whole number of that currency's units.");
            var created = await _client.ProductPricePoints.CreateProductCurrencyPrices(
                new CreateProductCurrencyPricesOperationRequest
                {
                    ProductPricePointId = pricePointId,
                    Body = new CreateProductCurrencyPricesRequest
                    {
                        CurrencyPrices = [new CreateProductCurrencyPrice { Currency = code, Price = (int)price, Role = CurrencyPriceRole.Baseline }],
                    },
                }, null, ct);
            saved = created.CurrencyPrices.FirstOrDefault(p => string.Equals(p.Currency, code, StringComparison.OrdinalIgnoreCase));
        }

        return new CurrencyPriceDto
        {
            PlanId = planId,
            PricePointId = pricePointId,
            Currency = code,
            Price = saved?.Price is { } p ? (decimal)p : price,
        };
    });

    public Task<SubscriptionDto> SetNextBillingDateAsync(int subscriptionId, string date, CancellationToken ct) => Run(async () =>
    {
        var sub = await GetSubscriptionByIdAsync(subscriptionId, ct);
        if (sub.State != SubscriptionState.Active && sub.State != SubscriptionState.Trialing)
            throw BillingException.Conflict($"The billing date of a subscription that is {sub.State?.Value} cannot be set.");

        // A date-only value is noon in the site's time zone, which is what Maxio itself assumes for a bare date;
        // the site's offset is the one its own timestamps carry. A full timestamp is used exactly as given.
        var siteOffset = sub.CurrentPeriodEndsAt?.Offset ?? sub.CreatedAt?.Offset ?? TimeSpan.Zero;
        DateTimeOffset target;
        if (DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            target = new DateTimeOffset(day.ToDateTime(new TimeOnly(12, 0)), siteOffset);
            var todayAtSite = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(siteOffset).DateTime);
            if (day <= todayAtSite)
                throw BillingException.BadRequest("The next billing date must be a future date.");
        }
        else if (DateTimeOffset.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant)
                 && date.Contains('T') && (date.EndsWith('Z') || date.LastIndexOfAny(['+', '-']) > date.IndexOf('T')))
        {
            target = instant;
            if (target <= DateTimeOffset.UtcNow)
                throw BillingException.BadRequest("The next billing date must be in the future.");
        }
        else
        {
            throw BillingException.BadRequest("The date must be formatted YYYY-MM-DD (or a full timestamp with an offset).");
        }

        await _client.Subscriptions.UpdateSubscription(
            new UpdateSubscriptionOperationRequest
            {
                SubscriptionId = subscriptionId,
                Body = new UpdateSubscriptionRequest { Subscription = new UpdateSubscription { NextBillingAt = target } },
            }, null, ct);

        // The update answer carries no next_billing_at: the new date shows as current_period_ends_at.
        var after = await GetSubscriptionByIdAsync(subscriptionId, ct);
        var applied = after.CurrentPeriodEndsAt;
        if (applied is not { } a || (Math.Abs((a - target).TotalMinutes) >= 1 && a.Date != target.ToOffset(a.Offset).Date))
            throw new BillingException(HttpStatusCode.BadGateway, "The billing system did not apply the new billing date.");
        return await ToDtoAsync(after, ct);
    });

    // --------------------------------------------------------------- helpers

    private async Task<Subscription> GetSubscriptionByIdAsync(int subscriptionId, CancellationToken ct)
    {
        try
        {
            var response = await _client.Subscriptions.ReadSubscription(
                new ReadSubscriptionRequest { SubscriptionId = subscriptionId }, null, ct);
            return response.Subscription ?? throw BillingException.NotFound("Subscription not found.");
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw BillingException.NotFound("Subscription not found.");
        }
    }

    /// <summary>A shopper only ever reaches their own subscriptions (those of the Maxio customer named by their JWT identity).</summary>
    private async Task<Subscription> GetOwnedSubscriptionAsync(string userName, int subscriptionId, CancellationToken ct)
    {
        var sub = await GetSubscriptionByIdAsync(subscriptionId, ct);
        if (!string.Equals(sub.Customer?.Reference, userName, StringComparison.OrdinalIgnoreCase))
            throw BillingException.NotFound("Subscription not found.");
        return sub;
    }

    private async Task<SubscriptionDto> ToDtoAsync(Subscription sub, CancellationToken ct)
    {
        var balance = (sub.BalanceInCents ?? 0) / 100m;
        return new SubscriptionDto
        {
            Id = sub.Id ?? 0,
            State = sub.State?.Value,
            PlanHandle = sub.Product?.Handle,
            PlanName = sub.Product?.Name,
            PlanId = sub.Product?.Id,
            Price = sub.ProductPriceInCents is { } cents ? cents / 100m : null,
            Currency = sub.Currency,
            NextBillingAt = sub.CurrentPeriodEndsAt,
            CanceledAt = sub.CanceledAt,
            Balance = balance,
            Paid = await IsPaidAsync(sub, ct),
        };
    }

    /// <summary>Paid means nothing is owed: no balance on the subscription and no open invoice with an amount due.</summary>
    private async Task<bool> IsPaidAsync(Subscription sub, CancellationToken ct)
    {
        if ((sub.BalanceInCents ?? 0) > 0) return false;
        if (sub.Id is not { } id) return true;
        for (var page = 1; ; page++)
        {
            var invoices = await _client.Invoices.ListInvoices(
                new ListInvoicesRequest { SubscriptionId = id, Status = InvoiceStatus.Open, Page = page, PerPage = PAGE_SIZE }, null, ct);
            foreach (var invoice in invoices.Invoices)
            {
                if (decimal.TryParse(invoice.DueAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out var due) && due > 0)
                    return false;
            }
            if (invoices.Invoices.Count < PAGE_SIZE) return true;
        }
    }

    private async Task<T> Run<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (SdkException ex)
        {
            var translated = MaxioErrorReader.Translate(ex);
            _logger.LogWarning(ex, "Maxio call failed: {Status} {Reason}", (int)translated.StatusCode, translated.Message);
            throw translated;
        }
    }
}
