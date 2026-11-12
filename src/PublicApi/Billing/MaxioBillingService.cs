using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Subscription billing with Maxio as the system of record. Every failure the app cannot recover from
/// is raised as a <see cref="BillingException"/> carrying a 4xx/5xx status and a caller-safe reason.
/// </summary>
public class MaxioBillingService
{
    private const string BRAND_FAMILY_PREFIX = "brand-";
    private const int PAGE_SIZE = 200;
    private const int MAX_PAGES = 25;
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    // In-flight claims: a second identical request is refused before it reaches Maxio.
    private static readonly ConcurrentDictionary<string, byte> Claims = new();

    private readonly MaxioAdvancedBillingClient _client;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IReadRepository<CatalogBrand> _brands;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(MaxioAdvancedBillingClient client, UserManager<ApplicationUser> userManager,
        IReadRepository<CatalogBrand> brands, ILogger<MaxioBillingService> logger)
    {
        _client = client;
        _userManager = userManager;
        _brands = brands;
        _logger = logger;
    }

    // ───────────────────────── error boundary ─────────────────────────

    private async Task<T> Run<T>(Func<CancellationToken, Task<T>> call, CancellationToken requestAborted)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
        cts.CancelAfter(Budget);
        try
        {
            return await call(cts.Token);
        }
        catch (BillingException)
        {
            throw;
        }
        catch (OperationCanceledException) when (!requestAborted.IsCancellationRequested)
        {
            throw new BillingException(504, "Billing provider did not answer in time.");
        }
        catch (ApiException ex)
        {
            throw Translate(ex);
        }
        catch (SdkTimeoutException ex)
        {
            _logger.LogError(ex, "Maxio timeout");
            throw new BillingException(504, "Billing provider did not answer in time.", ex);
        }
        catch (SdkConnectionException ex)
        {
            _logger.LogError(ex, "Maxio unreachable");
            throw new BillingException(502, "Billing provider is unreachable.", ex);
        }
        catch (AuthSchemeException ex)
        {
            _logger.LogError(ex, "Maxio credentials missing or rejected");
            throw new BillingException(502, "Billing provider is unavailable.", ex);
        }
    }

    private BillingException Translate(ApiException ex)
    {
        var status = (int)ex.StatusCode;
        var reason = Reason(ex);
        if (status == 404) return new BillingException(404, reason ?? "Not found at the billing provider.", ex);
        if (status is 400 or 422)
        {
            _logger.LogWarning("Maxio rejected request: {Status} {Reason}", status, reason);
            return new BillingException(422, reason ?? "The billing provider rejected the request.", ex);
        }
        if (status == 409) return new BillingException(409, reason ?? "Conflict at the billing provider.", ex);
        // 401/403/429/5xx and unreadable bodies are not the caller's to fix.
        _logger.LogError(ex, "Maxio error {Status}", status);
        return new BillingException(502, "Billing provider is unavailable.", ex);
    }

    private static string? Reason(ApiException ex)
    {
        static string? Join(IReadOnlyList<string>? l) => l is { Count: > 0 } ? string.Join("; ", l) : null;
        static string? Raw(ApiError e) => e.TryGetRawError(out var r) ? Trim(r.ReadAsString()) : null;
        switch (ex)
        {
            case ApiException<RawError> r: return Trim(r.Error.ReadAsString());
            case ApiException<CreateSubscriptionError> e: return e.Error.TryGetErrorListResponse1(out var l1) ? Join(l1.Errors) : Raw(e.Error);
            case ApiException<UpdateSubscriptionError> e: return e.Error.TryGetErrorListResponse1(out var l2) ? Join(l2.Errors) : Raw(e.Error);
            case ApiException<PauseSubscriptionError> e: return e.Error.TryGetErrorListResponse1(out var l3) ? Join(l3.Errors) : Raw(e.Error);
            case ApiException<ResumeSubscriptionError> e: return e.Error.TryGetErrorListResponse1(out var l4) ? Join(l4.Errors) : Raw(e.Error);
            case ApiException<ReactivateSubscriptionError> e: return e.Error.TryGetErrorListResponse1(out var l5) ? Join(l5.Errors) : Raw(e.Error);
            case ApiException<CreateProductError> e: return e.Error.TryGetErrorListResponse1(out var l6) ? Join(l6.Errors) : Raw(e.Error);
            case ApiException<CreateProductFamilyError> e: return e.Error.TryGetErrorListResponse1(out var l7) ? Join(l7.Errors) : Raw(e.Error);
            case ApiException<CreateProductCurrencyPricesError> e:
                return e.Error.TryGetErrorArrayMapResponse1(out var m) && m.Errors is { Count: > 0 }
                    ? Trim(string.Join("; ", m.Errors.Select(kv => $"{kv.Key}: {FlattenErrors(kv.Value)}")))
                    : Raw(e.Error) ?? "Maxio rejected the currency price.";
            case ApiException<CancelSubscriptionError> e: return Raw(e.Error);
            case ApiException<CreateCustomerError> e: return Raw(e.Error);
            default: return null;
        }
    }

    private static string FlattenErrors(object value) =>
        value is System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.Array } arr
            ? string.Join(", ", arr.EnumerateArray().Select(x => x.ToString()).Distinct())
            : value.ToString() ?? "";

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : (s.Length > 300 ? s[..300] : s);

    private static IDisposable Claim(string key, string what)
    {
        if (!Claims.TryAdd(key, 0))
            throw new BillingException(409, $"A request for {what} is already in progress.");
        return new Release(key);
    }

    private sealed class Release : IDisposable
    {
        private readonly string _key;
        public Release(string key) => _key = key;
        public void Dispose() => Claims.TryRemove(_key, out _);
    }

    // ───────────────────────── plans ─────────────────────────

    private static int? BrandIdOf(Product p) =>
        p.ProductFamily?.Handle is { } h && h.StartsWith(BRAND_FAMILY_PREFIX, StringComparison.Ordinal)
        && int.TryParse(h[BRAND_FAMILY_PREFIX.Length..], out var id) ? id : null;

    private async Task<(List<Product> Plans, bool Truncated)> OfferedPlans(CancellationToken ct)
    {
        var plans = new List<Product>();
        var truncated = false;
        for (var page = 1; ; page++)
        {
            if (page > MAX_PAGES) { truncated = true; break; }
            var batch = await _client.Products.ListProducts(
                new MaxioAdvancedBilling.Requests.Products.ListProductsRequest { Page = page, PerPage = PAGE_SIZE },
                cancellationToken: ct);
            plans.AddRange(batch.Select(b => b.Product).Where(p => p != null)!);
            if (batch.Count < PAGE_SIZE) break;
        }
        // A plan is a non-archived product in one of the brand families, subscribable without a card.
        var offered = plans.Where(p => BrandIdOf(p) != null && p.ArchivedAt == null && p.RequireCreditCard != true).ToList();
        return (offered, truncated);
    }

    private async Task<Dictionary<int, string>> BrandNames(CancellationToken ct) =>
        (await _brands.ListAsync(ct)).ToDictionary(b => b.Id, b => b.Brand);

    private static PlanDto ToDto(Product p, IReadOnlyDictionary<int, string> brands)
    {
        var brandId = BrandIdOf(p);
        return new PlanDto(p.Id ?? 0, p.Handle ?? "", p.Name ?? "", (p.PriceInCents ?? 0) / 100m, brandId,
            brandId != null && brands.TryGetValue(brandId.Value, out var n) ? n : null);
    }

    public Task<ListPlansResponse> ListPlans(CancellationToken ct) => Run(async t =>
    {
        var (plans, truncated) = await OfferedPlans(t);
        var brands = await BrandNames(t);
        return new ListPlansResponse(plans.Select(p => ToDto(p, brands)).ToList(), truncated);
    }, ct);

    private async Task<Product> RequireOfferedPlan(string? handle, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(handle))
            throw new BillingException(400, "planHandle is required.");
        var (plans, truncated) = await OfferedPlans(ct);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, handle.Trim(), StringComparison.Ordinal));
        if (plan != null) return plan;
        if (truncated) throw new BillingException(503, "Plan catalog is too large to verify the plan.");
        throw new BillingException(404, $"'{handle}' is not a plan that can be subscribed to.");
    }

    // ───────────────────────── shoppers & ownership ─────────────────────────

    private async Task<int> EnsureCustomer(string username, CancellationToken ct)
    {
        try
        {
            var existing = await _client.Customers.ReadCustomerByReference(
                new MaxioAdvancedBilling.Requests.Customers.ReadCustomerByReferenceRequest { Reference = username },
                cancellationToken: ct);
            if (existing.Customer?.Id is { } id) return id;
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound) { }

        var user = await _userManager.FindByNameAsync(username)
                   ?? throw new BillingException(401, "Unknown user.");
        if (string.IsNullOrWhiteSpace(user.Email))
            throw new BillingException(422, "The signed-in user has no email address, which billing requires.");

        using var claim = Claim($"customer:{username}", "creating your billing profile");
        try
        {
            var created = await _client.Customers.CreateCustomer(
                new MaxioAdvancedBilling.Requests.Customers.CreateCustomerOperationRequest
                {
                    Body = new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = username.Split('@')[0],
                            LastName = "Shopper",
                            Email = user.Email,
                            Reference = username
                        }
                    }
                }, cancellationToken: ct);
            return created.Customer?.Id ?? throw new BillingException(502, "Billing provider returned no customer.");
        }
        catch (SdkConnectionException)
        {
            // The write may have landed: settle it by re-reading with the reference we sent.
            var settled = await _client.Customers.ReadCustomerByReference(
                new MaxioAdvancedBilling.Requests.Customers.ReadCustomerByReferenceRequest { Reference = username },
                cancellationToken: ct);
            return settled.Customer?.Id ?? throw new BillingException(502, "Billing profile could not be confirmed.");
        }
    }

    private async Task<Subscription> Owned(int subscriptionId, ClaimsPrincipal user, CancellationToken ct)
    {
        var username = user.Identity?.Name ?? throw new BillingException(401, "Not signed in.");
        var sub = await ReadSub(subscriptionId, ct);
        if (!string.Equals(sub.Customer?.Reference, username, StringComparison.Ordinal))
            throw new BillingException(404, "Subscription not found."); // do not leak existence
        return sub;
    }

    private async Task<Subscription> ReadSub(int id, CancellationToken ct)
    {
        var r = await _client.Subscriptions.ReadSubscription(
            new MaxioAdvancedBilling.Requests.Subscriptions.ReadSubscriptionRequest { SubscriptionId = id },
            cancellationToken: ct);
        return r.Subscription ?? throw new BillingException(502, "Billing provider returned no subscription.");
    }

    private static SubscriptionDto ToDto(Subscription s) => new(
        s.Id ?? 0, s.Product?.Handle, s.Product?.Name, s.State?.Value,
        s.BalanceInCents == null ? null : s.BalanceInCents <= 0, // owes nothing: zero or in credit
        s.BalanceInCents, s.CurrentPeriodEndsAt);

    // ───────────────────────── shopper features ─────────────────────────

    public Task<SubscriptionDto> Subscribe(ClaimsPrincipal user, string? planHandle, CancellationToken ct) => Run(async t =>
    {
        var username = user.Identity?.Name ?? throw new BillingException(401, "Not signed in.");
        var plan = await RequireOfferedPlan(planHandle, t);
        var customerId = await EnsureCustomer(username, t);

        using var claim = Claim($"sub:{username}:{plan.Handle}", "this subscription");
        var reference = Guid.NewGuid().ToString("N");
        try
        {
            var r = await _client.Subscriptions.CreateSubscription(
                new MaxioAdvancedBilling.Requests.Subscriptions.CreateSubscriptionOperationRequest
                {
                    Body = new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = plan.Handle,
                            CustomerId = customerId,
                            PaymentCollectionMethod = CollectionMethod.Remittance, // no card details
                            Reference = reference
                        }
                    }
                }, cancellationToken: t);
            return ToDto(r.Subscription ?? throw new BillingException(502, "Billing provider returned no subscription."));
        }
        catch (SdkConnectionException)
        {
            // Outcome unknown: re-read by the reference we sent before reporting anything.
            var found = await _client.Subscriptions.FindSubscription(
                new MaxioAdvancedBilling.Requests.Subscriptions.FindSubscriptionRequest { Reference = reference },
                cancellationToken: t);
            return ToDto(found.Subscription ?? throw new BillingException(502, "Subscription could not be confirmed."));
        }
    }, ct);

    public Task<SubscriptionDto> GetSubscription(ClaimsPrincipal user, int id, CancellationToken ct) => Run(async t =>
    {
        var sub = await Owned(id, user, t);
        if (sub.BalanceInCents == null)
            throw new BillingException(502, "Billing provider did not report the balance.");
        return ToDto(sub);
    }, ct);

    public Task<SubscriptionDto> Pause(ClaimsPrincipal user, int id, CancellationToken ct) => Run(async t =>
    {
        var sub = await Owned(id, user, t);
        if (sub.State != SubscriptionState.Active && sub.State != SubscriptionState.Trialing)
            throw new BillingException(409, $"A subscription in state '{sub.State?.Value}' cannot be paused.");
        var r = await _client.SubscriptionStatus.PauseSubscription(
            new MaxioAdvancedBilling.Requests.SubscriptionStatus.PauseSubscriptionRequest { SubscriptionId = id },
            cancellationToken: t);
        return ToDto(r.Subscription ?? sub);
    }, ct);

    public Task<SubscriptionDto> Resume(ClaimsPrincipal user, int id, CancellationToken ct) => Run(async t =>
    {
        var sub = await Owned(id, user, t);
        if (sub.State != SubscriptionState.OnHold)
            throw new BillingException(409, $"Only a paused subscription can be resumed; this one is '{sub.State?.Value}'.");
        var r = await _client.SubscriptionStatus.ResumeSubscription(
            new MaxioAdvancedBilling.Requests.SubscriptionStatus.ResumeSubscriptionRequest { SubscriptionId = id },
            cancellationToken: t);
        return ToDto(r.Subscription ?? sub);
    }, ct);

    public Task<SubscriptionDto> Cancel(ClaimsPrincipal user, int id, CancellationToken ct) => Run(async t =>
    {
        var sub = await Owned(id, user, t);
        if (sub.State == SubscriptionState.Canceled)
            throw new BillingException(409, "The subscription is already cancelled.");
        var r = await _client.SubscriptionStatus.CancelSubscription(
            new MaxioAdvancedBilling.Requests.SubscriptionStatus.CancelSubscriptionRequest { SubscriptionId = id },
            cancellationToken: t);
        return ToDto(r.Subscription ?? sub);
    }, ct);

    public Task<SubscriptionDto> Restart(ClaimsPrincipal user, int id, CancellationToken ct) => Run(async t =>
    {
        var sub = await Owned(id, user, t);
        if (sub.State != SubscriptionState.Canceled)
            throw new BillingException(409, "Only a cancelled subscription can be restarted.");
        var r = await _client.SubscriptionStatus.ReactivateSubscription(
            new MaxioAdvancedBilling.Requests.SubscriptionStatus.ReactivateSubscriptionOperationRequest { SubscriptionId = id },
            cancellationToken: t);
        return ToDto(r.Subscription ?? sub);
    }, ct);

    public Task<SubscriptionDto> Switch(ClaimsPrincipal user, int id, string? planHandle, CancellationToken ct) => Run(async t =>
    {
        var sub = await Owned(id, user, t);
        var plan = await RequireOfferedPlan(planHandle, t);
        if (string.Equals(sub.Product?.Handle, plan.Handle, StringComparison.Ordinal))
            throw new BillingException(409, "The subscription is already on that plan.");
        await _client.Subscriptions.UpdateSubscription(
            new MaxioAdvancedBilling.Requests.Subscriptions.UpdateSubscriptionOperationRequest
            {
                SubscriptionId = id,
                Body = new UpdateSubscriptionRequest { Subscription = new UpdateSubscription { ProductHandle = plan.Handle } }
            }, cancellationToken: t);
        var after = await ReadSub(id, t);
        if (!string.Equals(after.Product?.Handle, plan.Handle, StringComparison.Ordinal))
            throw new BillingException(502, "The billing provider did not move the subscription to the new plan.");
        return ToDto(after);
    }, ct);

    public Task<UsageResponseDto> Usage(ClaimsPrincipal user, int id, string? date, CancellationToken ct) => Run(async t =>
    {
        if (!DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            throw new BillingException(400, "date must be in the form YYYY-MM-DD.");
        await Owned(id, user, t);

        var comps = await _client.SubscriptionComponents.ListSubscriptionComponents(
            new MaxioAdvancedBilling.Requests.SubscriptionComponents.ListSubscriptionComponentsRequest { SubscriptionId = id },
            cancellationToken: t);

        var entries = new List<UsageEntryDto>();
        var truncated = false;
        foreach (var c in comps.Select(x => x.Component).Where(c => c?.ComponentId != null && c.Kind == ComponentKind.MeteredComponent))
        {
            for (var page = 1; ; page++)
            {
                if (page > MAX_PAGES) { truncated = true; break; }
                var batch = await _client.SubscriptionComponents.ListUsages(
                    new MaxioAdvancedBilling.Requests.SubscriptionComponents.ListUsagesRequest
                    {
                        SubscriptionIdOrReference = id,
                        ComponentId = c!.ComponentId!.Value,
                        SinceDate = new DateTimeOffset(day, TimeSpan.Zero),
                        UntilDate = new DateTimeOffset(day.AddDays(1), TimeSpan.Zero),
                        Page = page,
                        PerPage = PAGE_SIZE
                    }, cancellationToken: t);
                foreach (var u in batch.Select(b => b.Usage))
                {
                    // until_date is inclusive of the next midnight: keep only the requested calendar day.
                    if (u.CreatedAt is { } at && at.Date != day.Date) continue;
                    entries.Add(new UsageEntryDto(u.Id, u.ComponentHandle ?? c.ComponentHandle, u.ComponentId ?? c.ComponentId,
                        u.Quantity?.Match(i => i.ToString(CultureInfo.InvariantCulture), s => s, raw => raw.ToString()),
                        u.Memo, u.CreatedAt));
                }
                if (batch.Count < PAGE_SIZE) break;
            }
        }
        return new UsageResponseDto(id, day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), entries, truncated);
    }, ct);

    // ───────────────────────── staff features ─────────────────────────

    public Task<PlanDto> CreatePlan(string? name, decimal monthlyPrice, int brandId, CancellationToken ct) => Run(async t =>
    {
        if (string.IsNullOrWhiteSpace(name)) throw new BillingException(400, "name is required.");
        if (monthlyPrice <= 0 || decimal.Round(monthlyPrice, 2) != monthlyPrice)
            throw new BillingException(400, "monthlyPrice must be positive with at most 2 decimal places.");
        var brand = await _brands.GetByIdAsync(brandId, t)
                    ?? throw new BillingException(404, $"Brand {brandId} is not in the catalog.");

        var (existing, _) = await OfferedPlans(t);
        if (existing.Any(p => BrandIdOf(p) == brandId && string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new BillingException(409, $"Brand '{brand.Brand}' already has a plan named '{name.Trim()}'.");

        using var claim = Claim($"plan:{brandId}:{name.Trim().ToLowerInvariant()}", "this plan");
        var familyHandle = await EnsureFamily(brandId, brand.Brand, t);
        var handle = $"plan-{brandId}-{Slug(name)}-{Guid.NewGuid().ToString("N")[..6]}";
        var brandMap = new Dictionary<int, string> { [brandId] = brand.Brand };
        try
        {
            var r = await _client.Products.CreateProduct(
                new MaxioAdvancedBilling.Requests.Products.CreateProductRequest
                {
                    ProductFamilyId = $"handle:{familyHandle}",
                    Body = new CreateOrUpdateProductRequest
                    {
                        Product = new CreateOrUpdateProduct
                        {
                            Name = name.Trim(),
                            Handle = handle,
                            Description = name.Trim(),
                            PriceInCents = (long)(monthlyPrice * 100m),
                            Interval = 1,
                            IntervalUnit = IntervalUnit.Month,
                            RequireCreditCard = false
                        }
                    }
                }, cancellationToken: t);
            return ToDto(r.Product ?? throw new BillingException(502, "Billing provider returned no plan."), brandMap);
        }
        catch (SdkConnectionException)
        {
            var found = await _client.Products.ReadProductByHandle(
                new MaxioAdvancedBilling.Requests.Products.ReadProductByHandleRequest { ApiHandle = handle }, cancellationToken: t);
            return ToDto(found.Product ?? throw new BillingException(502, "Plan could not be confirmed."), brandMap);
        }
    }, ct);

    private static string Slug(string s)
    {
        var chars = s.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) && c < 128 ? c : '-').ToArray();
        var slug = string.Join("-", new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return slug.Length == 0 ? "plan" : (slug.Length > 40 ? slug[..40] : slug);
    }

    private async Task<string> EnsureFamily(int brandId, string brandName, CancellationToken ct)
    {
        var handle = BRAND_FAMILY_PREFIX + brandId;
        async Task<bool> Exists() =>
            (await _client.ProductFamilies.ListProductFamilies(
                new MaxioAdvancedBilling.Requests.ProductFamilies.ListProductFamiliesRequest(), cancellationToken: ct))
            .Any(f => f.ProductFamily?.Handle == handle && f.ProductFamily.ArchivedAt == null);

        if (await Exists()) return handle;
        try
        {
            await _client.ProductFamilies.CreateProductFamily(
                new MaxioAdvancedBilling.Requests.ProductFamilies.CreateProductFamilyOperationRequest
                {
                    Body = new CreateProductFamilyRequest
                    {
                        ProductFamily = new CreateProductFamily { Name = brandName, Handle = handle }
                    }
                }, cancellationToken: ct);
        }
        catch (SdkConnectionException)
        {
            if (!await Exists()) throw new BillingException(502, "Brand family could not be confirmed.");
        }
        return handle;
    }

    public Task<IReadOnlyList<CurrencyPriceDto>> AddCurrencyPrice(int planId, string? currency, decimal price, CancellationToken ct) =>
        Run<IReadOnlyList<CurrencyPriceDto>>(async t =>
    {
        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3 || !currency.Trim().All(char.IsLetter))
            throw new BillingException(400, "currency must be a 3-letter ISO code.");
        if (price <= 0 || price != decimal.Truncate(price) || price > int.MaxValue)
            throw new BillingException(400, "price must be a positive whole number.");
        var code = currency.Trim().ToUpperInvariant();

        var product = (await _client.Products.ReadProduct(
            new MaxioAdvancedBilling.Requests.Products.ReadProductRequest { ProductId = planId }, cancellationToken: t)).Product;
        if (product == null || BrandIdOf(product) == null || product.ArchivedAt != null)
            throw new BillingException(404, $"Plan {planId} not found.");
        var pricePointId = product.DefaultProductPricePointId
                           ?? throw new BillingException(422, "The plan has no default price point to price.");

        using var claim = Claim($"ccy:{planId}:{code}", "this currency price");
        var r = await _client.ProductPricePoints.CreateProductCurrencyPrices(
            new MaxioAdvancedBilling.Requests.ProductPricePoints.CreateProductCurrencyPricesOperationRequest
            {
                ProductPricePointId = pricePointId,
                Body = new CreateProductCurrencyPricesRequest
                {
                    CurrencyPrices = new[]
                    {
                        new CreateProductCurrencyPrice { Currency = code, Price = (int)price, Role = CurrencyPriceRole.Baseline }
                    }
                }
            }, cancellationToken: t);
        return r.CurrencyPrices.Select(c => new CurrencyPriceDto(c.Currency, c.Price, c.FormattedPrice)).ToList();
    }, ct);

    public Task<SubscriptionDto> SetNextBillingDate(int id, string? date, CancellationToken ct) => Run(async t =>
    {
        if (!DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            throw new BillingException(400, "date must be in the form YYYY-MM-DD.");
        if (day.Date <= DateTime.UtcNow.Date)
            throw new BillingException(400, "date must be in the future.");

        var sub = await ReadSub(id, t);
        if (sub.State == SubscriptionState.Canceled || sub.State == SubscriptionState.Expired)
            throw new BillingException(409, $"A subscription in state '{sub.State?.Value}' is not billed.");
        if (!string.IsNullOrEmpty(sub.SnapDay))
            throw new BillingException(422, "Calendar-billing subscriptions cannot be given a next billing date.");

        await _client.Subscriptions.UpdateSubscription(
            new MaxioAdvancedBilling.Requests.Subscriptions.UpdateSubscriptionOperationRequest
            {
                SubscriptionId = id,
                Body = new UpdateSubscriptionRequest
                {
                    Subscription = new UpdateSubscription { NextBillingAt = new DateTimeOffset(day.AddHours(12), TimeSpan.Zero) }
                }
            }, cancellationToken: t);

        // The update does not echo the date: verify it from the subscription.
        var after = await ReadSub(id, t);
        var actual = after.CurrentPeriodEndsAt ?? after.NextAssessmentAt;
        if (actual == null || Math.Abs((actual.Value.UtcDateTime.Date - day.Date).TotalDays) > 1)
            throw new BillingException(409, $"The billing provider set the next billing date to {actual?.ToString("yyyy-MM-dd") ?? "an unknown date"} instead.");
        return ToDto(after);
    }, ct);
}
