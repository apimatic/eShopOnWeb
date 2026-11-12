using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.ProductPricePoints;
using MaxioAdvancedBilling.Requests.Products;
using MaxioAdvancedBilling.Requests.SubscriptionComponents;
using MaxioAdvancedBilling.Requests.SubscriptionInvoiceAccount;
using MaxioAdvancedBilling.Requests.SubscriptionProducts;
using MaxioAdvancedBilling.Requests.SubscriptionStatus;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// All calls to Maxio. Every failure leaves this class as a <see cref="BillingException"/> carrying a 4xx/5xx and a reason.
/// </summary>
public class MaxioBillingService
{
    private const int PER_PAGE = 200;
    private const int MAX_PAGES = 50;
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly IReadRepository<CatalogBrand> _brands;
    private readonly CatalogContext _db;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(MaxioAdvancedBillingClient client, IReadRepository<CatalogBrand> brands,
        CatalogContext db, ILogger<MaxioBillingService> logger)
    {
        _client = client;
        _brands = brands;
        _db = db;
        _logger = logger;
    }

    // ------------------------------------------------------------------ plans

    public async Task<(List<PlanDto> Plans, bool Truncated)> ListPlansAsync(CancellationToken ct)
    {
        using var cts = Bounded(ct);
        var brandNames = (await _brands.ListAsync(cts.Token)).Select(b => b.Brand).ToList();
        return await OfferedPlansAsync(brandNames, cts.Token);
    }

    /// <summary>Non-archived products whose product family is named like a catalog brand.</summary>
    private async Task<(List<PlanDto> Plans, bool Truncated)> OfferedPlansAsync(IReadOnlyCollection<string> brandNames, CancellationToken ct)
    {
        var plans = new List<PlanDto>();
        var truncated = false;
        for (var page = 1; ; page++)
        {
            if (page > MAX_PAGES) { truncated = true; break; }
            var items = await Run(() => _client.Products.ListProducts(
                new ListProductsRequest { Page = page, PerPage = PER_PAGE }, cancellationToken: ct));
            foreach (var item in items)
            {
                var p = item.Product;
                var family = p?.ProductFamily?.Name;
                if (p == null || p.ArchivedAt != null || p.Id == null || p.Handle == null || family == null) continue;
                var brand = brandNames.FirstOrDefault(b => string.Equals(b, family, StringComparison.OrdinalIgnoreCase));
                if (brand == null) continue;
                plans.Add(ToPlan(p, brand));
            }
            if (items.Count < PER_PAGE) break;
        }
        return (plans, truncated);
    }

    private static PlanDto ToPlan(Product p, string brand) => new()
    {
        Id = p.Id!.Value,
        Handle = p.Handle ?? "",
        Name = p.Name ?? "",
        Brand = brand,
        Price = (p.PriceInCents ?? 0) / 100m,
        Interval = p.Interval,
        IntervalUnit = p.IntervalUnit?.Value,
    };

    private async Task<PlanDto> OfferedPlanByHandleAsync(string? handle, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(handle)) throw new BillingException(400, "planHandle is required.");
        var brandNames = (await _brands.ListAsync(ct)).Select(b => b.Brand).ToList();
        var (plans, truncated) = await OfferedPlansAsync(brandNames, ct);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, handle.Trim(), StringComparison.OrdinalIgnoreCase));
        if (plan != null) return plan;
        if (truncated) throw new BillingException(503, "The plan list is too long to verify that the plan is offered; try again later.");
        throw new BillingException(404, $"Plan '{handle}' is not a plan offered by this shop.");
    }

    // ---------------------------------------------------------- subscriptions

    public async Task<SubscriptionDto> SubscribeAsync(string user, string? planHandle, CancellationToken ct)
    {
        using var cts = Bounded(ct);
        var token = cts.Token;
        var plan = await OfferedPlanByHandleAsync(planHandle, token);

        var site = await Run(() => _client.Sites.ReadSite(cancellationToken: token));
        var method = site.Site?.RelationshipInvoicingEnabled == true ? CollectionMethod.Remittance : CollectionMethod.Invoice;

        // claim -> SDK call -> release
        var claim = await ClaimAsync($"subscribe:{user.ToLowerInvariant()}:{plan.Handle.ToLowerInvariant()}",
            "A subscription to this plan is already being created for you.", token);
        try
        {
            var started = DateTimeOffset.UtcNow.AddMinutes(-1);
            var customerKnown = await CustomerExistsAsync(user, token);
            var create = new CreateSubscription
            {
                ProductHandle = plan.Handle,
                PaymentCollectionMethod = method,
            };
            create = customerKnown
                ? create with { CustomerReference = user }
                : create with { CustomerAttributes = CustomerFor(user) };

            SubscriptionResponse created;
            try
            {
                created = await Run<SubscriptionResponse, CreateSubscriptionError>(
                    () => _client.Subscriptions.CreateSubscription(
                        new CreateSubscriptionOperationRequest { Body = new CreateSubscriptionRequest { Subscription = create } },
                        cancellationToken: token));
            }
            catch (BillingException ex) when (ex.OutcomeUnknown)
            {
                var found = await FindSubscriptionAsync(user, plan.Handle, started, token);
                if (found == null) throw new BillingException(502, "The billing system did not confirm the subscription; its outcome is unknown, check your subscriptions before retrying.", ex) { OutcomeUnknown = true };
                return await GetAsync(user, found.Value, token);
            }
            var id = created.Subscription?.Id ?? throw new BillingException(502, "The billing system answered without a subscription.");
            return await GetAsync(user, id, token);
        }
        finally
        {
            await ReleaseAsync(claim);
        }
    }

    private async Task<int?> FindSubscriptionAsync(string user, string handle, DateTimeOffset since, CancellationToken ct)
    {
        try
        {
            var customer = await Run(() => _client.Customers.ReadCustomerByReference(new ReadCustomerByReferenceRequest { Reference = user }, cancellationToken: ct));
            if (customer.Customer?.Id is not int customerId) return null;
            var subs = await Run(() => _client.Customers.ListCustomerSubscriptions(new ListCustomerSubscriptionsRequest { CustomerId = customerId }, cancellationToken: ct));
            return subs.Select(s => s.Subscription)
                .Where(s => s?.Id != null && string.Equals(s.Product?.Handle, handle, StringComparison.OrdinalIgnoreCase) && (s.CreatedAt ?? DateTimeOffset.MinValue) >= since)
                .OrderByDescending(s => s!.CreatedAt).Select(s => s!.Id).FirstOrDefault();
        }
        catch (BillingException)
        {
            return null;
        }
    }

    private async Task<bool> CustomerExistsAsync(string user, CancellationToken ct)
    {
        try
        {
            var c = await Run(() => _client.Customers.ReadCustomerByReference(new ReadCustomerByReferenceRequest { Reference = user }, cancellationToken: ct));
            return c.Customer != null;
        }
        catch (BillingException ex) when (ex.StatusCode == 404)
        {
            return false;
        }
    }

    private static CustomerAttributes CustomerFor(string user)
    {
        var local = user.Contains('@') ? user[..user.IndexOf('@')] : user;
        var parts = local.Split(new[] { '.', '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var first = parts.Length > 0 ? parts[0] : local;
        var last = parts.Length > 1 ? string.Join(' ', parts.Skip(1)) : "Shopper";
        return new CustomerAttributes
        {
            Reference = user,
            FirstName = first,
            LastName = last,
            Email = user.Contains('@') ? user : $"{local}@shoppers.invalid",
        };
    }

    public async Task<SubscriptionDto> GetAsync(string user, int subscriptionId, CancellationToken ct)
    {
        using var cts = Bounded(ct);
        var sub = await OwnedAsync(user, subscriptionId, cts.Token);
        return await ViewAsync(sub, cts.Token);
    }

    private async Task<Subscription> OwnedAsync(string user, int subscriptionId, CancellationToken ct)
    {
        Subscription? sub;
        try
        {
            var r = await Run(() => _client.Subscriptions.ReadSubscription(new ReadSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: ct));
            sub = r.Subscription;
        }
        catch (BillingException ex) when (ex.StatusCode == 404)
        {
            throw NotYours();
        }
        if (sub == null || !string.Equals(sub.Customer?.Reference, user, StringComparison.OrdinalIgnoreCase)) throw NotYours();
        return sub;
    }

    private static BillingException NotYours() => new(404, "No such subscription for the signed-in shopper.");

    private async Task<SubscriptionDto> ViewAsync(Subscription sub, CancellationToken ct)
    {
        long? openInvoices = null;
        try
        {
            var balances = await Run(() => _client.SubscriptionInvoiceAccount.ReadAccountBalances(
                new ReadAccountBalancesRequest { SubscriptionId = sub.Id!.Value }, cancellationToken: ct));
            openInvoices = balances.OpenInvoices?.BalanceInCents;
        }
        catch (BillingException ex) when (ex.StatusCode is >= 400 and < 500)
        {
            _logger.LogWarning("Account balances unavailable for subscription {Id}: {Reason}", sub.Id, ex.Message);
        }
        if (openInvoices == null && sub.BalanceInCents == null)
            throw new BillingException(409, "The balance of this subscription is not available, so it cannot be said whether it is paid.");
        // Maxio may report the same debt in both places; what is owed is the larger figure, never below zero.
        var owedCents = Math.Max(Math.Max(openInvoices ?? 0, 0), Math.Max(sub.BalanceInCents ?? 0, 0));
        return new SubscriptionDto
        {
            Id = sub.Id!.Value,
            State = sub.State?.Value,
            PlanHandle = sub.Product?.Handle,
            PlanName = sub.Product?.Name,
            CustomerReference = sub.Customer?.Reference,
            Paid = owedCents == 0,
            BalanceOwed = owedCents / 100m,
            CurrentPeriodEndsAt = sub.CurrentPeriodEndsAt,
            CanceledAt = sub.CanceledAt,
        };
    }

    public async Task<SubscriptionDto> PauseAsync(string user, int id, CancellationToken ct)
    {
        using var cts = Bounded(ct);
        var t = cts.Token;
        var sub = await OwnedAsync(user, id, t);
        if (State(sub) is "canceled" or "expired") throw new BillingException(409, $"A {State(sub)} subscription cannot be paused.");
        if (State(sub) is "on_hold" or "paused") throw new BillingException(409, "The subscription is already paused.");
        return await WriteAsync(user, id, t, () => Run<SubscriptionResponse, PauseSubscriptionError>(
            () => _client.SubscriptionStatus.PauseSubscription(new PauseSubscriptionRequest { SubscriptionId = id }, cancellationToken: t)),
            s => State(s) is "on_hold" or "paused");
    }

    public async Task<SubscriptionDto> ResumeAsync(string user, int id, CancellationToken ct)
    {
        using var cts = Bounded(ct);
        var t = cts.Token;
        var sub = await OwnedAsync(user, id, t);
        if (State(sub) is not ("on_hold" or "paused")) throw new BillingException(409, "Only a paused subscription can be resumed.");
        return await WriteAsync(user, id, t, () => Run<SubscriptionResponse, ResumeSubscriptionError>(
            () => _client.SubscriptionStatus.ResumeSubscription(new ResumeSubscriptionRequest { SubscriptionId = id }, cancellationToken: t)),
            s => State(s) is not ("on_hold" or "paused"));
    }

    public async Task<SubscriptionDto> CancelAsync(string user, int id, CancellationToken ct)
    {
        using var cts = Bounded(ct);
        var t = cts.Token;
        var sub = await OwnedAsync(user, id, t);
        if (State(sub) is "canceled") throw new BillingException(409, "The subscription is already canceled.");
        return await WriteAsync(user, id, t, () => Run<SubscriptionResponse, CancelSubscriptionError>(
            () => _client.SubscriptionStatus.CancelSubscription(new CancelSubscriptionRequest { SubscriptionId = id }, cancellationToken: t)),
            s => State(s) is "canceled");
    }

    public async Task<SubscriptionDto> RestartAsync(string user, int id, CancellationToken ct)
    {
        using var cts = Bounded(ct);
        var t = cts.Token;
        var sub = await OwnedAsync(user, id, t);
        if (State(sub) is not "canceled") throw new BillingException(409, "Only a canceled subscription can be restarted.");
        return await WriteAsync(user, id, t, () => Run<SubscriptionResponse, ReactivateSubscriptionError>(
            () => _client.SubscriptionStatus.ReactivateSubscription(
                new ReactivateSubscriptionOperationRequest
                {
                    SubscriptionId = id,
                    // keep an owed balance owed, so that restarting does not make an unpaid subscription look paid
                    Body = new ReactivateSubscriptionRequest { PreserveBalance = true },
                }, cancellationToken: t)),
            s => State(s) is not "canceled");
    }

    public async Task<SubscriptionDto> SwitchAsync(string user, int id, string? planHandle, CancellationToken ct)
    {
        using var cts = Bounded(ct);
        var t = cts.Token;
        var sub = await OwnedAsync(user, id, t);
        var target = await OfferedPlanByHandleAsync(planHandle, t);
        if (string.Equals(sub.Product?.Handle, target.Handle, StringComparison.OrdinalIgnoreCase))
            throw new BillingException(409, "The subscription is already on that plan.");

        var migratable = State(sub) is "active" or "trialing";
        Task<SubscriptionResponse> Send(bool preservePeriod) => migratable
            ? Run<SubscriptionResponse, MigrateSubscriptionProductError>(() => _client.SubscriptionProducts.MigrateSubscriptionProduct(
                new MigrateSubscriptionProductRequest
                {
                    SubscriptionId = id,
                    Body = new SubscriptionProductMigrationRequest
                    {
                        Migration = new SubscriptionProductMigration { ProductHandle = target.Handle, PreservePeriod = preservePeriod },
                    },
                }, cancellationToken: t))
            : Run<SubscriptionResponse, UpdateSubscriptionError>(() => _client.Subscriptions.UpdateSubscription(
                new UpdateSubscriptionOperationRequest
                {
                    SubscriptionId = id,
                    Body = new UpdateSubscriptionRequest { Subscription = new UpdateSubscription { ProductHandle = target.Handle } },
                }, cancellationToken: t));

        return await WriteAsync(user, id, t, async () =>
            {
                SubscriptionResponse switched;
                try { switched = await Send(true); }
                catch (BillingException ex) when (migratable && ex.StatusCode == 422 && ex.Message.Contains("prorat", StringComparison.OrdinalIgnoreCase))
                {
                    switched = await Send(false);
                }
                if (State(sub) != "canceled") return switched;
                // a canceled subscriber who switches is put on the new plan, which means it runs again
                return await Run<SubscriptionResponse, ReactivateSubscriptionError>(() => _client.SubscriptionStatus.ReactivateSubscription(
                    new ReactivateSubscriptionOperationRequest { SubscriptionId = id, Body = new ReactivateSubscriptionRequest { PreserveBalance = true } },
                    cancellationToken: t));
            },
            s => string.Equals(s.Product?.Handle, target.Handle, StringComparison.OrdinalIgnoreCase) && State(s) != "canceled",
            mustChange: true, failure: "The billing system did not move the subscription to the new plan.");
    }

    private static string State(Subscription s) => s.State?.Value ?? "";

    /// <summary>Runs a write, settles an unknown outcome by re-reading, verifies it took effect, and returns the fresh view.</summary>
    private async Task<SubscriptionDto> WriteAsync(string user, int id, CancellationToken ct, Func<Task<SubscriptionResponse>> write,
        Func<Subscription, bool> confirmed, bool mustChange = false, string failure = "The billing system did not apply the change.")
    {
        try
        {
            await write();
        }
        catch (BillingException ex) when (ex.OutcomeUnknown)
        {
            var reread = await OwnedAsync(user, id, ct);
            if (!confirmed(reread)) throw new BillingException(502, "The billing system did not confirm the change; its outcome is unknown, check the subscription before retrying.", ex) { OutcomeUnknown = true };
        }
        var fresh = await OwnedAsync(user, id, ct);
        if (mustChange && !confirmed(fresh)) throw new BillingException(409, failure);
        return await ViewAsync(fresh, ct);
    }

    // ------------------------------------------------------------------ usage

    public async Task<UsageResponse> UsageAsync(string user, int id, string? dateText, CancellationToken ct)
    {
        if (!DateTime.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new BillingException(400, "date is required and must be a valid calendar date in the form YYYY-MM-DD.");
        using var cts = Bounded(ct);
        var t = cts.Token;
        await OwnedAsync(user, id, t);

        var response = new UsageResponse { SubscriptionId = id, Date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
        var components = await Run(() => _client.SubscriptionComponents.ListSubscriptionComponents(
            new ListSubscriptionComponentsRequest { SubscriptionId = id }, cancellationToken: t));
        var since = new DateTimeOffset(date, TimeSpan.Zero);
        var until = new DateTimeOffset(date.AddDays(1), TimeSpan.Zero);
        foreach (var wrapper in components)
        {
            var c = wrapper.Component;
            if (c?.ComponentId == null || c.ArchivedAt != null || c.Kind?.Value != ComponentKind.MeteredComponent.Value) continue;
            var componentId = c.ComponentId.Value;
            var dto = new ComponentUsageDto { ComponentId = componentId, ComponentName = c.Name };
            for (var page = 1; ; page++)
            {
                if (page > MAX_PAGES) { response.Truncated = true; break; }
                var currentPage = page;
                var usages = await Run(() => _client.SubscriptionComponents.ListUsages(new ListUsagesRequest
                {
                    SubscriptionIdOrReference = id,
                    ComponentId = componentId,
                    SinceDate = since,
                    UntilDate = until,
                    Page = currentPage,
                    PerPage = PER_PAGE,
                }, cancellationToken: t));
                foreach (var u in usages.Select(x => x.Usage))
                {
                    if (u == null || u.CreatedAt == null || u.CreatedAt.Value.Date != date.Date) continue;
                    string? q = null;
                    if (u.Quantity != null)
                    {
                        if (u.Quantity.TryGetInt(out var qi)) q = qi.ToString(CultureInfo.InvariantCulture);
                        else if (u.Quantity.TryGetString(out var qs)) q = qs;
                    }
                    dto.Usages.Add(new UsageEntryDto { Id = u.Id, Quantity = q, Memo = u.Memo, RecordedAt = u.CreatedAt });
                    if (decimal.TryParse(q, NumberStyles.Any, CultureInfo.InvariantCulture, out var n)) dto.TotalQuantity += n;
                }
                if (usages.Count < PER_PAGE) break;
            }
            response.Components.Add(dto);
        }
        return response;
    }

    // ------------------------------------------------------------------ staff

    public async Task<PlanDto> CreatePlanAsync(string? name, decimal price, string? brand, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new BillingException(400, "name is required.");
        if (string.IsNullOrWhiteSpace(brand)) throw new BillingException(400, "brand is required.");
        if (price < 0 || decimal.Round(price, 2) != price) throw new BillingException(400, "price must be a non-negative amount with at most two decimals.");
        using var cts = Bounded(ct);
        var t = cts.Token;

        var catalogBrand = (await _brands.ListAsync(t)).FirstOrDefault(b => string.Equals(b.Brand, brand.Trim(), StringComparison.OrdinalIgnoreCase));
        if (catalogBrand == null) throw new BillingException(422, $"Brand '{brand}' is not a brand in the shop's catalog.");

        var handle = Slug(catalogBrand.Brand) + "-" + Slug(name);
        var claim = await ClaimAsync($"plan:{handle}", "A plan with this brand and name is already being created.", t);
        try
        {
            var families = await Run(() => _client.ProductFamilies.ListProductFamilies(new ListProductFamiliesRequest(), cancellationToken: t));
            var familyId = families.Select(f => f.ProductFamily)
                .FirstOrDefault(f => f?.Id != null && f.ArchivedAt == null && string.Equals(f.Name, catalogBrand.Brand, StringComparison.OrdinalIgnoreCase))?.Id;
            if (familyId == null)
            {
                try
                {
                    var created = await Run<ProductFamilyResponse, CreateProductFamilyError>(() => _client.ProductFamilies.CreateProductFamily(
                        new CreateProductFamilyOperationRequest
                        {
                            Body = new CreateProductFamilyRequest { ProductFamily = new CreateProductFamily { Name = catalogBrand.Brand } },
                        }, cancellationToken: t));
                    familyId = created.ProductFamily?.Id;
                }
                catch (BillingException ex) when (ex.OutcomeUnknown)
                {
                    var again = await Run(() => _client.ProductFamilies.ListProductFamilies(new ListProductFamiliesRequest(), cancellationToken: t));
                    familyId = again.Select(f => f.ProductFamily).FirstOrDefault(f => f?.Id != null && string.Equals(f.Name, catalogBrand.Brand, StringComparison.OrdinalIgnoreCase))?.Id;
                    if (familyId == null) throw new BillingException(502, "The billing system did not confirm the brand; its outcome is unknown.", ex) { OutcomeUnknown = true };
                }
            }
            if (familyId == null) throw new BillingException(502, "The billing system did not return the brand's product family.");

            ProductResponse product;
            try
            {
                product = await Run<ProductResponse, CreateProductError>(() => _client.Products.CreateProduct(new CreateProductRequest
                {
                    ProductFamilyId = familyId.Value.ToString(CultureInfo.InvariantCulture),
                    Body = new CreateOrUpdateProductRequest
                    {
                        Product = new CreateOrUpdateProduct
                        {
                            Name = name.Trim(),
                            Description = name.Trim(),
                            Handle = handle,
                            PriceInCents = (long)decimal.Round(price * 100m),
                            Interval = 1,
                            IntervalUnit = IntervalUnit.Month,
                        },
                    },
                }, cancellationToken: t));
            }
            catch (BillingException ex) when (ex.OutcomeUnknown)
            {
                product = await Run(() => _client.Products.ReadProductByHandle(new ReadProductByHandleRequest { ApiHandle = handle }, cancellationToken: t));
            }
            var p = product.Product ?? throw new BillingException(502, "The billing system answered without a plan.");
            return ToPlan(p, catalogBrand.Brand);
        }
        finally
        {
            await ReleaseAsync(claim);
        }
    }

    public async Task<CurrencyPriceDto> SetCurrencyPriceAsync(int planId, string? currency, decimal price, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(currency)) throw new BillingException(400, "currency is required.");
        if (price <= 0) throw new BillingException(400, "price must be greater than zero.");
        currency = currency.Trim().ToUpperInvariant();
        using var cts = Bounded(ct);
        var t = cts.Token;

        var brandNames = (await _brands.ListAsync(t)).Select(b => b.Brand).ToList();
        var (plans, truncated) = await OfferedPlansAsync(brandNames, t);
        var plan = plans.FirstOrDefault(p => p.Id == planId);
        if (plan == null)
            throw truncated ? new BillingException(503, "The plan list is too long to verify the plan; try again later.")
                            : new BillingException(404, $"Plan {planId} is not a plan offered by this shop.");

        var site = await Run(() => _client.Sites.ReadSite(cancellationToken: t));
        var allowed = site.Site?.NonPrimaryCurrencies ?? new List<string>();
        if (!allowed.Any(c => string.Equals(c, currency, StringComparison.OrdinalIgnoreCase)))
            throw new BillingException(422, $"Currency '{currency}' is not enabled for this billing site" +
                (allowed.Count > 0 ? $" (available: {string.Join(", ", allowed)})." : "."));

        var product = (await Run(() => _client.Products.ReadProductByHandle(new ReadProductByHandleRequest { ApiHandle = plan.Handle }, cancellationToken: t))).Product;
        var pricePointId = product?.DefaultProductPricePointId ?? throw new BillingException(409, "The plan has no default price point to attach a currency price to.");

        async Task<ProductPricePoint> ReadPoint() => (await Run(() => _client.ProductPricePoints.ReadProductPricePoint(new ReadProductPricePointRequest
        {
            ProductId = planId,
            PricePointId = pricePointId,
            CurrencyPrices = true,
        }, cancellationToken: t))).PricePoint ?? throw new BillingException(502, "The billing system answered without a price point.");

        var point = await ReadPoint();
        if ((point.TrialPriceInCents ?? 0) != 0 || (point.InitialChargeInCents ?? 0) != 0)
            throw new BillingException(409, "This plan has a trial or initial charge, so its currency prices cannot be set here.");

        var existing = point.CurrencyPrices?.FirstOrDefault(c => string.Equals(c.Currency, currency, StringComparison.OrdinalIgnoreCase) && c.Role?.Value == CurrencyPriceRole.Baseline.Value);
        if (existing == null && price != decimal.Truncate(price))
            throw new BillingException(422, "A new currency price must be a whole amount.");

        string? claim = null;
        if (existing == null) claim = await ClaimAsync($"currency:{planId}:{currency}", "A price in this currency is already being created for the plan.", t);
        try
        {
            try
            {
                if (existing?.Id is int rowId)
                    await Run<CurrencyPricesResponse, UpdateProductCurrencyPricesError>(() => _client.ProductPricePoints.UpdateProductCurrencyPrices(
                        new UpdateProductCurrencyPricesRequest
                        {
                            ProductPricePointId = pricePointId,
                            Body = new UpdateCurrencyPricesRequest { CurrencyPrices = new[] { new UpdateCurrencyPrice { Id = rowId, Price = (double)price } } },
                        }, cancellationToken: t));
                else
                    await Run<CurrencyPricesResponse, CreateProductCurrencyPricesError>(() => _client.ProductPricePoints.CreateProductCurrencyPrices(
                        new CreateProductCurrencyPricesOperationRequest
                        {
                            ProductPricePointId = pricePointId,
                            Body = new CreateProductCurrencyPricesRequest
                            {
                                CurrencyPrices = new[] { new CreateProductCurrencyPrice { Currency = currency, Price = (int)price, Role = CurrencyPriceRole.Baseline } },
                            },
                        }, cancellationToken: t));
            }
            catch (BillingException ex) when (ex.OutcomeUnknown)
            {
                // settled below by reading the price point back
                _logger.LogWarning("Currency price write for plan {Plan} {Currency} had an unknown outcome; reading back.", planId, currency);
            }

            var after = await ReadPoint();
            var row = after.CurrencyPrices?.FirstOrDefault(c => string.Equals(c.Currency, currency, StringComparison.OrdinalIgnoreCase) && c.Role?.Value == CurrencyPriceRole.Baseline.Value);
            if (row?.Price == null || (decimal)row.Price.Value != price)
                throw new BillingException(409, "The billing system did not store the requested currency price" +
                    (row?.Price != null ? $" (it holds {row.Price}); the price point may follow the site's exchange rate." : "."));
            return new CurrencyPriceDto { PlanId = planId, Currency = currency, Price = (decimal)row.Price.Value };
        }
        finally
        {
            await ReleaseAsync(claim);
        }
    }

    public async Task<SubscriptionDto> SetNextBillingDateAsync(int id, string? dateText, CancellationToken ct)
    {
        if (!DateTime.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new BillingException(400, "date is required and must be a valid calendar date in the form YYYY-MM-DD.");
        if (date.Date <= DateTime.UtcNow.Date) throw new BillingException(422, "The next billing date must be in the future.");
        using var cts = Bounded(ct);
        var t = cts.Token;

        Subscription sub;
        try { sub = (await Run(() => _client.Subscriptions.ReadSubscription(new ReadSubscriptionRequest { SubscriptionId = id }, cancellationToken: t))).Subscription ?? throw new BillingException(404, "No such subscription."); }
        catch (BillingException ex) when (ex.StatusCode == 404) { throw new BillingException(404, "No such subscription."); }

        if (State(sub) is not ("active" or "trialing" or "past_due" or "soft_failure"))
            throw new BillingException(409, $"A subscription in state '{State(sub)}' is not billed on a schedule, so it has no next billing date to set.");
        if (!string.IsNullOrEmpty(sub.SnapDay))
            throw new BillingException(409, "This subscription uses calendar billing; its billing date follows its snap day.");

        var at = new DateTimeOffset(date.Year, date.Month, date.Day, 12, 0, 0, TimeSpan.Zero);
        try
        {
            await Run<SubscriptionResponse, UpdateSubscriptionError>(() => _client.Subscriptions.UpdateSubscription(
                new UpdateSubscriptionOperationRequest
                {
                    SubscriptionId = id,
                    Body = new UpdateSubscriptionRequest { Subscription = new UpdateSubscription { NextBillingAt = at } },
                }, cancellationToken: t));
        }
        catch (BillingException ex) when (ex.OutcomeUnknown)
        {
            _logger.LogWarning("Next billing date write for {Id} had an unknown outcome; reading back.", id);
        }

        var fresh = (await Run(() => _client.Subscriptions.ReadSubscription(new ReadSubscriptionRequest { SubscriptionId = id }, cancellationToken: t))).Subscription
            ?? throw new BillingException(502, "The billing system answered without the subscription.");
        if (fresh.CurrentPeriodEndsAt == null || Math.Abs((fresh.CurrentPeriodEndsAt.Value.UtcDateTime.Date - date.Date).TotalDays) > 1)
            throw new BillingException(409, "The billing system did not move the next billing date to the requested day.");
        return await ViewAsync(fresh, t);
    }

    // ----------------------------------------------------------------- claims

    private async Task<string> ClaimAsync(string key, string conflictMessage, CancellationToken ct)
    {
        try
        {
            _db.BillingClaims.Add(new BillingClaim(key));
            await _db.SaveChangesAsync(ct);
            return key;
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
        {
            foreach (var e in _db.ChangeTracker.Entries<BillingClaim>().Where(e => e.State == EntityState.Added).ToList())
                e.State = EntityState.Detached;
            throw new BillingException(409, conflictMessage, ex);
        }
    }

    private async Task ReleaseAsync(string? key)
    {
        if (key == null) return;
        try
        {
            var claim = await _db.BillingClaims.FindAsync(key);
            if (claim != null)
            {
                _db.BillingClaims.Remove(claim);
                await _db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not release billing claim {Key}", key);
        }
    }

    // --------------------------------------------------------- error boundary

    private static CancellationTokenSource Bounded(CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Budget);
        return cts;
    }

    private static string Slug(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in s.Trim().ToLowerInvariant())
            sb.Append(char.IsLetterOrDigit(ch) && ch < 128 ? ch : '-');
        return string.Join('-', sb.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string Clip(string? s) => string.IsNullOrWhiteSpace(s) ? "" : s.Length > 300 ? s[..300] : s;

    /// <summary>The provider's own reason out of a typed error body, or null.</summary>
    private static string? DetailOf(ApiError error)
    {
        static string? List(bool ok, ErrorListResponse1? v) => ok && v != null ? string.Join("; ", v.Errors) : null;
        static string? Map(bool ok, ErrorArrayMapResponse1? v) => ok && v?.Errors != null ? string.Join("; ", v.Errors.Select(kv => $"{kv.Key}: {kv.Value}")) : null;
        ErrorListResponse1? l;
        ErrorArrayMapResponse1? m;
        switch (error)
        {
            case CreateSubscriptionError e: return List(e.TryGetErrorListResponse1(out l), l);
            case PauseSubscriptionError e: return List(e.TryGetErrorListResponse1(out l), l);
            case ResumeSubscriptionError e: return List(e.TryGetErrorListResponse1(out l), l);
            case ReactivateSubscriptionError e: return List(e.TryGetErrorListResponse1(out l), l);
            case MigrateSubscriptionProductError e: return List(e.TryGetErrorListResponse1(out l), l);
            case UpdateSubscriptionError e: return List(e.TryGetErrorListResponse1(out l), l);
            case CreateProductFamilyError e: return List(e.TryGetErrorListResponse1(out l), l);
            case CreateProductError e: return List(e.TryGetErrorListResponse1(out l), l);
            case CreateProductCurrencyPricesError e: return Map(e.TryGetErrorArrayMapResponse1(out m), m);
            case UpdateProductCurrencyPricesError e: return Map(e.TryGetErrorArrayMapResponse1(out m), m);
            default: return null;
        }
    }

    /// <summary>Case B operations (no typed error body).</summary>
    private async Task<T> Run<T>(Func<Task<T>> call)
    {
        try { return await call(); }
        catch (ApiException<RawError> ex) { throw FromStatus(ex.StatusCode, Clip(ex.Error.ReadAsString())); }
        catch (SdkException ex) { throw Translate(ex); }
    }

    /// <summary>Case A operations: the typed error body gives the reason.</summary>
    private async Task<T> Run<T, TError>(Func<Task<T>> call) where TError : ApiError
    {
        try { return await call(); }
        catch (ApiException<TError> ex)
        {
            var text = DetailOf(ex.Error);
            if (string.IsNullOrWhiteSpace(text) && ex.Error.TryGetRawError(out var raw)) text = Clip(raw.ReadAsString());
            throw FromStatus(ex.StatusCode, text);
        }
        catch (SdkException ex) { throw Translate(ex); }
    }

    private BillingException Translate(SdkException ex)
    {
        _logger.LogWarning("Maxio call failed: {Type} {Message}", ex.GetType().Name, ex.Message);
        return ex switch
        {
            ApiException api when (int)api.StatusCode is >= 200 and < 300 =>
                new BillingException(502, "The billing system answered with a response that could not be processed.", ex) { OutcomeUnknown = true },
            ApiException api => FromStatus(api.StatusCode, null),
            SdkTimeoutException => new BillingException(504, "The billing system did not answer in time.", ex) { OutcomeUnknown = true },
            SdkConnectionException => new BillingException(502, "The billing system could not be reached.", ex) { OutcomeUnknown = true },
            _ => new BillingException(502, "The billing system is unavailable.", ex),
        };
    }

    private BillingException FromStatus(HttpStatusCode status, string? detail)
    {
        var code = (int)status;
        _logger.LogWarning("Maxio answered {Status}: {Detail}", code, detail);
        var reason = string.IsNullOrWhiteSpace(detail) ? "" : " " + detail;
        return code switch
        {
            401 or 403 => new BillingException(502, "The billing system is unavailable."),
            429 => new BillingException(503, "The billing system is busy; try again shortly."),
            404 => new BillingException(404, "The billing system has no such record." + reason),
            >= 400 and < 500 => new BillingException(code == 400 ? 422 : code, "The billing system refused the request." + reason),
            _ => new BillingException(502, "The billing system failed to process the request."),
        };
    }
}
