using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Models;
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
/// Subscription billing on top of Maxio (the system of record). Nothing is persisted locally:
/// a shopper is a Maxio customer whose reference is the JWT identity.
/// </summary>
public class MaxioBillingService
{
    private const int PAGE_SIZE = 200;

    private readonly MaxioClientFactory _clients;
    private readonly IReadRepository<CatalogBrand> _brands;

    public MaxioBillingService(MaxioClientFactory clients, IReadRepository<CatalogBrand> brands)
    {
        _clients = clients;
        _brands = brands;
    }

    private MaxioAdvancedBillingClient Client => _clients.GetClient();

    // ---------------------------------------------------------------- plans

    public Task<List<PlanDto>> ListPlansAsync() => MaxioErrors.Run(async () =>
    {
        var brandNames = await BrandNamesAsync();
        var currency = (await Client.Sites.ReadSite()).Site?.Currency;
        var plans = new List<PlanDto>();
        for (var page = 1; ; page++)
        {
            var batch = await Client.Products.ListProducts(new ListProductsRequest { Page = page, PerPage = PAGE_SIZE });
            plans.AddRange(batch.Select(b => b.Product).Where(p => p.ArchivedAt == null).Select(p => ToPlan(p, brandNames, currency)));
            if (batch.Count < PAGE_SIZE) break;
        }
        return plans.OrderBy(p => p.Price).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    });

    private async Task<Product> FindPlanAsync(string? handle)
    {
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw BillingException.BadRequest("A plan handle is required.");
        }

        Product product;
        try
        {
            product = (await MaxioErrors.Run(() => Client.Products.ReadProductByHandle(
                new ReadProductByHandleRequest { ApiHandle = handle.Trim() }))).Product;
        }
        catch (BillingException ex) when (ex.StatusCode == 404)
        {
            throw BillingException.NotFound($"There is no plan with handle '{handle}'.");
        }

        if (product.ArchivedAt != null)
        {
            throw BillingException.Conflict($"Plan '{handle}' is no longer available.");
        }
        return product;
    }

    private async Task<HashSet<string>> BrandNamesAsync() =>
        (await _brands.ListAsync()).Select(b => b.Brand).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static PlanDto ToPlan(Product p, ISet<string> brandNames, string? currency)
    {
        // The plan's brand is stored in the product's accounting code in Maxio.
        var brand = !string.IsNullOrWhiteSpace(p.AccountingCode)
            ? brandNames.FirstOrDefault(b => string.Equals(b, p.AccountingCode, StringComparison.OrdinalIgnoreCase))
            : null;
        return new PlanDto
        {
            Id = p.Id ?? 0,
            Handle = p.Handle,
            Name = p.Name,
            Description = p.Description,
            Price = (p.PriceInCents ?? 0) / 100m,
            Currency = currency,
            Interval = p.Interval ?? 0,
            IntervalUnit = p.IntervalUnit?.Value,
            Brand = brand,
        };
    }

    // ---------------------------------------------------------- subscribing

    public Task<SubscriptionDto> SubscribeAsync(string user, string? planHandle) => MaxioErrors.Run(async () =>
    {
        var plan = await FindPlanAsync(planHandle);
        var customerId = await EnsureCustomerAsync(user);

        var created = await Client.Subscriptions.CreateSubscription(new CreateSubscriptionOperationRequest
        {
            Body = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = plan.Handle,
                    CustomerId = customerId,
                    // No card on file: the shopper is invoiced instead of charged automatically.
                    PaymentCollectionMethod = CollectionMethod.Remittance,
                },
            },
        });
        return await ToSubscriptionDtoAsync(created.Subscription!);
    });

    private async Task<int> EnsureCustomerAsync(string user)
    {
        try
        {
            var existing = await MaxioErrors.Run(() => Client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = user }));
            if (existing.Customer?.Id is { } id) return id;
        }
        catch (BillingException ex) when (ex.StatusCode == 404)
        {
            // first purchase: fall through and create the customer
        }

        var email = user.Contains('@') ? user : $"{user}@shoppers.eshop.local";
        var local = user.Contains('@') ? user[..user.IndexOf('@')] : user;
        var created = await Client.Customers.CreateCustomer(new CreateCustomerOperationRequest
        {
            Body = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = local,
                    LastName = "Shopper",
                    Email = email,
                    Reference = user,
                },
            },
        });
        return created.Customer!.Id!.Value;
    }

    // ------------------------------------------------------- subscription page

    public Task<SubscriptionDto> GetSubscriptionAsync(string user, int subscriptionId) => MaxioErrors.Run(async () =>
        await ToSubscriptionDtoAsync(await LoadOwnedAsync(user, subscriptionId)));

    /// <summary>Loads a subscription; unless <paramref name="user"/> is null it must belong to that shopper.</summary>
    private async Task<Subscription> LoadOwnedAsync(string? user, int subscriptionId)
    {
        Subscription sub;
        try
        {
            sub = (await MaxioErrors.Run(() => Client.Subscriptions.ReadSubscription(
                new ReadSubscriptionRequest { SubscriptionId = subscriptionId }))).Subscription
                  ?? throw BillingException.NotFound($"Subscription {subscriptionId} was not found.");
        }
        catch (BillingException ex) when (ex.StatusCode == 404)
        {
            throw BillingException.NotFound($"Subscription {subscriptionId} was not found.");
        }

        if (user != null && !string.Equals(sub.Customer?.Reference, user, StringComparison.OrdinalIgnoreCase))
        {
            // Do not reveal other shoppers' subscriptions.
            throw BillingException.NotFound($"Subscription {subscriptionId} was not found.");
        }
        return sub;
    }

    private async Task<SubscriptionDto> ToSubscriptionDtoAsync(Subscription sub)
    {
        var brandNames = await BrandNamesAsync();
        var owed = await AmountOwedAsync(sub);
        return new SubscriptionDto
        {
            Id = sub.Id ?? 0,
            State = sub.State?.Value,
            Paid = owed <= 0,
            AmountOwed = Math.Max(owed, 0),
            Currency = sub.Currency,
            Plan = sub.Product == null ? null : ToPlan(sub.Product, brandNames, sub.Currency),
            NextBillingAt = sub.NextAssessmentAt,
            CurrentPeriodEndsAt = sub.CurrentPeriodEndsAt,
            CreatedAt = sub.CreatedAt,
            CanceledAt = sub.CanceledAt,
            PausedAt = sub.OnHoldAt,
        };
    }

    /// <summary>What the shopper still owes on the subscription: unpaid invoices, or a positive balance.</summary>
    private async Task<decimal> AmountOwedAsync(Subscription sub)
    {
        decimal fromInvoices = 0;
        for (var page = 1; ; page++)
        {
            var batch = await Client.Invoices.ListInvoices(new ListInvoicesRequest
            {
                SubscriptionId = sub.Id,
                Page = page,
                PerPage = PAGE_SIZE,
            });
            foreach (var invoice in batch.Invoices)
            {
                if (invoice.Status == InvoiceStatus.Voided || invoice.Status == InvoiceStatus.Canceled ||
                    invoice.Status == InvoiceStatus.Draft || invoice.Status == InvoiceStatus.Paid)
                {
                    continue;
                }
                fromInvoices += ParseMoney(invoice.DueAmount);
            }
            if (batch.Invoices.Count < PAGE_SIZE) break;
        }

        var balance = (sub.BalanceInCents ?? 0) / 100m;
        return Math.Max(fromInvoices, balance);
    }

    private static decimal ParseMoney(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : 0m;

    // --------------------------------------------------------- pause / resume

    private static bool IsEnded(Subscription s) =>
        s.State == SubscriptionState.Canceled || s.State == SubscriptionState.Expired;

    public Task<SubscriptionDto> PauseAsync(string user, int subscriptionId) => MaxioErrors.Run(async () =>
    {
        var sub = await LoadOwnedAsync(user, subscriptionId);
        if (sub.State == SubscriptionState.OnHold)
            throw BillingException.Conflict("The subscription is already paused.");
        if (IsEnded(sub))
            throw BillingException.Conflict($"A {sub.State} subscription cannot be paused.");

        var res = await Client.SubscriptionStatus.PauseSubscription(
            new PauseSubscriptionRequest { SubscriptionId = subscriptionId, Body = new PauseRequest() });
        return await ToSubscriptionDtoAsync(res.Subscription!);
    });

    public Task<SubscriptionDto> ResumeAsync(string user, int subscriptionId) => MaxioErrors.Run(async () =>
    {
        var sub = await LoadOwnedAsync(user, subscriptionId);
        if (sub.State != SubscriptionState.OnHold)
            throw BillingException.Conflict("Only a paused subscription can be resumed.");

        var res = await Client.SubscriptionStatus.ResumeSubscription(
            new ResumeSubscriptionRequest { SubscriptionId = subscriptionId });
        return await ToSubscriptionDtoAsync(res.Subscription!);
    });

    // -------------------------------------------------------- cancel / restart

    public Task<SubscriptionDto> CancelAsync(string user, int subscriptionId) => MaxioErrors.Run(async () =>
    {
        var sub = await LoadOwnedAsync(user, subscriptionId);
        if (IsEnded(sub))
            throw BillingException.Conflict($"The subscription is already {sub.State}.");

        var res = await Client.SubscriptionStatus.CancelSubscription(new CancelSubscriptionRequest
        {
            SubscriptionId = subscriptionId,
            Body = new CancellationRequest { Subscription = new CancellationOptions() },
        });
        return await ToSubscriptionDtoAsync(res.Subscription!);
    });

    public Task<SubscriptionDto> RestartAsync(string user, int subscriptionId) => MaxioErrors.Run(async () =>
    {
        var sub = await LoadOwnedAsync(user, subscriptionId);
        if (sub.State != SubscriptionState.Canceled)
            throw BillingException.Conflict("Only a cancelled subscription can be restarted.");

        var res = await Client.SubscriptionStatus.ReactivateSubscription(
            new ReactivateSubscriptionOperationRequest { SubscriptionId = subscriptionId, Body = new ReactivateSubscriptionRequest() });
        return await ToSubscriptionDtoAsync(res.Subscription!);
    });

    // ------------------------------------------------------------ switch plan

    public Task<SubscriptionDto> SwitchPlanAsync(string user, int subscriptionId, string? planHandle) => MaxioErrors.Run(async () =>
    {
        var sub = await LoadOwnedAsync(user, subscriptionId);
        var plan = await FindPlanAsync(planHandle);

        var samePlan = sub.Product?.Id == plan.Id;
        var wasCancelled = sub.State == SubscriptionState.Canceled;
        if (samePlan && !wasCancelled)
            throw BillingException.Conflict("The subscription is already on that plan.");
        if (sub.State == SubscriptionState.Expired)
            throw BillingException.Conflict("An expired subscription cannot be switched.");

        if (wasCancelled)
        {
            // Maxio only migrates live subscriptions: bring it back first so the shopper ends up on the new plan.
            await Client.SubscriptionStatus.ReactivateSubscription(
                new ReactivateSubscriptionOperationRequest { SubscriptionId = subscriptionId, Body = new ReactivateSubscriptionRequest() });
            if (samePlan)
            {
                return await ToSubscriptionDtoAsync(await LoadOwnedAsync(user, subscriptionId));
            }
        }

        try
        {
            var res = await Client.SubscriptionProducts.MigrateSubscriptionProduct(new MigrateSubscriptionProductRequest
            {
                SubscriptionId = subscriptionId,
                Body = new SubscriptionProductMigrationRequest
                {
                    Migration = new SubscriptionProductMigration { ProductHandle = plan.Handle },
                },
            });
            return await ToSubscriptionDtoAsync(res.Subscription!);
        }
        catch when (wasCancelled)
        {
            // Leave the subscription as the shopper had it rather than half-restarted.
            try
            {
                await Client.SubscriptionStatus.CancelSubscription(new CancelSubscriptionRequest
                {
                    SubscriptionId = subscriptionId,
                    Body = new CancellationRequest { Subscription = new CancellationOptions() },
                });
            }
            catch
            {
                // best effort
            }
            throw;
        }
    });

    // ------------------------------------------------------------------ usage

    public Task<SubscriptionUsageDto> UsageForDayAsync(string user, int subscriptionId, DateTime day) => MaxioErrors.Run(async () =>
    {
        await LoadOwnedAsync(user, subscriptionId);

        var components = await Client.SubscriptionComponents.ListSubscriptionComponents(
            new ListSubscriptionComponentsRequest { SubscriptionId = subscriptionId });

        var result = new SubscriptionUsageDto
        {
            SubscriptionId = subscriptionId,
            Date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        };

        foreach (var c in components.Select(r => r.Component).Where(c => c.Kind == ComponentKind.MeteredComponent && c.ComponentId != null))
        {
            var entry = new ComponentUsageDto
            {
                ComponentId = c.ComponentId!.Value,
                ComponentHandle = c.ComponentHandle,
                ComponentName = c.Name,
                UnitName = c.UnitName,
            };

            // The date filters are midnight-based and the list is paged, so widen the window by a
            // day each side and keep exactly the chosen day.
            var since = new DateTimeOffset(day.AddDays(-1), TimeSpan.Zero);
            var until = new DateTimeOffset(day.AddDays(2), TimeSpan.Zero);
            for (var page = 1; ; page++)
            {
                var batch = await Client.SubscriptionComponents.ListUsages(new ListUsagesRequest
                {
                    SubscriptionIdOrReference = subscriptionId,
                    ComponentId = c.ComponentId.Value,
                    SinceDate = since,
                    UntilDate = until,
                    Page = page,
                    PerPage = PAGE_SIZE,
                });
                foreach (var u in batch.Select(b => b.Usage))
                {
                    // created_at carries the site's UTC offset, so its calendar date is the site's day.
                    if (u.CreatedAt is not { } at || at.Date != day.Date) continue;
                    entry.Usages.Add(new UsageEntryDto
                    {
                        Id = u.Id,
                        Quantity = QuantityOf(u),
                        Memo = u.Memo,
                        CreatedAt = at,
                    });
                }
                if (batch.Count < PAGE_SIZE) break;
            }

            entry.TotalQuantity = entry.Usages.Sum(u => u.Quantity);
            result.Components.Add(entry);
        }

        result.TotalQuantity = result.Components.Sum(c => c.TotalQuantity);
        return result;
    });

    private static decimal QuantityOf(Usage u)
    {
        if (u.Quantity == null) return 0;
        if (u.Quantity.TryGetInt(out var i)) return i;
        if (u.Quantity.TryGetString(out var s) &&
            decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)) return d;
        return 0;
    }

    // ============================================================== staff

    public Task<PlanDto> CreatePlanAsync(string? name, decimal? price, string? brand, int? brandId) => MaxioErrors.Run(async () =>
    {
        if (string.IsNullOrWhiteSpace(name))
            throw BillingException.BadRequest("A plan name is required.");
        if (price is null || price <= 0)
            throw BillingException.BadRequest("The monthly price must be greater than zero.");
        if (decimal.Round(price.Value, 2) != price.Value)
            throw BillingException.BadRequest("The monthly price can have at most two decimal places.");
        if (string.IsNullOrWhiteSpace(brand) && brandId is null)
            throw BillingException.BadRequest("A brand is required.");

        var allBrands = await _brands.ListAsync();
        var brandNames = allBrands.Select(b => b.Brand).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var catalogBrand = !string.IsNullOrWhiteSpace(brand)
            ? allBrands.FirstOrDefault(b => string.Equals(b.Brand, brand.Trim(), StringComparison.OrdinalIgnoreCase))?.Brand
            : allBrands.FirstOrDefault(b => b.Id == brandId)?.Brand;
        if (catalogBrand == null)
            throw BillingException.BadRequest(
                $"'{brand ?? brandId?.ToString()}' is not a brand in the catalog. Choose one of: {string.Join(", ", brandNames.OrderBy(b => b))}.");

        var handle = Slugify(name);
        if (handle.Length == 0)
            throw BillingException.BadRequest("The plan name must contain letters or digits.");

        try
        {
            await MaxioErrors.Run(() => Client.Products.ReadProductByHandle(new ReadProductByHandleRequest { ApiHandle = handle }));
            throw BillingException.Conflict($"A plan with handle '{handle}' already exists.");
        }
        catch (BillingException ex) when (ex.StatusCode == 404)
        {
            // handle is free
        }

        var familyId = await PlanFamilyIdAsync();
        var created = await Client.Products.CreateProduct(new CreateProductRequest
        {
            ProductFamilyId = familyId.ToString(CultureInfo.InvariantCulture),
            Body = new CreateOrUpdateProductRequest
            {
                Product = new CreateOrUpdateProduct
                {
                    Name = name.Trim(),
                    Handle = handle,
                    Description = $"{name.Trim()} ({catalogBrand})",
                    AccountingCode = catalogBrand,
                    PriceInCents = (long)(price.Value * 100m),
                    Interval = 1,
                    IntervalUnit = IntervalUnit.Month,
                    RequireCreditCard = false,
                },
            },
        });
        return ToPlan(created.Product, brandNames, (await Client.Sites.ReadSite()).Site?.Currency);
    });

    /// <summary>
    /// Plans live in the site's existing plan family (so they share its metered components);
    /// one is created only on a site that has none.
    /// </summary>
    private async Task<int> PlanFamilyIdAsync()
    {
        var families = await Client.ProductFamilies.ListProductFamilies(new ListProductFamiliesRequest());
        var existing = families.Select(f => f.ProductFamily).Where(f => f.ArchivedAt == null && f.Id != null)
            .OrderBy(f => f.Id).FirstOrDefault();
        if (existing != null) return existing.Id!.Value;

        var created = await Client.ProductFamilies.CreateProductFamily(new CreateProductFamilyOperationRequest
        {
            Body = new CreateProductFamilyRequest
            {
                ProductFamily = new CreateProductFamily { Name = "eShop Subscriptions" },
            },
        });
        return created.ProductFamily!.Id!.Value;
    }

    private static string Slugify(string name)
    {
        var chars = name.Trim().ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray();
        return string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }

    public Task<CurrencyPriceDto> AddCurrencyPriceAsync(int planId, string? currency, decimal? price) => MaxioErrors.Run(async () =>
    {
        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3 || !currency.Trim().All(char.IsAsciiLetter))
            throw BillingException.BadRequest("The currency must be a three-letter ISO 4217 code such as EUR.");
        if (price is null || price <= 0)
            throw BillingException.BadRequest("The price must be greater than zero.");
        if (decimal.Truncate(price.Value) != price.Value)
            throw BillingException.BadRequest("Maxio takes currency prices in whole currency units; send a whole number.");
        var code = currency.Trim().ToUpperInvariant();

        Product product;
        try
        {
            product = (await MaxioErrors.Run(() => Client.Products.ReadProduct(new ReadProductRequest { ProductId = planId }))).Product;
        }
        catch (BillingException ex) when (ex.StatusCode == 404)
        {
            throw BillingException.NotFound($"Plan {planId} was not found.");
        }
        if (product.ArchivedAt != null)
            throw BillingException.Conflict($"Plan {planId} is archived.");
        if (product.DefaultProductPricePointId is not { } pricePointId)
            throw BillingException.Conflict($"Plan {planId} has no default price point to price.");

        var site = (await Client.Sites.ReadSite()).Site;
        if (string.Equals(site?.Currency, code, StringComparison.OrdinalIgnoreCase))
            throw BillingException.Conflict($"{code} is the site's own currency; the plan's price is already in it.");

        var res = await Client.ProductPricePoints.CreateProductCurrencyPrices(new CreateProductCurrencyPricesOperationRequest
        {
            ProductPricePointId = pricePointId,
            Body = new CreateProductCurrencyPricesRequest
            {
                CurrencyPrices =
                [
                    new CreateProductCurrencyPrice { Currency = code, Price = (int)price.Value, Role = CurrencyPriceRole.Baseline },
                ],
            },
        });

        var made = res.CurrencyPrices.FirstOrDefault(c => string.Equals(c.Currency, code, StringComparison.OrdinalIgnoreCase))
                   ?? res.CurrencyPrices.FirstOrDefault();
        return new CurrencyPriceDto
        {
            PlanId = planId,
            Currency = made?.Currency ?? code,
            Price = made?.Price is { } p ? (decimal)p : price.Value,
            FormattedPrice = made?.FormattedPrice,
        };
    });

    public Task<SubscriptionDto> SetNextBillingDateAsync(int subscriptionId, DateTime date) => MaxioErrors.Run(async () =>
    {
        var sub = await LoadOwnedAsync(null, subscriptionId);
        if (IsEnded(sub))
            throw BillingException.Conflict($"A {sub.State} subscription is not billed again.");
        if (sub.State == SubscriptionState.OnHold)
            throw BillingException.Conflict("A paused subscription is not billed; resume it first.");
        if (date.Date <= DateTime.UtcNow.Date)
            throw BillingException.BadRequest("The next billing date must be in the future.");

        // Keep the subscription's usual time of day and the site's UTC offset so the charge lands on the chosen
        // calendar date in the site's time zone.
        var current = sub.NextAssessmentAt;
        var target = current is { } c
            ? new DateTimeOffset(date.Date + c.TimeOfDay, c.Offset)
            : new DateTimeOffset(date.Date.AddHours(12), TimeSpan.Zero);

        var res = await Client.Subscriptions.UpdateSubscription(new UpdateSubscriptionOperationRequest
        {
            SubscriptionId = subscriptionId,
            Body = new UpdateSubscriptionRequest { Subscription = new UpdateSubscription { NextBillingAt = target } },
        });

        var updated = res.Subscription!;
        if (updated.NextAssessmentAt is { } actual && actual.Date != date.Date)
        {
            throw new BillingException(422,
                $"The billing system scheduled the next billing for {actual:yyyy-MM-dd} rather than {date:yyyy-MM-dd}.");
        }
        return await ToSubscriptionDtoAsync(updated);
    });
}
