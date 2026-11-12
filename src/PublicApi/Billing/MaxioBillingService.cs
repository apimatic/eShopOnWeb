using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.AnyOf;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.ProductPricePoints;
using MaxioAdvancedBilling.Requests.Products;
using MaxioAdvancedBilling.Requests.SubscriptionComponents;
using MaxioAdvancedBilling.Requests.SubscriptionInvoiceAccount;
using MaxioAdvancedBilling.Requests.SubscriptionProducts;
using MaxioAdvancedBilling.Requests.Subscriptions;
using MaxioAdvancedBilling.Requests.SubscriptionStatus;
using MaxioAdvancedBilling.Requests.Customers;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using SdkCustomer = MaxioAdvancedBilling.Models.Customer;
using SdkProduct = MaxioAdvancedBilling.Models.Product;
using SdkSubscription = MaxioAdvancedBilling.Models.Subscription;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// The one place that talks to Maxio (the billing system of record). Every operation is bounded by a single
/// total budget, every write takes a claim first, and every provider failure leaves as a BillingException.
/// </summary>
public class MaxioBillingService
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan ClaimExpiry = TimeSpan.FromMinutes(5);
    private const int PageSize = 200;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly CatalogContext _db;
    private readonly IReadRepository<CatalogBrand> _brands;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(MaxioAdvancedBillingClient client, CatalogContext db,
        IReadRepository<CatalogBrand> brands, ILogger<MaxioBillingService> logger)
    {
        _client = client;
        _db = db;
        _brands = brands;
        _logger = logger;
    }

    // ------------------------------------------------------------------ plans

    public async Task<List<PlanDto>> ListPlansAsync(CancellationToken ct)
    {
        using var cts = Bounded(ct);
        var products = await ListOfferedAsync(cts.Token);
        return products.Select(MapPlan).ToList();
    }

    private async Task<List<SdkProduct>> ListOfferedAsync(CancellationToken ct)
    {
        var all = new List<SdkProduct>();
        const int maxPages = 25;
        for (var page = 1; ; page++)
        {
            if (page > maxPages)
                throw new BillingException(502, "The plan list is too large to read completely.");
            var items = await ReadCall(() => _client.Products.ListProducts(
                new ListProductsRequest { Page = page, PerPage = PageSize }, cancellationToken: ct));
            all.AddRange(items.Select(i => i.Product).Where(p => p != null && p.ArchivedAt == null && !string.IsNullOrWhiteSpace(p.Handle))!);
            if (items.Count < PageSize) break;
        }
        return all;
    }

    private static PlanDto MapPlan(SdkProduct p) => new()
    {
        Id = p.Id ?? 0,
        Handle = p.Handle,
        Name = p.Name,
        PriceInCents = p.PriceInCents ?? 0,
        MonthlyPrice = (p.PriceInCents ?? 0) / 100m,
        Brand = p.ProductFamily?.Name,
        Interval = p.Interval,
        IntervalUnit = p.IntervalUnit?.Value,
    };

    private async Task<SdkProduct> RequireOfferedHandleAsync(string? handle, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(handle))
            throw new BillingException(400, "planHandle is required.");
        var offered = await ListOfferedAsync(ct);
        var plan = offered.FirstOrDefault(p => string.Equals(p.Handle, handle.Trim(), StringComparison.OrdinalIgnoreCase));
        if (plan == null)
            throw new BillingException(400, $"'{handle}' is not a plan that can be subscribed to. List the plans with GET /api/plans.");
        return plan;
    }

    // ------------------------------------------------------------- subscribe

    public async Task<SubscriptionDto> SubscribeAsync(string userName, string? planHandle, CancellationToken ct)
    {
        using var cts = Bounded(ct);
        var token = cts.Token;

        var plan = await RequireOfferedHandleAsync(planHandle, token);
        var customerId = await EnsureCustomerAsync(userName, token);
        var site = await ReadCall(() => _client.Sites.ReadSite(cancellationToken: token));
        var method = site.Site?.RelationshipInvoicingEnabled == true ? CollectionMethod.Remittance : CollectionMethod.Invoice;

        var claimKey = $"subscribe:{userName.ToLowerInvariant()}:{plan.Handle}";
        var attempt = await TakeClaimAsync(claimKey);

        var response = await GuardedWrite(claimKey,
            () => _client.Subscriptions.CreateSubscription(new CreateSubscriptionOperationRequest
            {
                Body = new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = plan.Handle,
                        CustomerId = customerId,
                        PaymentCollectionMethod = method,
                        Reference = attempt,
                    }
                }
            }, cancellationToken: token),
            async () =>
            {
                try
                {
                    return await _client.Subscriptions.FindSubscription(
                        new FindSubscriptionRequest { Reference = attempt }, cancellationToken: token);
                }
                catch (ApiException ex) when ((int)ex.StatusCode == 404)
                {
                    return null;
                }
            });

        var sub = response.Subscription ?? throw new BillingException(502, "Maxio returned no subscription.");
        _logger.LogInformation("User {User} subscribed to {Plan}: subscription {Id}", userName, plan.Handle, sub.Id);
        return MapSubscription(sub);
    }

    private async Task<int> EnsureCustomerAsync(string userName, CancellationToken ct)
    {
        var key = "customer:" + userName.ToLowerInvariant();

        var row = await _db.MaxioOperationClaims.AsNoTracking().FirstOrDefaultAsync(c => c.ClaimKey == key, ct);
        if (row?.Result != null && int.TryParse(row.Result, out var known))
            return known;

        var existing = await FindCustomerAsync(userName, ct);
        if (existing != null)
        {
            await RememberCustomerAsync(key, existing.Value);
            return existing.Value;
        }

        string attempt;
        try
        {
            attempt = await TakeClaimAsync(key);
        }
        catch (BillingException)
        {
            // Somebody else is creating this customer right now: wait for the result, then fall back to a lookup.
            for (var i = 0; i < 20; i++)
            {
                await Task.Delay(500, ct);
                _db.ChangeTracker.Clear();
                var r = await _db.MaxioOperationClaims.AsNoTracking().FirstOrDefaultAsync(c => c.ClaimKey == key, ct);
                if (r?.Result != null && int.TryParse(r.Result, out var id)) return id;
            }
            var found = await FindCustomerAsync(userName, ct);
            if (found != null) return found.Value;
            throw new BillingException(409, "Your billing profile is still being created. Try again shortly.");
        }

        var (first, last) = SplitName(userName);
        var response = await GuardedWrite(key,
            () => _client.Customers.CreateCustomer(new CreateCustomerOperationRequest
            {
                Body = new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = first,
                        LastName = last,
                        Email = userName,
                        Reference = userName,
                    }
                }
            }, cancellationToken: ct),
            async () =>
            {
                var id = await FindCustomerAsync(userName, ct);
                return id == null ? null : new CustomerResponse { Customer = new SdkCustomer { Id = id, Reference = userName } };
            },
            releaseOnSuccess: false);

        var customerId = response.Customer?.Id ?? throw new BillingException(502, "Maxio returned no customer.");
        await RememberCustomerAsync(key, customerId);
        _ = attempt;
        return customerId;
    }

    private async Task<int?> FindCustomerAsync(string userName, CancellationToken ct)
    {
        try
        {
            var r = await _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = userName }, cancellationToken: ct);
            return r.Customer?.Id;
        }
        catch (ApiException ex) when ((int)ex.StatusCode == 404)
        {
            return null;
        }
        catch (SdkException ex)
        {
            throw Translate(ex);
        }
    }

    private async Task RememberCustomerAsync(string key, int customerId)
    {
        _db.ChangeTracker.Clear();
        var row = await _db.MaxioOperationClaims.FirstOrDefaultAsync(c => c.ClaimKey == key);
        if (row == null)
        {
            _db.MaxioOperationClaims.Add(new MaxioOperationClaim
            {
                ClaimKey = key,
                Attempt = Guid.NewGuid().ToString("N"),
                Result = customerId.ToString(CultureInfo.InvariantCulture),
                CreatedUtc = DateTime.UtcNow,
            });
        }
        else
        {
            row.Result = customerId.ToString(CultureInfo.InvariantCulture);
        }
        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateException) { _db.ChangeTracker.Clear(); }
    }

    private static (string First, string Last) SplitName(string userName)
    {
        var local = userName.Split('@')[0];
        return (string.IsNullOrWhiteSpace(local) ? "Shopper" : local, "Shopper");
    }

    // ------------------------------------------------------- read subscription

    private async Task<SdkSubscription> LoadOwnedAsync(int subscriptionId, string userName, CancellationToken ct)
    {
        SdkSubscription? sub;
        try
        {
            var r = await ReadCall(() => _client.Subscriptions.ReadSubscription(
                new ReadSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: ct));
            sub = r.Subscription;
        }
        catch (BillingException ex) when (ex.StatusCode == 404)
        {
            throw NotFound();
        }

        // Unknown subscription and someone else's subscription answer identically.
        if (sub == null || !string.Equals(sub.Customer?.Reference, userName, StringComparison.OrdinalIgnoreCase))
            throw NotFound();
        return sub;
    }

    private static BillingException NotFound() => new(404, "Subscription not found.");

    public async Task<SubscriptionDto> GetSubscriptionAsync(int subscriptionId, string userName, CancellationToken ct)
    {
        using var cts = Bounded(ct);
        var sub = await LoadOwnedAsync(subscriptionId, userName, cts.Token);
        var balances = await ReadCall(() => _client.SubscriptionInvoiceAccount.ReadAccountBalances(
            new ReadAccountBalancesRequest { SubscriptionId = subscriptionId }, cancellationToken: cts.Token));

        // Owes nothing only when neither the open-invoice balance nor the subscription balance is positive.
        var owed = Math.Max(balances.OpenInvoices?.BalanceInCents ?? 0, 0) + Math.Max(sub.BalanceInCents ?? 0, 0);
        var dto = MapSubscription(sub);
        dto.Paid = owed <= 0;
        return dto;
    }

    // ------------------------------------------------------------- lifecycle

    private static string StateOf(SdkSubscription s) => s.State?.Value ?? string.Empty;

    private async Task<SubscriptionDto> LifecycleAsync(int id, string userName, string action,
        Func<string, string?> refuse, Func<CancellationToken, Task<SubscriptionResponse>> write, Func<SdkSubscription, bool> done,
        CancellationToken ct)
    {
        using var cts = Bounded(ct);
        var token = cts.Token;
        var sub = await LoadOwnedAsync(id, userName, token);
        var refusal = refuse(StateOf(sub));
        if (refusal != null) throw new BillingException(409, refusal);

        await TakeClaimAsync($"sub:{id}");
        var response = await GuardedWrite($"sub:{id}",
            () => write(token),
            async () =>
            {
                var r = await _client.Subscriptions.ReadSubscription(new ReadSubscriptionRequest { SubscriptionId = id }, cancellationToken: token);
                return r.Subscription != null && done(r.Subscription) ? r : null;
            });
        _logger.LogInformation("User {User} {Action} subscription {Id}", userName, action, id);
        return MapSubscription(response.Subscription ?? sub);
    }

    public Task<SubscriptionDto> PauseAsync(int id, string userName, CancellationToken ct) =>
        LifecycleAsync(id, userName, "paused",
            s => s is "active" or "trialing" ? null
                : s is "on_hold" or "paused" ? "The subscription is already paused."
                : $"A subscription that is {s} cannot be paused.",
            t => _client.SubscriptionStatus.PauseSubscription(
                new PauseSubscriptionRequest { SubscriptionId = id }, cancellationToken: t),
            s => StateOf(s) is "on_hold" or "paused", ct);

    public Task<SubscriptionDto> ResumeAsync(int id, string userName, CancellationToken ct) =>
        LifecycleAsync(id, userName, "resumed",
            s => s is "on_hold" or "paused" ? null : $"Only a paused subscription can be resumed; this one is {s}.",
            t => _client.SubscriptionStatus.ResumeSubscription(
                new ResumeSubscriptionRequest { SubscriptionId = id }, cancellationToken: t),
            s => StateOf(s) is not ("on_hold" or "paused"), ct);

    public Task<SubscriptionDto> CancelAsync(int id, string userName, CancellationToken ct) =>
        LifecycleAsync(id, userName, "canceled",
            s => s is "canceled" ? "The subscription is already canceled."
                : s is "expired" ? "The subscription has already ended."
                : null,
            t => _client.SubscriptionStatus.CancelSubscription(
                new CancelSubscriptionRequest { SubscriptionId = id }, cancellationToken: t),
            s => StateOf(s) is "canceled", ct);

    public Task<SubscriptionDto> RestartAsync(int id, string userName, CancellationToken ct) =>
        LifecycleAsync(id, userName, "restarted",
            s => s is "canceled" ? null : $"Only a canceled subscription can be restarted; this one is {s}.",
            t => _client.SubscriptionStatus.ReactivateSubscription(
                new ReactivateSubscriptionOperationRequest { SubscriptionId = id }, cancellationToken: t),
            s => StateOf(s) is not "canceled", ct);

    // ---------------------------------------------------------------- switch

    public async Task<SubscriptionDto> SwitchAsync(int id, string userName, string? planHandle, CancellationToken ct)
    {
        using var cts = Bounded(ct);
        var token = cts.Token;
        var sub = await LoadOwnedAsync(id, userName, token);
        var plan = await RequireOfferedHandleAsync(planHandle, token);

        if (string.Equals(sub.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase))
            throw new BillingException(409, "The subscription is already on that plan.");

        var state = StateOf(sub);
        var target = plan.Handle!;
        async Task<SubscriptionResponse?> Settle()
        {
            var r = await _client.Subscriptions.ReadSubscription(new ReadSubscriptionRequest { SubscriptionId = id }, cancellationToken: token);
            return string.Equals(r.Subscription?.Product?.Handle, target, StringComparison.OrdinalIgnoreCase) ? r : null;
        }

        await TakeClaimAsync($"sub:{id}");
        SubscriptionResponse response;
        if (state is "active" or "trialing")
        {
            response = await GuardedWrite($"sub:{id}",
                () => _client.SubscriptionProducts.MigrateSubscriptionProduct(new MigrateSubscriptionProductRequest
                {
                    SubscriptionId = id,
                    Body = new SubscriptionProductMigrationRequest
                    {
                        Migration = new SubscriptionProductMigration { ProductHandle = target }
                    }
                }, cancellationToken: token), Settle);
        }
        else
        {
            // Migration only accepts active/trialing subscriptions; every other state (canceled, paused, ...)
            // gets the plan through a subscription update and keeps its state.
            response = await GuardedWrite($"sub:{id}",
                () => _client.Subscriptions.UpdateSubscription(new UpdateSubscriptionOperationRequest
                {
                    SubscriptionId = id,
                    Body = new UpdateSubscriptionRequest
                    {
                        Subscription = new UpdateSubscription { ProductHandle = target }
                    }
                }, cancellationToken: token), Settle);
        }

        var result = response.Subscription ?? sub;
        if (!string.Equals(result.Product?.Handle, target, StringComparison.OrdinalIgnoreCase))
        {
            var reread = await ReadCall(() => _client.Subscriptions.ReadSubscription(
                new ReadSubscriptionRequest { SubscriptionId = id }, cancellationToken: token));
            result = reread.Subscription ?? result;
            if (!string.Equals(result.Product?.Handle, target, StringComparison.OrdinalIgnoreCase))
                throw new BillingException(422, "Maxio did not move the subscription to the new plan.");
        }
        _logger.LogInformation("User {User} switched subscription {Id} to {Plan}", userName, id, target);
        return MapSubscription(result);
    }

    // ----------------------------------------------------------------- usage

    public async Task<SubscriptionUsageDto> GetUsageAsync(int id, string userName, string? date, CancellationToken ct)
    {
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            throw new BillingException(400, "date is required and must be a real calendar date formatted YYYY-MM-DD.");

        using var cts = Bounded(ct);
        var token = cts.Token;
        await LoadOwnedAsync(id, userName, token);

        var components = await ReadCall(() => _client.SubscriptionComponents.ListSubscriptionComponents(
            new ListSubscriptionComponentsRequest { SubscriptionId = id }, cancellationToken: token));

        var result = new SubscriptionUsageDto { SubscriptionId = id, Date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
        var since = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var until = since.AddDays(1);

        foreach (var c in components.Select(x => x.Component).Where(x => x?.ComponentId != null && x.Kind == ComponentKind.MeteredComponent))
        {
            var dto = new ComponentUsageDto
            {
                ComponentId = c!.ComponentId!.Value,
                ComponentHandle = c.ComponentHandle,
                Name = c.Name,
                UnitName = c.UnitName,
            };
            for (var page = 1; ; page++)
            {
                if (page > 25) throw new BillingException(502, "The usage list is too large to read completely.");
                var usages = await ReadCall(() => _client.SubscriptionComponents.ListUsages(new ListUsagesRequest
                {
                    SubscriptionIdOrReference = id,
                    ComponentId = c.ComponentId!.Value,
                    SinceDate = since,
                    UntilDate = until,
                    Page = page,
                    PerPage = PageSize,
                }, cancellationToken: token));

                foreach (var u in usages.Select(x => x.Usage).Where(x => x != null))
                {
                    // until_date may include midnight of the next day: keep only usage recorded on the picked day.
                    if (u!.CreatedAt != null && DateOnly.FromDateTime(u.CreatedAt.Value.DateTime) != day) continue;
                    var q = ParseQuantity(u.Quantity);
                    dto.Usages.Add(new UsageItemDto { Id = u.Id, Quantity = q, Memo = u.Memo, CreatedAt = u.CreatedAt });
                    dto.TotalQuantity += q;
                }
                if (usages.Count < PageSize) break;
            }
            result.Components.Add(dto);
        }
        return result;
    }

    private static decimal ParseQuantity(Quantity1? q)
    {
        if (q == null) return 0;
        if (q.TryGetInt(out var i)) return i;
        if (q.TryGetString(out var s) && decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
        return 0;
    }

    // ----------------------------------------------------------------- admin

    public async Task<PlanDto> CreatePlanAsync(string? name, decimal? monthlyPrice, string? brand, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new BillingException(400, "name is required.");
        if (monthlyPrice == null || monthlyPrice <= 0) throw new BillingException(400, "monthlyPrice must be greater than zero.");
        if (decimal.Round(monthlyPrice.Value, 2) != monthlyPrice.Value) throw new BillingException(400, "monthlyPrice can have at most two decimals.");
        if (string.IsNullOrWhiteSpace(brand)) throw new BillingException(400, "brand is required.");

        var catalogBrands = await _brands.ListAsync(ct);
        var match = catalogBrands.FirstOrDefault(b => string.Equals(b.Brand, brand.Trim(), StringComparison.OrdinalIgnoreCase));
        if (match == null)
            throw new BillingException(400, $"'{brand}' is not a brand in the catalog. Valid brands: {string.Join(", ", catalogBrands.Select(b => b.Brand))}.");

        using var cts = Bounded(ct);
        var token = cts.Token;

        var familyHandle = Slug(match.Brand);
        var familyId = await EnsureFamilyAsync(match.Brand, familyHandle, token);

        var handle = Slug(name);
        var key = $"plan:{handle}";
        await TakeClaimAsync(key);
        var response = await GuardedWrite(key,
            () => _client.Products.CreateProduct(new CreateProductRequest
            {
                ProductFamilyId = familyId.ToString(CultureInfo.InvariantCulture),
                Body = new CreateOrUpdateProductRequest
                {
                    Product = new CreateOrUpdateProduct
                    {
                        Name = name.Trim(),
                        Handle = handle,
                        Description = name.Trim(),
                        PriceInCents = (long)decimal.Round(monthlyPrice.Value * 100m),
                        Interval = 1,
                        IntervalUnit = IntervalUnit.Month,
                        RequireCreditCard = false,
                    }
                }
            }, cancellationToken: token),
            async () =>
            {
                try { return await _client.Products.ReadProductByHandle(new ReadProductByHandleRequest { ApiHandle = handle }, cancellationToken: token); }
                catch (ApiException ex) when ((int)ex.StatusCode == 404) { return null; }
            });

        var product = response.Product ?? throw new BillingException(502, "Maxio returned no product.");
        return MapPlan(product);
    }

    private async Task<int> EnsureFamilyAsync(string brand, string handle, CancellationToken ct)
    {
        async Task<int?> Find()
        {
            var families = await ReadCall(() => _client.ProductFamilies.ListProductFamilies(new ListProductFamiliesRequest(), cancellationToken: ct));
            return families.Select(f => f.ProductFamily)
                .FirstOrDefault(f => f != null && (string.Equals(f.Handle, handle, StringComparison.OrdinalIgnoreCase)
                                                   || string.Equals(f.Name, brand, StringComparison.OrdinalIgnoreCase)))?.Id;
        }

        var existing = await Find();
        if (existing != null) return existing.Value;

        var key = $"family:{handle}";
        try
        {
            await TakeClaimAsync(key);
        }
        catch (BillingException)
        {
            // Another admin is creating it; use theirs.
            await Task.Delay(1000, ct);
            return await Find() ?? throw new BillingException(409, "The brand is being set up. Try again shortly.");
        }

        var response = await GuardedWrite(key,
            () => _client.ProductFamilies.CreateProductFamily(new CreateProductFamilyOperationRequest
            {
                Body = new CreateProductFamilyRequest
                {
                    ProductFamily = new CreateProductFamily { Name = brand, Handle = handle }
                }
            }, cancellationToken: ct),
            async () =>
            {
                var id = await Find();
                return id == null ? null : new ProductFamilyResponse { ProductFamily = new ProductFamily { Id = id, Name = brand, Handle = handle } };
            });
        return response.ProductFamily?.Id ?? throw new BillingException(502, "Maxio returned no product family.");
    }

    public async Task<CurrencyPriceDto> SetCurrencyPriceAsync(int planId, string? currency, decimal? price, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(currency) || !Regex.IsMatch(currency.Trim(), "^[A-Za-z]{3}$"))
            throw new BillingException(400, "currency must be a three-letter code such as EUR.");
        if (price == null || price <= 0) throw new BillingException(400, "price must be greater than zero.");
        var code = currency.Trim().ToUpperInvariant();

        using var cts = Bounded(ct);
        var token = cts.Token;

        var offered = await ListOfferedAsync(token);
        if (offered.All(p => p.Id != planId))
            throw new BillingException(404, $"Plan {planId} was not found.");

        var site = (await ReadCall(() => _client.Sites.ReadSite(cancellationToken: token))).Site;
        if (string.Equals(site?.Currency, code, StringComparison.OrdinalIgnoreCase))
            throw new BillingException(422, $"{code} is the site's primary currency; the plan's own price is already in it.");
        if (site?.NonPrimaryCurrencies == null || !site.NonPrimaryCurrencies.Any(c => string.Equals(c, code, StringComparison.OrdinalIgnoreCase)))
            throw new BillingException(422, $"{code} is not a currency configured on the billing site.");

        var product = (await ReadCall(() => _client.Products.ReadProduct(new ReadProductRequest { ProductId = planId }, cancellationToken: token))).Product;
        var pricePointId = product?.DefaultProductPricePointId
            ?? throw new BillingException(422, "The plan has no default price point to price.");

        var pricePoints = await ReadCall(() => _client.ProductPricePoints.ListProductPricePoints(new ListProductPricePointsRequest
        {
            ProductId = planId,
            CurrencyPrices = true,
            PerPage = PageSize,
        }, cancellationToken: token));
        var pricePoint = pricePoints.PricePoints?.FirstOrDefault(p => p.Id == pricePointId);
        var existing = pricePoint?.CurrencyPrices?.FirstOrDefault(c => string.Equals(c.Currency, code, StringComparison.OrdinalIgnoreCase));

        var key = $"currency:{planId}:{code}";
        await TakeClaimAsync(key);

        async Task<CurrencyPricesResponse?> Settle()
        {
            var r = await _client.ProductPricePoints.ListProductPricePoints(new ListProductPricePointsRequest
            {
                ProductId = planId, CurrencyPrices = true, PerPage = PageSize,
            }, cancellationToken: token);
            var found = r.PricePoints?.FirstOrDefault(p => p.Id == pricePointId)?.CurrencyPrices
                ?.Where(c => string.Equals(c.Currency, code, StringComparison.OrdinalIgnoreCase) && (decimal?)c.Price == price).ToList();
            return found is { Count: > 0 } ? new CurrencyPricesResponse { CurrencyPrices = found } : null;
        }

        if (existing?.Id != null)
        {
            await GuardedWrite(key,
                () => _client.ProductPricePoints.UpdateProductCurrencyPrices(new UpdateProductCurrencyPricesRequest
                {
                    ProductPricePointId = pricePointId,
                    Body = new UpdateCurrencyPricesRequest
                    {
                        CurrencyPrices = new List<UpdateCurrencyPrice> { new() { Id = existing.Id.Value, Price = (double)price.Value } }
                    }
                }, cancellationToken: token), Settle);
        }
        else
        {
            // The create operation takes whole units; a fractional price is created rounded, then corrected by an update.
            var whole = (int)decimal.Round(price.Value, MidpointRounding.AwayFromZero);
            var created = await GuardedWrite(key,
                () => _client.ProductPricePoints.CreateProductCurrencyPrices(new CreateProductCurrencyPricesOperationRequest
                {
                    ProductPricePointId = pricePointId,
                    Body = new CreateProductCurrencyPricesRequest
                    {
                        CurrencyPrices = new List<CreateProductCurrencyPrice>
                        {
                            new() { Currency = code, Price = whole, Role = CurrencyPriceRole.Baseline }
                        }
                    }
                }, cancellationToken: token), async () =>
                {
                    var r = await _client.ProductPricePoints.ListProductPricePoints(new ListProductPricePointsRequest
                    {
                        ProductId = planId, CurrencyPrices = true, PerPage = PageSize,
                    }, cancellationToken: token);
                    var found = r.PricePoints?.FirstOrDefault(p => p.Id == pricePointId)?.CurrencyPrices
                        ?.Where(c => string.Equals(c.Currency, code, StringComparison.OrdinalIgnoreCase)).ToList();
                    return found is { Count: > 0 } ? new CurrencyPricesResponse { CurrencyPrices = found } : null;
                }, releaseOnSuccess: false);

            if (whole != price.Value)
            {
                var created1 = created.CurrencyPrices?.FirstOrDefault(c => string.Equals(c.Currency, code, StringComparison.OrdinalIgnoreCase));
                if (created1?.Id == null)
                {
                    await ReleaseClaimAsync(key);
                    throw new BillingException(502, "Maxio did not return the created currency price.");
                }
                await GuardedWrite(key,
                    () => _client.ProductPricePoints.UpdateProductCurrencyPrices(new UpdateProductCurrencyPricesRequest
                    {
                        ProductPricePointId = pricePointId,
                        Body = new UpdateCurrencyPricesRequest
                        {
                            CurrencyPrices = new List<UpdateCurrencyPrice> { new() { Id = created1.Id.Value, Price = (double)price.Value } }
                        }
                    }, cancellationToken: token), Settle);
            }
            else
            {
                await ReleaseClaimAsync(key);
            }
        }

        _logger.LogInformation("Plan {Plan} priced in {Currency}: {Price}", planId, code, price);
        return new CurrencyPriceDto { PlanId = planId, Currency = code, Price = price.Value };
    }

    public async Task<SubscriptionDto> SetNextBillingDateAsync(int id, string? date, CancellationToken ct)
    {
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            throw new BillingException(400, "date is required and must be a real calendar date formatted YYYY-MM-DD.");
        if (day <= DateOnly.FromDateTime(DateTime.UtcNow))
            throw new BillingException(400, "date must be in the future.");

        using var cts = Bounded(ct);
        var token = cts.Token;

        SdkSubscription sub;
        try
        {
            sub = (await ReadCall(() => _client.Subscriptions.ReadSubscription(
                new ReadSubscriptionRequest { SubscriptionId = id }, cancellationToken: token))).Subscription ?? throw NotFound();
        }
        catch (BillingException ex) when (ex.StatusCode == 404) { throw NotFound(); }

        var state = StateOf(sub);
        if (state is not ("active" or "trialing"))
            throw new BillingException(409, $"A subscription that is {state} is not billed on a date, so its next billing date cannot be set.");

        // Noon UTC keeps the same calendar date for site time zones between UTC-12 and UTC+11.
        var at = new DateTimeOffset(day.Year, day.Month, day.Day, 12, 0, 0, TimeSpan.Zero);
        var key = $"sub:{id}";
        await TakeClaimAsync(key);

        async Task<SubscriptionResponse?> Settle()
        {
            var r = await _client.Subscriptions.ReadSubscription(new ReadSubscriptionRequest { SubscriptionId = id }, cancellationToken: token);
            return Matches(r.Subscription) ? r : null;
        }
        bool Matches(SdkSubscription? s) =>
            s != null && (s.NextAssessmentAt != null && DateOnly.FromDateTime(s.NextAssessmentAt.Value.DateTime) == day
                          || s.CurrentPeriodEndsAt != null && DateOnly.FromDateTime(s.CurrentPeriodEndsAt.Value.DateTime) == day);

        var response = await GuardedWrite(key,
            () => _client.Subscriptions.UpdateSubscription(new UpdateSubscriptionOperationRequest
            {
                SubscriptionId = id,
                Body = new UpdateSubscriptionRequest { Subscription = new UpdateSubscription { NextBillingAt = at } }
            }, cancellationToken: token), Settle);

        var verified = await ReadCall(() => _client.Subscriptions.ReadSubscription(
            new ReadSubscriptionRequest { SubscriptionId = id }, cancellationToken: token));
        if (!Matches(verified.Subscription))
            throw new BillingException(502, "Maxio accepted the change but the next billing date did not move.");

        _logger.LogInformation("Subscription {Id} next billing date set to {Date}", id, day);
        return MapSubscription(verified.Subscription ?? response.Subscription!);
    }

    // --------------------------------------------------------------- plumbing

    private static SubscriptionDto MapSubscription(SdkSubscription s) => new()
    {
        Id = s.Id ?? 0,
        State = s.State?.Value,
        PlanId = s.Product?.Id,
        PlanHandle = s.Product?.Handle,
        PlanName = s.Product?.Name,
        Currency = s.Currency,
        CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
        NextAssessmentAt = s.NextAssessmentAt,
        CanceledAt = s.CanceledAt,
    };

    private static string Slug(string value)
    {
        var sb = new StringBuilder();
        foreach (var ch in value.Trim().ToLowerInvariant())
            sb.Append(char.IsLetterOrDigit(ch) && ch < 128 ? ch : '-');
        return Regex.Replace(sb.ToString(), "-+", "-").Trim('-');
    }

    private static CancellationTokenSource Bounded(CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Budget);
        return cts;
    }

    /// <summary>A read: provider failures become a BillingException.</summary>
    private async Task<T> ReadCall<T>(Func<Task<T>> call)
    {
        var capture = ErrorBodyCapture.Current = new ErrorBodyCapture();
        try
        {
            return await call();
        }
        catch (SdkException ex)
        {
            throw Translate(ex);
        }
        finally
        {
            _ = capture;
        }
    }

    /// <summary>
    /// A write: the caller has already taken the claim. The claim is released when Maxio refuses the call, and kept when the
    /// outcome is unknown and cannot be settled by re-reading (it expires, so it cannot block the key forever).
    /// </summary>
    private async Task<T> GuardedWrite<T>(string claimKey, Func<Task<T>> call, Func<Task<T?>> settle, bool releaseOnSuccess = true)
        where T : class
    {
        ErrorBodyCapture.Current = new ErrorBodyCapture();
        try
        {
            var result = await call();
            if (releaseOnSuccess) await ReleaseClaimAsync(claimKey);
            return result;
        }
        catch (SdkConnectionException ex)
        {
            // The write may still have landed: settle it from Maxio before reporting anything.
            T? found = null;
            try { found = await settle(); }
            catch (SdkException) { /* could not settle: unknown */ }
            if (found != null)
            {
                _logger.LogWarning(ex, "Write {Key} failed in transit but Maxio had applied it", claimKey);
                if (releaseOnSuccess) await ReleaseClaimAsync(claimKey);
                return found;
            }
            _logger.LogError(ex, "Write {Key} has an unknown outcome", claimKey);
            throw new BillingException(504, "Billing did not answer in time and the outcome is unknown. Check the subscription before trying again.", ex);
        }
        catch (SdkException ex)
        {
            await ReleaseClaimAsync(claimKey);
            throw Translate(ex);
        }
        catch
        {
            await ReleaseClaimAsync(claimKey);
            throw;
        }
    }

    private async Task<string> TakeClaimAsync(string key)
    {
        _db.ChangeTracker.Clear();
        var cutoff = DateTime.UtcNow - ClaimExpiry;
        var stale = await _db.MaxioOperationClaims.Where(c => c.ClaimKey == key && c.Result == null && c.CreatedUtc < cutoff).ToListAsync();
        if (stale.Count > 0)
        {
            _db.MaxioOperationClaims.RemoveRange(stale);
            await _db.SaveChangesAsync();
        }

        var attempt = Guid.NewGuid().ToString("N");
        _db.MaxioOperationClaims.Add(new MaxioOperationClaim { ClaimKey = key, Attempt = attempt, CreatedUtc = DateTime.UtcNow });
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
        {
            _db.ChangeTracker.Clear();
            throw new BillingException(409, "The same change is already being processed. Try again in a moment.", ex);
        }
        return attempt;
    }

    private async Task ReleaseClaimAsync(string key)
    {
        _db.ChangeTracker.Clear();
        var row = await _db.MaxioOperationClaims.FirstOrDefaultAsync(c => c.ClaimKey == key);
        if (row == null) return;
        _db.MaxioOperationClaims.Remove(row);
        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateException) { _db.ChangeTracker.Clear(); }
    }

    private BillingException Translate(SdkException ex)
    {
        var capture = ErrorBodyCapture.Current;
        switch (ex)
        {
            case ApiException api:
                var code = (int)api.StatusCode;
                var detail = Describe(capture?.Body);
                _logger.LogWarning("Maxio answered {Status} for {Method} {Uri}: {Detail}", code, api.Method, api.RequestUri, detail);
                if (code is 401 or 403)
                    return new BillingException(502, "The billing provider is unavailable.", ex);
                if (code == 429)
                    return new BillingException(503, "The billing provider is busy. Try again shortly.", ex);
                if (code is >= 400 and < 500 && api is not ResponseDeserializationException)
                    return new BillingException(code, string.IsNullOrEmpty(detail) ? "Maxio refused the request." : detail, ex);
                return new BillingException(502, "The billing provider returned an unreadable answer.", ex);
            case SdkTimeoutException:
                return new BillingException(504, "The billing provider did not answer in time.", ex);
            default:
                _logger.LogError(ex, "Billing provider failure");
                return new BillingException(502, "The billing provider is unavailable.", ex);
        }
    }

    private static string Describe(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var messages = new List<string>();
            void Collect(JsonElement e)
            {
                switch (e.ValueKind)
                {
                    case JsonValueKind.String: messages.Add(e.GetString()!); break;
                    case JsonValueKind.Array: foreach (var i in e.EnumerateArray()) Collect(i); break;
                    case JsonValueKind.Object:
                        foreach (var p in e.EnumerateObject())
                            if (p.Name is "errors" or "error" or "message") Collect(p.Value);
                        break;
                }
            }
            Collect(doc.RootElement);
            if (messages.Count > 0) return Truncate(string.Join(" ", messages));
        }
        catch (JsonException) { }
        return Truncate(body);
    }

    private static string Truncate(string s) => s.Length <= 300 ? s : s[..300];
}
