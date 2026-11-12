using System;
using System.Collections.Concurrent;
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
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.ProductPricePoints;
using MaxioAdvancedBilling.Requests.Products;
using MaxioAdvancedBilling.Requests.SubscriptionComponents;
using MaxioAdvancedBilling.Requests.SubscriptionInvoiceAccount;
using MaxioAdvancedBilling.Requests.SubscriptionProducts;
using MaxioAdvancedBilling.Requests.Subscriptions;
using MaxioAdvancedBilling.Requests.SubscriptionStatus;
using MaxioAdvancedBilling.Requests.Customers;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using SdkProduct = MaxioAdvancedBilling.Models.Product;

namespace Microsoft.eShopWeb.PublicApi.MaxioEndpoints;

/// <summary>A failure the endpoint answers with a 4xx/5xx status and a reason.</summary>
public class BillingException : Exception
{
    public int Status { get; }
    public BillingException(int status, string message, Exception? inner = null) : base(message, inner) { Status = status; }
}

/// <summary>Claims that stop a second caller from starting the same write while one is in flight (in this process).</summary>
public class MaxioClaims
{
    private readonly ConcurrentDictionary<string, DateTime> _claims = new();
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(2);

    public IDisposable Take(string key)
    {
        var now = DateTime.UtcNow;
        if (_claims.TryGetValue(key, out var at) && now - at > Ttl)
            _claims.TryRemove(new KeyValuePair<string, DateTime>(key, at));
        if (!_claims.TryAdd(key, now))
            throw new BillingException(409, "The same request is already in progress, try again shortly.");
        return new Release(_claims, key);
    }

    private sealed class Release(ConcurrentDictionary<string, DateTime> claims, string key) : IDisposable
    {
        public void Dispose() => claims.TryRemove(key, out _);
    }
}

public record PlanDto(int Id, string Handle, string Name, long PriceInCents, int? BrandId, string? Brand, int Interval, string IntervalUnit);

/// <summary>The one class that fronts the Maxio SDK for the PublicApi.</summary>
public class MaxioBillingService
{
    private const string FAMILY_PREFIX = "eshop-brand-";
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly MaxioClaims _claims;
    private readonly IRepository<CatalogBrand> _brands;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(MaxioAdvancedBillingClient client, MaxioSettings settings, MaxioClaims claims,
        IRepository<CatalogBrand> brands, ILogger<MaxioBillingService> logger)
    {
        _client = client;
        _settings = settings;
        _claims = claims;
        _brands = brands;
        _logger = logger;
    }

    // ---------------------------------------------------------------- plumbing

    /// <summary>Runs one request's Maxio work under one total budget and translates SDK failures.</summary>
    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken requestAborted)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
        cts.CancelAfter(TimeSpan.FromSeconds(_settings.TotalTimeoutSeconds));
        try
        {
            return await work(cts.Token);
        }
        catch (BillingException) { throw; }
        catch (OperationCanceledException) when (!requestAborted.IsCancellationRequested)
        {
            throw new BillingException(504, "Billing system did not answer in time.");
        }
        catch (ResponseDeserializationException ex)
        {
            _logger.LogError(ex, "Maxio returned an unreadable body (HTTP {Status})", (int)ex.StatusCode);
            var status = (int)ex.StatusCode;
            if (status is >= 400 and < 500 && status != 401 && status != 403 && status != 429)
                throw new BillingException(status, "Billing system rejected the request.", ex);
            throw new BillingException(502, "Billing system returned a response that could not be processed.", ex);
        }
        catch (ApiException ex)
        {
            throw Translate(ex);
        }
        catch (SdkTimeoutException ex)
        {
            _logger.LogWarning(ex, "Maxio call timed out");
            throw new BillingException(504, "Billing system did not answer in time.", ex);
        }
        catch (SdkConnectionException ex)
        {
            _logger.LogWarning(ex, "Maxio unreachable");
            throw new BillingException(502, "Billing system is unreachable.", ex);
        }
        catch (AuthSchemeException ex)
        {
            _logger.LogError(ex, "Maxio credentials missing or unusable");
            throw new BillingException(502, "Billing system is not available.", ex);
        }
    }

    private BillingException Translate(ApiException ex)
    {
        var status = (int)ex.StatusCode;
        var detail = Describe(ex);
        _logger.LogWarning("Maxio answered {Status}: {Detail}", status, detail);
        if (status is 401 or 403)
            return new BillingException(502, "Billing system is not available.", ex);
        if (status == 429)
            return new BillingException(429, "Billing system is busy, try again later.", ex);
        if (status == 404)
            return new BillingException(404, "Not found in the billing system.", ex);
        if (status is >= 400 and < 500)
            return new BillingException(status, string.IsNullOrEmpty(detail) ? "Billing system rejected the request." : detail, ex);
        return new BillingException(502, "Billing system failed to process the request.", ex);
    }

    private static string Describe(ApiException ex)
    {
        static string Join(IEnumerable<string> m) => string.Join("; ", m);
        try
        {
            switch (ex)
            {
                case ApiException<RawError> r: return TryRaw(r.Error);
                case ApiException<CreateSubscriptionError> e: return ListOf(e.Error.TryGetErrorListResponse1(out var l1) ? l1 : null, e.Error);
                case ApiException<UpdateSubscriptionError> e: return ListOf(e.Error.TryGetErrorListResponse1(out var l2) ? l2 : null, e.Error);
                case ApiException<PauseSubscriptionError> e: return ListOf(e.Error.TryGetErrorListResponse1(out var l3) ? l3 : null, e.Error);
                case ApiException<ResumeSubscriptionError> e: return ListOf(e.Error.TryGetErrorListResponse1(out var l4) ? l4 : null, e.Error);
                case ApiException<ReactivateSubscriptionError> e: return ListOf(e.Error.TryGetErrorListResponse1(out var l5) ? l5 : null, e.Error);
                case ApiException<MigrateSubscriptionProductError> e: return ListOf(e.Error.TryGetErrorListResponse1(out var l6) ? l6 : null, e.Error);
                case ApiException<CreateProductError> e: return ListOf(e.Error.TryGetErrorListResponse1(out var l7) ? l7 : null, e.Error);
                case ApiException<CreateProductFamilyError> e: return ListOf(e.Error.TryGetErrorListResponse1(out var l8) ? l8 : null, e.Error);
                case ApiException<CancelSubscriptionError> e: return TryRawOf(e.Error);
                case ApiException<CreateCustomerError> e: return TryRawOf(e.Error);
                case ApiException<CreateProductCurrencyPricesError> e: return TryRawOf(e.Error);
                case ApiException<UpdateProductCurrencyPricesError> e: return TryRawOf(e.Error);
            }
        }
        catch (Exception) { /* fall through to the generic text */ }
        return "";

        string ListOf(ErrorListResponse1? list, ApiError error) =>
            list is not null ? Join(list.Errors) : TryRawOf(error);
    }

    private static string TryRawOf(ApiError error) =>
        error.TryGetRawError(out var raw) ? TryRaw(raw) : "";

    private static string TryRaw(RawError raw)
    {
        var text = raw.ReadAsString();
        return text.Length > 400 ? text[..400] : text;
    }

    public static string Normalize(string? userName) => (userName ?? "").Trim().ToLowerInvariant();

    private static BillingException Bad(string msg) => new(400, msg);

    // ---------------------------------------------------------------- plan catalog

    private async Task<(List<PlanDto> Plans, bool Truncated)> LoadPlansAsync(CancellationToken ct)
    {
        var brandList = await _brands.ListAsync(ct);
        var brandsById = brandList.ToDictionary(b => b.Id, b => b.Brand);

        var families = await _client.ProductFamilies.ListProductFamilies(new ListProductFamiliesRequest(), cancellationToken: ct);
        var plans = new List<PlanDto>();
        var truncated = false;
        foreach (var fam in families.Select(f => f.ProductFamily).Where(f => f is not null && f.ArchivedAt is null))
        {
            var handle = fam!.Handle ?? "";
            if (!handle.StartsWith(FAMILY_PREFIX, StringComparison.Ordinal)
                || !int.TryParse(handle[FAMILY_PREFIX.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var brandId)
                || !brandsById.TryGetValue(brandId, out var brandName))
                continue;

            for (var page = 1; ; page++)
            {
                if (page > _settings.MaxPages) { truncated = true; break; }
                var products = await _client.ProductFamilies.ListProductsForProductFamily(new ListProductsForProductFamilyRequest
                {
                    ProductFamilyId = fam.Id!.Value.ToString(CultureInfo.InvariantCulture),
                    Page = page,
                    PerPage = 200
                }, cancellationToken: ct);
                foreach (var p in products.Select(x => x.Product).Where(x => x is not null && x.ArchivedAt is null && x.Handle is not null && x.Id is not null))
                    plans.Add(new PlanDto(p!.Id!.Value, p.Handle!, p.Name ?? p.Handle!, p.PriceInCents ?? 0, brandId, brandName,
                        p.Interval ?? 1, p.IntervalUnit?.Value ?? "month"));
                if (products.Count < 200) break;
            }
        }
        return (plans, truncated);
    }

    public Task<(List<PlanDto> Plans, bool Truncated)> ListPlansAsync(CancellationToken ct) =>
        RunAsync(LoadPlansAsync, ct);

    private async Task<PlanDto> RequireOfferedPlanAsync(string? handle, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(handle)) throw Bad("planHandle is required.");
        var (plans, truncated) = await LoadPlansAsync(ct);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, handle.Trim(), StringComparison.Ordinal));
        if (plan is null)
        {
            if (truncated) throw new BillingException(503, "The plan list is too large to verify the plan right now.");
            throw new BillingException(404, $"Plan '{handle}' is not offered.");
        }
        return plan;
    }

    // ---------------------------------------------------------------- admin: plans

    public Task<PlanDto> CreatePlanAsync(string? name, decimal? monthlyPrice, int? brandId, string? brandName, CancellationToken requestAborted) =>
        RunAsync(async ct =>
        {
            if (string.IsNullOrWhiteSpace(name)) throw Bad("name is required.");
            if (monthlyPrice is null || monthlyPrice <= 0 || decimal.Round(monthlyPrice.Value, 2) != monthlyPrice)
                throw Bad("monthlyPrice must be greater than zero with at most 2 decimals.");

            var brands = await _brands.ListAsync(ct);
            CatalogBrand? brand = brandId is not null
                ? brands.FirstOrDefault(b => b.Id == brandId)
                : brands.FirstOrDefault(b => string.Equals(b.Brand, brandName?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (brand is null) throw Bad("brand is not one of the brands in the catalog.");

            var familyHandle = FAMILY_PREFIX + brand.Id.ToString(CultureInfo.InvariantCulture);
            var planHandle = $"plan-{brand.Id}-{Slug(name)}";
            using var claim = _claims.Take($"plan:{brand.Id}:{planHandle}");

            var familyId = await EnsureFamilyAsync(brand, familyHandle, ct);
            var cents = (long)decimal.Round(monthlyPrice.Value * 100m, 0);
            var body = new CreateOrUpdateProduct
            {
                Name = name.Trim(),
                Description = name.Trim(),
                PriceInCents = cents,
                Interval = 1,
                IntervalUnit = IntervalUnit.Month,
                Handle = planHandle
            };
            SdkProduct? product;
            try
            {
                product = (await _client.Products.CreateProduct(new CreateProductRequest
                {
                    ProductFamilyId = familyId.ToString(CultureInfo.InvariantCulture),
                    Body = new CreateOrUpdateProductRequest { Product = body }
                }, cancellationToken: ct)).Product;
            }
            catch (SdkConnectionException)
            {
                // outcome unknown: the product may exist; settle by its handle
                product = await SettleProductAsync(planHandle, ct);
                if (product is null) throw new BillingException(502, "Billing system did not confirm the plan; check the plan list before retrying.");
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                throw new BillingException(409, $"The plan could not be created: {Describe(ex)}", ex);
            }
            if (product?.Id is null) throw new BillingException(502, "Billing system returned no plan.");
            return new PlanDto(product.Id.Value, product.Handle ?? planHandle, product.Name ?? name, product.PriceInCents ?? cents,
                brand.Id, brand.Brand, product.Interval ?? 1, product.IntervalUnit?.Value ?? "month");
        }, requestAborted);

    private async Task<SdkProduct?> SettleProductAsync(string handle, CancellationToken ct)
    {
        try { return (await _client.Products.ReadProductByHandle(new ReadProductByHandleRequest { ApiHandle = handle }, cancellationToken: ct)).Product; }
        catch (ApiException ex) when ((int)ex.StatusCode == 404) { return null; }
        catch (SdkConnectionException) { return null; }
    }

    private async Task<int> EnsureFamilyAsync(CatalogBrand brand, string familyHandle, CancellationToken ct)
    {
        async Task<int?> Read()
        {
            try
            {
                var all = await _client.ProductFamilies.ListProductFamilies(new ListProductFamiliesRequest(), cancellationToken: ct);
                return all.Select(f => f.ProductFamily).FirstOrDefault(f => f?.Handle == familyHandle)?.Id;
            }
            catch (ApiException ex) when ((int)ex.StatusCode == 404) { return null; }
        }

        var existing = await Read();
        if (existing is not null) return existing.Value;
        try
        {
            var created = await _client.ProductFamilies.CreateProductFamily(new CreateProductFamilyOperationRequest
            {
                Body = new CreateProductFamilyRequest { ProductFamily = new CreateProductFamily { Name = brand.Brand, Handle = familyHandle } }
            }, cancellationToken: ct);
            if (created.ProductFamily?.Id is { } id) return id;
        }
        catch (Exception ex) when (ex is SdkConnectionException || (ex is ApiException a && a.StatusCode == HttpStatusCode.UnprocessableEntity))
        {
            // lost a race or the outcome is unknown: settle by re-reading
        }
        existing = await Read();
        return existing ?? throw new BillingException(502, "Billing system did not confirm the brand's product family.");
    }

    private static string Slug(string name)
    {
        var sb = new StringBuilder();
        foreach (var ch in name.Trim().ToLowerInvariant())
            sb.Append(char.IsAsciiLetterOrDigit(ch) ? ch : '-');
        var s = string.Join("-", sb.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
        return s.Length == 0 ? "plan" : s;
    }

    // ---------------------------------------------------------------- admin: currency prices

    public record CurrencyPriceResult(int PlanId, string Currency, string? FormattedPrice, decimal Price, bool Created);

    public Task<CurrencyPriceResult> SetCurrencyPriceAsync(int planId, string? currency, decimal? price, CancellationToken requestAborted) =>
        RunAsync(async ct =>
        {
            if (string.IsNullOrWhiteSpace(currency)) throw Bad("currency is required.");
            if (price is null || price <= 0) throw Bad("price must be greater than zero.");
            var code = currency.Trim().ToUpperInvariant();

            var (plans, truncated) = await LoadPlansAsync(ct);
            var plan = plans.FirstOrDefault(p => p.Id == planId);
            if (plan is null)
                throw truncated ? new BillingException(503, "The plan list is too large to verify the plan right now.")
                                : new BillingException(404, $"Plan {planId} is not an offered plan.");

            var site = (await _client.Sites.ReadSite(cancellationToken: ct)).Site;
            var allowed = site.NonPrimaryCurrencies ?? new List<string>();
            if (!allowed.Contains(code, StringComparer.OrdinalIgnoreCase))
                throw Bad(allowed.Count == 0
                    ? $"currency '{code}' cannot be used: the billing site has no additional currencies enabled."
                    : $"currency '{code}' is not enabled on the billing site. Allowed: {string.Join(", ", allowed)}.");

            var product = (await _client.Products.ReadProduct(new ReadProductRequest { ProductId = planId }, cancellationToken: ct)).Product;
            var pricePointId = product?.DefaultProductPricePointId
                ?? throw new BillingException(409, "The plan has no default price point.");

            using var claim = _claims.Take($"ccy:{pricePointId}:{code}");

            async Task<IReadOnlyList<CurrencyPrice>> ReadPrices() =>
                (await _client.ProductPricePoints.ReadProductPricePoint(new ReadProductPricePointRequest
                {
                    ProductId = planId,
                    PricePointId = pricePointId,
                    CurrencyPrices = true
                }, cancellationToken: ct)).PricePoint.CurrencyPrices ?? new List<CurrencyPrice>();

            var current = await ReadPrices();
            var existing = current.FirstOrDefault(c => string.Equals(c.Currency, code, StringComparison.OrdinalIgnoreCase));
            var created = existing is null;
            // The price fields are expressed in the currency's major unit as the site shows it.
            try
            {
                if (existing is null)
                {
                    if (decimal.Round(price.Value, 0) != price.Value)
                        throw Bad("price must be a whole number of currency units for a new currency price.");
                    await _client.ProductPricePoints.CreateProductCurrencyPrices(new CreateProductCurrencyPricesOperationRequest
                    {
                        ProductPricePointId = pricePointId,
                        Body = new CreateProductCurrencyPricesRequest
                        {
                            CurrencyPrices = [new CreateProductCurrencyPrice { Currency = code, Price = (int)price.Value, Role = CurrencyPriceRole.Baseline }]
                        }
                    }, cancellationToken: ct);
                }
                else
                {
                    await _client.ProductPricePoints.UpdateProductCurrencyPrices(new UpdateProductCurrencyPricesRequest
                    {
                        ProductPricePointId = pricePointId,
                        Body = new UpdateCurrencyPricesRequest
                        {
                            CurrencyPrices = [new UpdateCurrencyPrice { Id = existing.Id ?? 0, Price = (double)price.Value }]
                        }
                    }, cancellationToken: ct);
                }
            }
            catch (SdkConnectionException) { /* outcome unknown: settled by the re-read below */ }

            var after = (await ReadPrices()).FirstOrDefault(c => string.Equals(c.Currency, code, StringComparison.OrdinalIgnoreCase));
            if (after is null) throw new BillingException(502, "Billing system did not confirm the currency price.");
            return new CurrencyPriceResult(planId, code, after.FormattedPrice, (decimal)(after.Price ?? 0), created);
        }, requestAborted);

    // ---------------------------------------------------------------- customers & ownership

    private async Task<int> EnsureCustomerAsync(string shopperRef, CancellationToken ct)
    {
        if (!shopperRef.Contains('@'))
            throw Bad("Your account has no email address, so it cannot be billed.");

        async Task<int?> Read()
        {
            try
            {
                return (await _client.Customers.ReadCustomerByReference(new ReadCustomerByReferenceRequest { Reference = shopperRef }, cancellationToken: ct))
                    .Customer?.Id;
            }
            catch (ApiException ex) when ((int)ex.StatusCode == 404) { return null; }
        }

        var id = await Read();
        if (id is not null) return id.Value;
        try
        {
            var local = shopperRef[..shopperRef.IndexOf('@')];
            var created = await _client.Customers.CreateCustomer(new CreateCustomerOperationRequest
            {
                Body = new CreateCustomerRequest
                {
                    Customer = new CreateCustomer { FirstName = local.Length == 0 ? "Shopper" : local, LastName = "Shopper", Email = shopperRef, Reference = shopperRef }
                }
            }, cancellationToken: ct);
            if (created.Customer?.Id is { } cid) return cid;
        }
        catch (Exception ex) when (ex is SdkConnectionException || ex is ApiException { StatusCode: HttpStatusCode.UnprocessableEntity })
        {
            // lost a race or the outcome is unknown: settle by re-reading
        }
        id = await Read();
        return id ?? throw new BillingException(502, "Billing system did not confirm the customer.");
    }

    private async Task<Subscription> LoadOwnedAsync(string shopperRef, int subscriptionId, CancellationToken ct)
    {
        Subscription? sub;
        try
        {
            sub = (await _client.Subscriptions.ReadSubscription(new ReadSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: ct)).Subscription;
        }
        catch (ApiException ex) when ((int)ex.StatusCode == 404)
        {
            throw new BillingException(404, "Subscription not found.");
        }
        if (sub is null || shopperRef.Length == 0 || Normalize(sub.Customer?.Reference) != shopperRef)
            throw new BillingException(404, "Subscription not found.");
        return sub;
    }

    // ---------------------------------------------------------------- shopper: subscribe / view

    public record SubscriptionDto(int Id, string? State, string? PlanHandle, string? PlanName, bool Paid, long BalanceInCents, long OpenInvoicesInCents,
        DateTimeOffset? CurrentPeriodEndsAt);

    private async Task<SubscriptionDto> ToDtoAsync(Subscription sub, CancellationToken ct)
    {
        var sid = sub.Id ?? throw new BillingException(502, "Billing system returned a subscription without an id.");
        var balances = await _client.SubscriptionInvoiceAccount.ReadAccountBalances(new ReadAccountBalancesRequest { SubscriptionId = sid }, cancellationToken: ct);
        var open = balances.OpenInvoices?.BalanceInCents;
        var bal = sub.BalanceInCents;
        if (open is null || bal is null)
            throw new BillingException(502, "Billing system did not report the balance for this subscription.");
        var state = sub.State;
        var behind = state == SubscriptionState.PastDue || state == SubscriptionState.Unpaid || state == SubscriptionState.SoftFailure;
        var paid = bal <= 0 && open <= 0 && !behind;
        return new SubscriptionDto(sid, state?.Value, sub.Product?.Handle, sub.Product?.Name, paid, bal.Value, open.Value, sub.CurrentPeriodEndsAt);
    }

    public Task<SubscriptionDto> GetSubscriptionAsync(string userName, int subscriptionId, CancellationToken requestAborted) =>
        RunAsync(async ct => await ToDtoAsync(await LoadOwnedAsync(Normalize(userName), subscriptionId, ct), ct), requestAborted);

    public Task<SubscriptionDto> SubscribeAsync(string userName, string? planHandle, CancellationToken requestAborted) =>
        RunAsync(async ct =>
        {
            var shopperRef = Normalize(userName);
            if (shopperRef.Length == 0) throw new BillingException(401, "Sign in to subscribe.");
            var plan = await RequireOfferedPlanAsync(planHandle, ct);
            using var claim = _claims.Take($"sub:{shopperRef}:{plan.Handle}");

            var customerId = await EnsureCustomerAsync(shopperRef, ct);
            await EnsureNoLiveSubscriptionAsync(customerId, plan.Handle, ct);

            var site = (await _client.Sites.ReadSite(cancellationToken: ct)).Site;
            var collection = site.RelationshipInvoicingEnabled == true ? CollectionMethod.Remittance : CollectionMethod.Invoice;
            var claimRef = $"eshop-{Guid.NewGuid():N}";
            Subscription? sub;
            try
            {
                sub = (await _client.Subscriptions.CreateSubscription(new CreateSubscriptionOperationRequest
                {
                    Body = new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = plan.Handle,
                            CustomerReference = shopperRef,
                            PaymentCollectionMethod = collection,
                            Reference = claimRef
                        }
                    }
                }, cancellationToken: ct)).Subscription;
            }
            catch (SdkConnectionException)
            {
                sub = await FindByReferenceAsync(claimRef, ct);
                if (sub is null) throw new BillingException(502, "Billing system did not confirm the subscription; check your subscriptions before retrying.");
            }
            if (sub is null) throw new BillingException(502, "Billing system returned no subscription.");
            return await ToDtoAsync(sub, ct);
        }, requestAborted);

    private async Task<Subscription?> FindByReferenceAsync(string reference, CancellationToken ct)
    {
        try { return (await _client.Subscriptions.FindSubscription(new FindSubscriptionRequest { Reference = reference }, cancellationToken: ct)).Subscription; }
        catch (ApiException) { return null; }
        catch (SdkConnectionException) { return null; }
    }

    private static readonly HashSet<string> DeadStates = new() { "canceled", "expired", "failed_to_create" };

    private async Task EnsureNoLiveSubscriptionAsync(int customerId, string planHandle, CancellationToken ct)
    {
        for (var page = 1; ; page++)
        {
            if (page > _settings.MaxPages) throw new BillingException(503, "Could not verify your existing subscriptions right now.");
            var list = await _client.Subscriptions.ListSubscriptions(new ListSubscriptionsRequest { CustomerId = customerId, Page = page, PerPage = 200 }, cancellationToken: ct);
            foreach (var s in list.Select(x => x.Subscription))
                if (s?.Product?.Handle == planHandle && s.State is { } st && !DeadStates.Contains(st.Value))
                    throw new BillingException(409, "You already have a live subscription to this plan.");
            if (list.Count < 200) return;
        }
    }

    // ---------------------------------------------------------------- shopper: pause / resume / cancel / restart

    private Task<SubscriptionDto> StatusChangeAsync(string userName, int subscriptionId, string key, Func<Subscription, string?> precondition,
        Func<CancellationToken, Task<Subscription?>> act, SubscriptionState expected, CancellationToken requestAborted) =>
        RunAsync(async ct =>
        {
            var shopperRef = Normalize(userName);
            using var claim = _claims.Take($"{key}:{subscriptionId}");
            var sub = await LoadOwnedAsync(shopperRef, subscriptionId, ct);
            var problem = precondition(sub);
            if (problem is not null) throw new BillingException(409, problem);
            Subscription? result;
            try { result = await act(ct); }
            catch (SdkConnectionException)
            {
                result = (await _client.Subscriptions.ReadSubscription(new ReadSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: ct)).Subscription;
                if (result?.State != expected) throw new BillingException(502, "Billing system did not confirm the change; check the subscription before retrying.");
            }
            return await ToDtoAsync(result ?? throw new BillingException(502, "Billing system returned no subscription."), ct);
        }, requestAborted);

    private static string? Need(Subscription s, string what, params SubscriptionState[] ok) =>
        ok.Any(o => s.State == o) ? null : $"Cannot {what} a subscription that is {s.State?.Value ?? "in an unknown state"}.";

    public Task<SubscriptionDto> PauseAsync(string user, int id, CancellationToken ct) =>
        StatusChangeAsync(user, id, "pause", s => Need(s, "pause", SubscriptionState.Active, SubscriptionState.Trialing),
            async c => (await _client.SubscriptionStatus.PauseSubscription(new PauseSubscriptionRequest { SubscriptionId = id }, cancellationToken: c)).Subscription,
            SubscriptionState.OnHold, ct);

    public Task<SubscriptionDto> ResumeAsync(string user, int id, CancellationToken ct) =>
        StatusChangeAsync(user, id, "resume", s => Need(s, "resume", SubscriptionState.OnHold),
            async c => (await _client.SubscriptionStatus.ResumeSubscription(new ResumeSubscriptionRequest { SubscriptionId = id }, cancellationToken: c)).Subscription,
            SubscriptionState.Active, ct);

    public Task<SubscriptionDto> CancelAsync(string user, int id, CancellationToken ct) =>
        StatusChangeAsync(user, id, "cancel",
            s => s.State == SubscriptionState.Canceled ? "The subscription is already cancelled." : null,
            async c => (await _client.SubscriptionStatus.CancelSubscription(new CancelSubscriptionRequest { SubscriptionId = id }, cancellationToken: c)).Subscription,
            SubscriptionState.Canceled, ct);

    public Task<SubscriptionDto> RestartAsync(string user, int id, CancellationToken ct) =>
        StatusChangeAsync(user, id, "restart", s => Need(s, "restart", SubscriptionState.Canceled),
            async c => (await _client.SubscriptionStatus.ReactivateSubscription(new ReactivateSubscriptionOperationRequest
            {
                SubscriptionId = id,
                Body = new ReactivateSubscriptionRequest { Resume = true }
            }, cancellationToken: c)).Subscription,
            SubscriptionState.Active, ct);

    // ---------------------------------------------------------------- shopper: switch plan

    public Task<SubscriptionDto> SwitchAsync(string userName, int subscriptionId, string? planHandle, CancellationToken requestAborted) =>
        RunAsync(async ct =>
        {
            var shopperRef = Normalize(userName);
            var plan = await RequireOfferedPlanAsync(planHandle, ct);
            using var claim = _claims.Take($"switch:{subscriptionId}");
            var sub = await LoadOwnedAsync(shopperRef, subscriptionId, ct);
            if (sub.Product?.Handle == plan.Handle && sub.State == SubscriptionState.Active)
                throw new BillingException(409, "The subscription is already on this plan.");

            async Task Migrate() =>
                await _client.SubscriptionProducts.MigrateSubscriptionProduct(new MigrateSubscriptionProductRequest
                {
                    SubscriptionId = subscriptionId,
                    Body = new SubscriptionProductMigrationRequest { Migration = new SubscriptionProductMigration { ProductHandle = plan.Handle } }
                }, cancellationToken: ct);

            async Task Reactivate(bool resume) =>
                await _client.SubscriptionStatus.ReactivateSubscription(new ReactivateSubscriptionOperationRequest
                {
                    SubscriptionId = subscriptionId,
                    Body = new ReactivateSubscriptionRequest { Resume = resume }
                }, cancellationToken: ct);

            var state = sub.State;
            try
            {
                if (state == SubscriptionState.Active || state == SubscriptionState.Trialing)
                {
                    if (sub.Product?.Handle == plan.Handle) throw new BillingException(409, "The subscription is already on this plan.");
                    await Migrate();
                }
                else if (state == SubscriptionState.OnHold)
                {
                    await _client.SubscriptionStatus.ResumeSubscription(new ResumeSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: ct);
                    if (sub.Product?.Handle != plan.Handle) await Migrate();
                }
                else if (state == SubscriptionState.Canceled || state == SubscriptionState.TrialEnded || state == SubscriptionState.Unpaid)
                {
                    try
                    {
                        await _client.Subscriptions.UpdateSubscription(new UpdateSubscriptionOperationRequest
                        {
                            SubscriptionId = subscriptionId,
                            Body = new UpdateSubscriptionRequest { Subscription = new UpdateSubscription { ProductHandle = plan.Handle } }
                        }, cancellationToken: ct);
                        await Reactivate(false);
                    }
                    catch (ApiException ex) when ((int)ex.StatusCode == 422)
                    {
                        // the product could not be changed while cancelled: reactivate first, then migrate
                        _logger.LogInformation("Product change on a cancelled subscription refused ({Detail}); reactivating first", Describe(ex));
                        var reread = await LoadOwnedAsync(shopperRef, subscriptionId, ct);
                        if (reread.State != SubscriptionState.Active) await Reactivate(false);
                        reread = await LoadOwnedAsync(shopperRef, subscriptionId, ct);
                        if (reread.Product?.Handle != plan.Handle) await Migrate();
                    }
                }
                else
                {
                    throw new BillingException(409, $"A subscription that is {state?.Value ?? "in an unknown state"} cannot be switched right now.");
                }
            }
            catch (BillingException) { throw; }
            catch (SdkConnectionException) { /* outcome unknown: settled by the re-read below */ }
            catch (ApiException ex)
            {
                var now = await LoadOwnedAsync(shopperRef, subscriptionId, ct);
                var t = Translate(ex);
                if (now.State != SubscriptionState.Active)
                    throw new BillingException(t.Status is >= 400 and < 500 ? t.Status : 409,
                        $"{t.Message} The subscription is now {now.State?.Value} on plan {now.Product?.Handle}.", ex);
                throw;
            }

            var final = await LoadOwnedAsync(shopperRef, subscriptionId, ct);
            if (final.State != SubscriptionState.Active || final.Product?.Handle != plan.Handle)
                throw new BillingException(409, $"The switch did not complete: the subscription is {final.State?.Value} on plan {final.Product?.Handle}.");
            return await ToDtoAsync(final, ct);
        }, requestAborted);

    // ---------------------------------------------------------------- shopper: usage for a day

    public record UsageItemDto(long? Id, int? ComponentId, string? ComponentHandle, string? Memo, DateTimeOffset? CreatedAt, string? Quantity);
    public record UsageTotalDto(int ComponentId, string? ComponentName, decimal Quantity);
    public record UsageDayDto(int SubscriptionId, string Date, List<UsageItemDto> Usages, List<UsageTotalDto> Totals, bool Truncated);

    public Task<UsageDayDto> GetUsageAsync(string userName, int subscriptionId, string? date, CancellationToken requestAborted) =>
        RunAsync(async ct =>
        {
            if (!DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                throw Bad("date is required in the format YYYY-MM-DD.");
            await LoadOwnedAsync(Normalize(userName), subscriptionId, ct);

            var comps = await _client.SubscriptionComponents.ListSubscriptionComponents(
                new ListSubscriptionComponentsRequest { SubscriptionId = subscriptionId }, cancellationToken: ct);
            var items = new List<UsageItemDto>();
            var totals = new List<UsageTotalDto>();
            var truncated = false;
            foreach (var c in comps.Select(x => x.Component).Where(x => x?.ComponentId is not null && x.Kind == ComponentKind.MeteredComponent))
            {
                decimal sum = 0;
                for (var page = 1; ; page++)
                {
                    if (page > _settings.MaxPages) { truncated = true; break; }
                    // widened window; the exact local calendar day is cut below from each usage's own offset
                    var list = await _client.SubscriptionComponents.ListUsages(new ListUsagesRequest
                    {
                        SubscriptionIdOrReference = subscriptionId,
                        ComponentId = c!.ComponentId!.Value,
                        SinceDate = new DateTimeOffset(DateTime.SpecifyKind(day.AddDays(-1), DateTimeKind.Utc)),
                        UntilDate = new DateTimeOffset(DateTime.SpecifyKind(day.AddDays(2), DateTimeKind.Utc)),
                        Page = page,
                        PerPage = 200
                    }, cancellationToken: ct);
                    foreach (var u in list.Select(x => x.Usage))
                    {
                        if (u.CreatedAt is not { } at || at.Date != day.Date) continue;
                        var q = QuantityOf(u.Quantity);
                        sum += q ?? 0;
                        items.Add(new UsageItemDto(u.Id, u.ComponentId ?? c.ComponentId, c.ComponentHandle, u.Memo, at, q?.ToString(CultureInfo.InvariantCulture)));
                    }
                    if (list.Count < 200) break;
                }
                totals.Add(new UsageTotalDto(c!.ComponentId!.Value, c.Name, sum));
            }
            return new UsageDayDto(subscriptionId, day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), items, totals, truncated);
        }, requestAborted);

    private static decimal? QuantityOf(MaxioAdvancedBilling.Models.AnyOf.Quantity1? q)
    {
        if (q is null) return null;
        if (q.TryGetInt(out var i)) return i;
        if (q.TryGetString(out var s) && decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)) return d;
        return null;
    }

    // ---------------------------------------------------------------- admin: next billing date

    public record NextBillingDto(int SubscriptionId, string State, string? NextBillingDate);

    public Task<NextBillingDto> SetNextBillingDateAsync(int subscriptionId, string? date, CancellationToken requestAborted) =>
        RunAsync(async ct =>
        {
            if (!DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                throw Bad("date is required in the format YYYY-MM-DD, and must be a real calendar date.");
            if (day.Date <= DateTime.UtcNow.Date)
                throw Bad("date must be in the future.");
            using var claim = _claims.Take($"nbd:{subscriptionId}");

            Subscription? sub;
            try { sub = (await _client.Subscriptions.ReadSubscription(new ReadSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: ct)).Subscription; }
            catch (ApiException ex) when ((int)ex.StatusCode == 404) { throw new BillingException(404, "Subscription not found."); }
            if (sub is null) throw new BillingException(404, "Subscription not found.");
            if (sub.State != SubscriptionState.Active && sub.State != SubscriptionState.Trialing)
                throw new BillingException(409, $"A subscription that is {sub.State?.Value ?? "in an unknown state"} is not billed on a schedule, so its next billing date cannot be set.");

            var offset = sub.CurrentPeriodEndsAt?.Offset ?? TimeSpan.Zero;
            var target = new DateTimeOffset(day.Year, day.Month, day.Day, 12, 0, 0, offset);
            try
            {
                await _client.Subscriptions.UpdateSubscription(new UpdateSubscriptionOperationRequest
                {
                    SubscriptionId = subscriptionId,
                    Body = new UpdateSubscriptionRequest { Subscription = new UpdateSubscription { NextBillingAt = target } }
                }, cancellationToken: ct);
            }
            catch (SdkConnectionException) { /* settled by the re-read below */ }

            var after = (await _client.Subscriptions.ReadSubscription(new ReadSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: ct)).Subscription;
            var actual = after?.CurrentPeriodEndsAt;
            if (actual is null || actual.Value.Date != day.Date)
                throw new BillingException(409, $"Billing system did not apply the date; the next billing date is {actual?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "unknown"}.");
            return new NextBillingDto(subscriptionId, after!.State?.Value ?? "", actual.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }, requestAborted);
}
