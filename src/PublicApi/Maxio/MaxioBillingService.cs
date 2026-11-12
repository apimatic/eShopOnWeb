using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using SDK = MaxioAdvancedBilling.Requests;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// All Maxio calls for subscription billing. Maxio is the system of record: the app keeps no billing state.
/// A plan is a Maxio product inside the product family of a catalog brand (handle <c>eshop-brand-{brandId}</c>).
/// </summary>
public class MaxioBillingService
{
    public const string FamilyPrefix = "eshop-brand-";
    private const int MaxPages = 25;
    private const int PerPage = 200;
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    // Maxio allows 4 concurrent calls per site; in-process gate.
    private static readonly SemaphoreSlim Gate = new(4);
    // Serialises writes per key (one shopper / one subscription / one plan handle at a time) in this process.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();

    private readonly MaxioAdvancedBillingClient _client;
    private readonly IReadRepository<CatalogBrand> _brands;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(MaxioAdvancedBillingClient client, IReadRepository<CatalogBrand> brands,
        UserManager<ApplicationUser> users, ILogger<MaxioBillingService> logger)
    {
        _client = client;
        _brands = brands;
        _users = users;
        _logger = logger;
    }

    // ---------------------------------------------------------------- plumbing

    private static CancellationToken Deadline(CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Budget);
        return cts.Token;
    }

    private async Task<T> Call<T>(Func<Task<T>> call)
    {
        await Gate.WaitAsync();
        try
        {
            return await call();
        }
        catch (MaxioBillingException) { throw; }
        catch (ApiException ex)
        {
            throw Translate(ex);
        }
        catch (SdkTimeoutException ex)
        {
            throw new MaxioBillingException(504, "The billing system timed out.", ex);
        }
        catch (SdkConnectionException ex)
        {
            throw new MaxioBillingException(502, "The billing system could not be reached.", ex);
        }
        catch (SdkException ex)
        {
            _logger.LogError(ex, "Unexpected Maxio SDK failure");
            throw new MaxioBillingException(502, "The billing system call failed.", ex);
        }
        catch (OperationCanceledException ex)
        {
            throw new MaxioBillingException(504, "The billing request took too long.", ex);
        }
        finally
        {
            Gate.Release();
        }
    }

    private MaxioBillingException Translate(ApiException ex)
    {
        var status = (int)ex.StatusCode;
        var reason = ex is ResponseDeserializationException ? null : Reason(ex);
        _logger.LogWarning("Maxio returned {Status}: {Reason}", status, reason);
        if (ex is ResponseDeserializationException || status < 400)
            return new MaxioBillingException(502, "The billing system returned an unexpected response.", ex);
        return status switch
        {
            404 => new MaxioBillingException(404, reason ?? "Not found.", ex),
            409 => new MaxioBillingException(409, reason ?? "Conflict.", ex),
            422 or 400 => new MaxioBillingException(422, reason ?? "The billing system refused the request.", ex),
            429 => new MaxioBillingException(503, "The billing system is busy, try again shortly.", ex),
            _ => new MaxioBillingException(502, "The billing system call failed.", ex),
        };
    }

    private static string? Reason(ApiException ex)
    {
        var error = ex.GetType().GetProperty("Error")?.GetValue(ex);
        if (error is RawError raw) return Clip(raw.ReadAsString());
        if (error is ApiError api)
        {
            foreach (var m in api.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                         .Where(m => m.Name.StartsWith("TryGet") && m.GetParameters().Length == 1))
            {
                var args = new object?[] { null };
                if (m.Invoke(api, args) is true && args[0] is { } v)
                {
                    if (v is ErrorListResponse1 list) return string.Join("; ", list.Errors);
                    if (v is RawError r) return Clip(r.ReadAsString());
                    return Clip(System.Text.Json.JsonSerializer.Serialize(v, v.GetType()));
                }
            }
        }
        return null;
    }

    private static string Clip(string s) => s.Length <= 500 ? s : s[..500];

    private async Task<T> Locked<T>(string key, Func<Task<T>> body)
    {
        var sem = Locks.GetOrAdd(key, _ => new SemaphoreSlim(1));
        if (!await sem.WaitAsync(0))
            throw new MaxioBillingException(409, "Another change to this resource is already in progress.");
        try { return await body(); }
        finally { sem.Release(); }
    }

    private static MaxioBillingException Bad(string message) => new(400, message);

    // ---------------------------------------------------------------- plans

    private static int? BrandIdOf(ProductFamily? f)
    {
        var h = f?.Handle;
        if (h != null && h.StartsWith(FamilyPrefix, StringComparison.Ordinal)
            && int.TryParse(h[FamilyPrefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var id)) return id;
        return null;
    }

    private async Task<List<ProductFamily>> BrandFamilies(CancellationToken ct)
    {
        var fams = await Call(() => _client.ProductFamilies.ListProductFamilies(new SDK.ProductFamilies.ListProductFamiliesRequest(), null, ct));
        return fams.Select(f => f.ProductFamily).Where(f => f != null && f.Id != null && BrandIdOf(f) != null && f.ArchivedAt == null).ToList()!;
    }

    /// <summary>The plans this shop offers: live products in the families of catalog brands.</summary>
    public async Task<IReadOnlyList<PlanDto>> ListPlans(CancellationToken ct)
    {
        ct = Deadline(ct);
        return await ListPlansCore(ct);
    }

    private async Task<IReadOnlyList<PlanDto>> ListPlansCore(CancellationToken ct)
    {
        var brands = (await _brands.ListAsync(ct)).ToDictionary(b => b.Id, b => b.Brand);
        var plans = new List<PlanDto>();
        foreach (var fam in await BrandFamilies(ct))
        {
            var brandId = BrandIdOf(fam)!.Value;
            if (!brands.TryGetValue(brandId, out var brandName)) continue;
            for (var page = 1; page <= MaxPages; page++)
            {
                var products = await Call(() => _client.ProductFamilies.ListProductsForProductFamily(
                    new SDK.ProductFamilies.ListProductsForProductFamilyRequest
                    { ProductFamilyId = fam.Id!.Value.ToString(CultureInfo.InvariantCulture), Page = page, PerPage = PerPage }, null, ct));
                foreach (var p in products.Select(x => x.Product).Where(p => p.ArchivedAt == null && p.Handle != null && p.Id != null))
                    plans.Add(ToPlan(p, new BrandRef(brandId, brandName)));
                if (products.Count < PerPage) break;
            }
        }
        return plans;
    }

    private static PlanDto ToPlan(Product p, BrandRef? brand) =>
        new(p.Id!.Value, p.Handle!, p.Name ?? p.Handle!, p.PriceInCents ?? 0, (p.PriceInCents ?? 0) / 100m, brand);

    private async Task<PlanDto> OfferedByHandle(string? handle, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(handle)) throw Bad("A plan handle is required.");
        var plan = (await ListPlansCore(ct)).FirstOrDefault(p => string.Equals(p.Handle, handle.Trim(), StringComparison.Ordinal));
        return plan ?? throw new MaxioBillingException(404, $"Plan '{handle}' is not offered.");
    }

    public async Task<PlanDto> CreatePlan(CreatePlanRequest req, CancellationToken ct)
    {
        ct = Deadline(ct);
        if (string.IsNullOrWhiteSpace(req.Name)) throw Bad("A plan name is required.");
        if (req.MonthlyPrice is null || req.MonthlyPrice <= 0) throw Bad("The monthly price must be greater than zero.");
        if (decimal.Round(req.MonthlyPrice.Value, 2) != req.MonthlyPrice.Value) throw Bad("The monthly price may have at most 2 decimals.");
        if (req.CatalogBrandId is null) throw Bad("A catalog brand id is required.");
        var brand = await _brands.GetByIdAsync(req.CatalogBrandId.Value, ct);
        if (brand == null) throw Bad($"Brand {req.CatalogBrandId} is not in the catalog.");

        var famHandle = FamilyPrefix + brand.Id.ToString(CultureInfo.InvariantCulture);
        var handle = Slug(req.Name);
        if (handle.Length == 0) throw Bad("The plan name must contain letters or digits.");
        var cents = (long)decimal.Round(req.MonthlyPrice.Value * 100m, 0);

        return await Locked("plan:" + handle, async () =>
        {
            var famId = await EnsureFamily(famHandle, brand, ct);
            var existing = await ListPlansCore(ct);
            if (existing.Any(p => p.Handle == handle)) throw new MaxioBillingException(409, $"A plan with handle '{handle}' already exists.");
            try
            {
                var created = await Call(() => _client.Products.CreateProduct(new SDK.Products.CreateProductRequest
                {
                    ProductFamilyId = famId.ToString(CultureInfo.InvariantCulture),
                    Body = new CreateOrUpdateProductRequest
                    {
                        Product = new CreateOrUpdateProduct
                        {
                            Name = req.Name.Trim(),
                            Handle = handle,
                            Description = $"{req.Name.Trim()} ({brand.Brand})",
                            PriceInCents = cents,
                            Interval = 1,
                            IntervalUnit = IntervalUnit.Month,
                            RequireCreditCard = false,
                        }
                    }
                }, null, ct));
                return ToPlan(created.Product, new BrandRef(brand.Id, brand.Brand));
            }
            catch (MaxioBillingException ex) when (ex.StatusCode == 422)
            {
                throw new MaxioBillingException(409, "Maxio refused the plan: " + ex.Message, ex);
            }
        });
    }

    private async Task<int> EnsureFamily(string famHandle, CatalogBrand brand, CancellationToken ct)
    {
        var fams = await Call(() => _client.ProductFamilies.ListProductFamilies(new SDK.ProductFamilies.ListProductFamiliesRequest(), null, ct));
        var found = fams.Select(f => f.ProductFamily).FirstOrDefault(f => f?.Handle == famHandle);
        if (found?.Id != null) return found.Id.Value;
        try
        {
            var created = await Call(() => _client.ProductFamilies.CreateProductFamily(new SDK.ProductFamilies.CreateProductFamilyOperationRequest
            {
                Body = new CreateProductFamilyRequest
                {
                    ProductFamily = new CreateProductFamily { Name = brand.Brand, Handle = famHandle, Description = $"Plans for brand {brand.Brand}" }
                }
            }, null, ct));
            return created.ProductFamily!.Id!.Value;
        }
        catch (MaxioBillingException ex) when (ex.StatusCode is 422 or 502 or 504)
        {
            // Lost a race or the outcome is unknown: re-read by handle.
            var again = await Call(() => _client.ProductFamilies.ListProductFamilies(new SDK.ProductFamilies.ListProductFamiliesRequest(), null, CancellationToken.None));
            var f = again.Select(x => x.ProductFamily).FirstOrDefault(x => x?.Handle == famHandle);
            return f?.Id ?? throw ex;
        }
    }

    private static string Slug(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name.Trim().ToLowerInvariant())
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        return sb.ToString().Trim('-');
    }

    public async Task<CurrencyPriceDto> SetCurrencyPrice(int planId, CurrencyPriceRequest req, CancellationToken ct)
    {
        ct = Deadline(ct);
        if (string.IsNullOrWhiteSpace(req.Currency)) throw Bad("A currency is required.");
        if (req.Price is null || req.Price <= 0) throw Bad("The price must be greater than zero.");
        if (decimal.Round(req.Price.Value, 2) != req.Price.Value) throw Bad("The price may have at most 2 decimals.");
        var currency = req.Currency.Trim().ToUpperInvariant();

        if ((await ListPlansCore(ct)).All(p => p.PlanId != planId))
            throw new MaxioBillingException(404, $"Plan {planId} is not one of this shop's plans.");
        var site = (await Call(() => _client.Sites.ReadSite(null, ct))).Site;
        if (string.Equals(site.Currency, currency, StringComparison.OrdinalIgnoreCase))
            throw Bad($"{currency} is the plan's own currency; its price is the monthly price.");
        if (site.NonPrimaryCurrencies == null || !site.NonPrimaryCurrencies.Contains(currency, StringComparer.OrdinalIgnoreCase))
            throw Bad($"{currency} is not a currency configured for this billing site.");

        return await Locked($"plan-price:{planId}", async () =>
        {
            var product = (await Call(() => _client.Products.ReadProduct(new SDK.Products.ReadProductRequest { ProductId = planId }, null, ct))).Product;
            var ppId = product.DefaultProductPricePointId
                       ?? throw new MaxioBillingException(502, "The plan has no default price point.");
            var points = await Call(() => _client.ProductPricePoints.ListProductPricePoints(new SDK.ProductPricePoints.ListProductPricePointsRequest
            { ProductId = planId, CurrencyPrices = true, PerPage = PerPage }, null, ct));
            var point = points.PricePoints.FirstOrDefault(p => p.Id == ppId)
                        ?? throw new MaxioBillingException(502, "The plan's default price point was not found.");
            var existing = point.CurrencyPrices?.FirstOrDefault(c => string.Equals(c.Currency, currency, StringComparison.OrdinalIgnoreCase));

            CurrencyPrice Pick(CurrencyPricesResponse r, CurrencyPrice fallback) =>
                r.CurrencyPrices.FirstOrDefault(c => string.Equals(c.Currency, currency, StringComparison.OrdinalIgnoreCase)) ?? fallback;

            CurrencyPrice result;
            if (existing?.Id != null)
            {
                var r = await Call(() => _client.ProductPricePoints.UpdateProductCurrencyPrices(new SDK.ProductPricePoints.UpdateProductCurrencyPricesRequest
                {
                    ProductPricePointId = ppId,
                    Body = new UpdateCurrencyPricesRequest
                    { CurrencyPrices = [new UpdateCurrencyPrice { Id = existing.Id.Value, Price = (double)req.Price.Value }] }
                }, null, ct));
                result = Pick(r, existing);
            }
            else
            {
                var whole = decimal.Truncate(req.Price.Value);
                var r = await Call(() => _client.ProductPricePoints.CreateProductCurrencyPrices(new SDK.ProductPricePoints.CreateProductCurrencyPricesOperationRequest
                {
                    ProductPricePointId = ppId,
                    Body = new CreateProductCurrencyPricesRequest
                    { CurrencyPrices = [new CreateProductCurrencyPrice { Currency = currency, Price = (int)whole, Role = CurrencyPriceRole.Baseline }] }
                }, null, ct));
                result = r.CurrencyPrices.FirstOrDefault(c => string.Equals(c.Currency, currency, StringComparison.OrdinalIgnoreCase)) ?? r.CurrencyPrices.First();
                if (whole != req.Price.Value && result.Id != null)
                {
                    // Create only takes whole numbers; set the exact amount through update.
                    var u = await Call(() => _client.ProductPricePoints.UpdateProductCurrencyPrices(new SDK.ProductPricePoints.UpdateProductCurrencyPricesRequest
                    {
                        ProductPricePointId = ppId,
                        Body = new UpdateCurrencyPricesRequest
                        { CurrencyPrices = [new UpdateCurrencyPrice { Id = result.Id.Value, Price = (double)req.Price.Value }] }
                    }, null, ct));
                    result = Pick(u, result);
                }
            }
            return new CurrencyPriceDto(planId, currency, (decimal)(result.Price ?? (double)req.Price.Value), result.FormattedPrice);
        });
    }

    // ---------------------------------------------------------------- subscriptions

    private static readonly SubscriptionState[] Dead = [SubscriptionState.Canceled, SubscriptionState.Expired];

    private async Task<Subscription> ReadSub(int id, CancellationToken ct)
    {
        var r = await Call(() => _client.Subscriptions.ReadSubscription(new SDK.Subscriptions.ReadSubscriptionRequest { SubscriptionId = id }, null, ct));
        return r.Subscription ?? throw new MaxioBillingException(502, "The billing system returned no subscription.");
    }

    /// <summary>Reads the subscription and confirms it belongs to the caller; anything else is a 404.</summary>
    private async Task<Subscription> Owned(int id, string user, CancellationToken ct)
    {
        Subscription sub;
        try { sub = await ReadSub(id, ct); }
        catch (MaxioBillingException ex) when (ex.StatusCode == 404) { throw new MaxioBillingException(404, "Subscription not found."); }
        if (!string.Equals(sub.Customer?.Reference, user, StringComparison.Ordinal))
            throw new MaxioBillingException(404, "Subscription not found.");
        return sub;
    }

    private async Task<string> CanonicalUser(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new MaxioBillingException(401, "Sign in to manage subscriptions.");
        var u = await _users.FindByNameAsync(name);
        return u?.UserName ?? throw new MaxioBillingException(401, "Unknown user.");
    }

    public async Task<SubscriptionDto> Subscribe(string? userName, SubscribeRequest req, CancellationToken ct)
    {
        ct = Deadline(ct);
        var user = await CanonicalUser(userName);
        var plan = await OfferedByHandle(req.PlanHandle, ct);
        return await Locked("user:" + user, async () =>
        {
            var customerId = await EnsureCustomer(user, ct);

            // One live subscription per plan per shopper (several different plans are fine).
            for (var page = 1; page <= MaxPages; page++)
            {
                var subs = await Call(() => _client.Subscriptions.ListSubscriptions(new SDK.Subscriptions.ListSubscriptionsRequest
                { CustomerId = customerId, Page = page, PerPage = PerPage }, null, ct));
                if (subs.Any(s => s.Subscription?.Product?.Handle == plan.Handle && s.Subscription.State != null && !Dead.Contains(s.Subscription.State)))
                    throw new MaxioBillingException(409, "You already have a subscription to this plan.");
                if (subs.Count < PerPage) break;
            }

            var site = (await Call(() => _client.Sites.ReadSite(null, ct))).Site;
            var method = site.RelationshipInvoicingEnabled == true ? CollectionMethod.Remittance : CollectionMethod.Invoice;
            var reference = Guid.NewGuid().ToString("N");
            Subscription created;
            try
            {
                created = (await Call(() => _client.Subscriptions.CreateSubscription(new SDK.Subscriptions.CreateSubscriptionOperationRequest
                {
                    Body = new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = plan.Handle,
                            CustomerId = customerId,
                            PaymentCollectionMethod = method,
                            Reference = reference,
                        }
                    }
                }, null, ct))).Subscription!;
            }
            catch (MaxioBillingException ex) when (ex.StatusCode is 502 or 504)
            {
                // Outcome unknown: settle it by the reference we sent.
                var found = await FindByReference(reference, CancellationToken.None);
                if (found == null) throw;
                created = found;
            }
            return await ToDto(created, ct);
        });
    }

    private async Task<Subscription?> FindByReference(string reference, CancellationToken ct)
    {
        try
        {
            var r = await Call(() => _client.Subscriptions.FindSubscription(new SDK.Subscriptions.FindSubscriptionRequest { Reference = reference }, null, ct));
            return r.Subscription;
        }
        catch (MaxioBillingException ex) when (ex.StatusCode == 404) { return null; }
    }

    private async Task<int> EnsureCustomer(string user, CancellationToken ct)
    {
        try
        {
            var c = await Call(() => _client.Customers.ReadCustomerByReference(new SDK.Customers.ReadCustomerByReferenceRequest { Reference = user }, null, ct));
            if (c.Customer.Id != null) return c.Customer.Id.Value;
        }
        catch (MaxioBillingException ex) when (ex.StatusCode == 404) { }

        var u = await _users.FindByNameAsync(user) ?? throw new MaxioBillingException(401, "Unknown user.");
        var email = u.Email ?? u.UserName!;
        var local = email.Split('@')[0];
        try
        {
            var created = await Call(() => _client.Customers.CreateCustomer(new SDK.Customers.CreateCustomerOperationRequest
            {
                Body = new CreateCustomerRequest
                { Customer = new CreateCustomer { FirstName = local, LastName = "Shopper", Email = email, Reference = user } }
            }, null, ct));
            return created.Customer.Id ?? throw new MaxioBillingException(502, "The billing system returned no customer id.");
        }
        catch (MaxioBillingException ex) when (ex.StatusCode is 422 or 502 or 504)
        {
            // Duplicate reference or unknown outcome: re-read by reference.
            var c = await Call(() => _client.Customers.ReadCustomerByReference(new SDK.Customers.ReadCustomerByReferenceRequest { Reference = user }, null, CancellationToken.None));
            return c.Customer.Id ?? throw ex;
        }
    }

    private async Task<SubscriptionDto> ToDto(Subscription s, CancellationToken ct)
    {
        var owes = (s.BalanceInCents ?? 0) > 0;
        if (!owes && s.Id != null)
        {
            try
            {
                var bal = await Call(() => _client.SubscriptionInvoiceAccount.ReadAccountBalances(new SDK.SubscriptionInvoiceAccount.ReadAccountBalancesRequest { SubscriptionId = s.Id.Value }, null, ct));
                owes = (bal.OpenInvoices?.BalanceInCents ?? 0) > 0;
            }
            catch (MaxioBillingException ex) when (ex.StatusCode is 404 or 422) { }
        }
        var st = s.State;
        if (st == SubscriptionState.PastDue || st == SubscriptionState.Unpaid || st == SubscriptionState.SoftFailure || st == SubscriptionState.Suspended)
            owes = true;
        return new SubscriptionDto(s.Id ?? 0, st?.Value, s.Product?.Handle, s.Product?.Name, !owes, s.BalanceInCents ?? 0,
            s.NextAssessmentAt, s.CurrentPeriodEndsAt, s.CanceledAt);
    }

    public async Task<SubscriptionDto> GetSubscription(string? userName, int id, CancellationToken ct)
    {
        ct = Deadline(ct);
        var user = await CanonicalUser(userName);
        return await ToDto(await Owned(id, user, ct), ct);
    }

    private static bool Is(SubscriptionState? s, params SubscriptionState[] any) => s != null && any.Contains(s);

    public async Task<SubscriptionDto> Pause(string? userName, int id, CancellationToken ct)
    {
        ct = Deadline(ct);
        var user = await CanonicalUser(userName);
        return await Locked("sub:" + id, async () =>
        {
            var sub = await Owned(id, user, ct);
            if (!Is(sub.State, SubscriptionState.Active, SubscriptionState.Trialing))
                throw new MaxioBillingException(409, $"A subscription in state '{sub.State?.Value}' cannot be paused.");
            var r = await Mutate(id, () => _client.SubscriptionStatus.PauseSubscription(new SDK.SubscriptionStatus.PauseSubscriptionRequest { SubscriptionId = id }, null, ct), s => Is(s.State, SubscriptionState.OnHold, SubscriptionState.Paused), ct);
            return await ToDto(r, ct);
        });
    }

    public async Task<SubscriptionDto> Resume(string? userName, int id, CancellationToken ct)
    {
        ct = Deadline(ct);
        var user = await CanonicalUser(userName);
        return await Locked("sub:" + id, async () =>
        {
            var sub = await Owned(id, user, ct);
            if (!Is(sub.State, SubscriptionState.OnHold, SubscriptionState.Paused))
                throw new MaxioBillingException(409, $"A subscription in state '{sub.State?.Value}' is not paused.");
            var r = await Mutate(id, () => _client.SubscriptionStatus.ResumeSubscription(new SDK.SubscriptionStatus.ResumeSubscriptionRequest { SubscriptionId = id }, null, ct), s => !Is(s.State, SubscriptionState.OnHold, SubscriptionState.Paused), ct);
            return await ToDto(r, ct);
        });
    }

    public async Task<SubscriptionDto> Cancel(string? userName, int id, CancellationToken ct)
    {
        ct = Deadline(ct);
        var user = await CanonicalUser(userName);
        return await Locked("sub:" + id, async () =>
        {
            var sub = await Owned(id, user, ct);
            if (Is(sub.State, SubscriptionState.Canceled, SubscriptionState.Expired))
                throw new MaxioBillingException(409, "The subscription is already cancelled.");
            var r = await Mutate(id, () => _client.SubscriptionStatus.CancelSubscription(new SDK.SubscriptionStatus.CancelSubscriptionRequest { SubscriptionId = id }, null, ct), s => Is(s.State, SubscriptionState.Canceled), ct);
            return await ToDto(r, ct);
        });
    }

    public async Task<SubscriptionDto> Restart(string? userName, int id, CancellationToken ct)
    {
        ct = Deadline(ct);
        var user = await CanonicalUser(userName);
        return await Locked("sub:" + id, async () =>
        {
            var sub = await Owned(id, user, ct);
            if (!Is(sub.State, SubscriptionState.Canceled))
                throw new MaxioBillingException(409, "Only a cancelled subscription can be restarted.");
            var r = await Mutate(id, () => _client.SubscriptionStatus.ReactivateSubscription(new SDK.SubscriptionStatus.ReactivateSubscriptionOperationRequest { SubscriptionId = id }, null, ct), s => !Is(s.State, SubscriptionState.Canceled), ct);
            return await ToDto(r, ct);
        });
    }

    /// <summary>Runs a status write; when its outcome is unknown (timeout/connection) re-reads the subscription to settle it.</summary>
    private async Task<Subscription> Mutate(int id, Func<Task<SubscriptionResponse>> write, Func<Subscription, bool> reached, CancellationToken ct)
    {
        try
        {
            return (await Call(write)).Subscription ?? await ReadSub(id, ct);
        }
        catch (MaxioBillingException ex) when (ex.StatusCode is 502 or 504)
        {
            var now = await ReadSub(id, CancellationToken.None);
            if (reached(now)) return now;
            throw;
        }
    }

    public async Task<SubscriptionDto> Switch(string? userName, int id, SwitchRequest req, CancellationToken ct)
    {
        ct = Deadline(ct);
        var user = await CanonicalUser(userName);
        var plan = await OfferedByHandle(req.PlanHandle, ct);
        return await Locked("sub:" + id, async () =>
        {
            var sub = await Owned(id, user, ct);
            if (sub.Product?.Handle == plan.Handle) throw new MaxioBillingException(409, "The subscription is already on this plan.");
            if (Is(sub.State, SubscriptionState.Expired)) throw new MaxioBillingException(409, "An expired subscription cannot be switched.");

            Task<SubscriptionResponse> Write() => Is(sub.State, SubscriptionState.Active, SubscriptionState.Trialing)
                ? _client.SubscriptionProducts.MigrateSubscriptionProduct(new SDK.SubscriptionProducts.MigrateSubscriptionProductRequest
                {
                    SubscriptionId = id,
                    Body = new SubscriptionProductMigrationRequest { Migration = new SubscriptionProductMigration { ProductHandle = plan.Handle } }
                }, null, ct)
                : _client.Subscriptions.UpdateSubscription(new SDK.Subscriptions.UpdateSubscriptionOperationRequest
                {
                    SubscriptionId = id,
                    Body = new UpdateSubscriptionRequest { Subscription = new UpdateSubscription { ProductHandle = plan.Handle } }
                }, null, ct);

            var r = await Mutate(id, Write, s => s.Product?.Handle == plan.Handle, ct);
            // Do not report success unless the subscription is really on the new plan.
            if (r.Product?.Handle != plan.Handle) r = await ReadSub(id, ct);
            if (r.Product?.Handle != plan.Handle)
                throw new MaxioBillingException(409, "The billing system did not move the subscription to the new plan.");
            return await ToDto(r, ct);
        });
    }

    public async Task<SubscriptionDto> SetNextBillingDate(int id, string? date, CancellationToken ct)
    {
        ct = Deadline(ct);
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            throw Bad("The date must be a real calendar date in the form YYYY-MM-DD.");
        return await Locked("sub:" + id, async () =>
        {
            var sub = await ReadSub(id, ct);
            if (!Is(sub.State, SubscriptionState.Active, SubscriptionState.Trialing, SubscriptionState.PastDue, SubscriptionState.SoftFailure))
                throw new MaxioBillingException(409, $"A subscription in state '{sub.State?.Value}' is not billed, so it has no billing date to set.");
            var offset = (sub.NextAssessmentAt ?? sub.CurrentPeriodEndsAt ?? DateTimeOffset.UtcNow).Offset;
            var at = new DateTimeOffset(d.ToDateTime(new TimeOnly(12, 0)), offset);
            if (at <= DateTimeOffset.UtcNow.AddHours(24)) throw Bad("The next billing date must be at least a day in the future.");

            await Mutate(id, () => _client.Subscriptions.UpdateSubscription(new SDK.Subscriptions.UpdateSubscriptionOperationRequest
            {
                SubscriptionId = id,
                Body = new UpdateSubscriptionRequest { Subscription = new UpdateSubscription { NextBillingAt = at } }
            }, null, ct), s => DateOnly.FromDateTime(s.NextAssessmentAt?.DateTime ?? DateTime.MinValue) == d, ct);
            var fresh = await ReadSub(id, ct);
            if (fresh.NextAssessmentAt is { } n && DateOnly.FromDateTime(n.DateTime) != d)
                throw new MaxioBillingException(409, $"The billing system set the next billing date to {n:yyyy-MM-dd}, not {date}.");
            return await ToDto(fresh, ct);
        });
    }

    // ---------------------------------------------------------------- usage

    public async Task<UsageDto> UsageForDay(string? userName, int id, string? date, CancellationToken ct)
    {
        ct = Deadline(ct);
        var user = await CanonicalUser(userName);
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            throw Bad("The date query parameter must be a real calendar date in the form YYYY-MM-DD.");
        await Owned(id, user, ct);

        var comps = await Call(() => _client.SubscriptionComponents.ListSubscriptionComponents(new SDK.SubscriptionComponents.ListSubscriptionComponentsRequest { SubscriptionId = id }, null, ct));
        var metered = comps.Select(c => c.Component).Where(c => c != null && c.ArchivedAt == null && c.ComponentId != null && c.Kind == ComponentKind.MeteredComponent).ToList();

        var since = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var until = since.AddDays(1);
        var result = new List<UsageComponentDto>();
        var truncated = new List<string>();
        var unparseable = new List<string>();
        foreach (var c in metered)
        {
            var compId = c!.ComponentId!.Value;
            var label = c.Name ?? compId.ToString(CultureInfo.InvariantCulture);
            decimal total = 0; var count = 0; var complete = false;
            for (var page = 1; page <= MaxPages; page++)
            {
                var usages = await Call(() => _client.SubscriptionComponents.ListUsages(new SDK.SubscriptionComponents.ListUsagesRequest
                {
                    SubscriptionIdOrReference = id,
                    ComponentId = compId,
                    SinceDate = since,
                    UntilDate = until,
                    Page = page,
                    PerPage = PerPage,
                }, null, ct));
                foreach (var u in usages.Select(x => x.Usage))
                {
                    // Both bounds are inclusive; keep only usage stamped on the chosen day (in the site's own offset).
                    if (u.CreatedAt is not { } at || DateOnly.FromDateTime(at.DateTime) != day) continue;
                    count++;
                    var q = u.Quantity;
                    if (q != null && q.TryGetInt(out var qi)) total += qi;
                    else if (q != null && q.TryGetString(out var qs) && decimal.TryParse(qs, NumberStyles.Float, CultureInfo.InvariantCulture, out var qd)) total += qd;
                    else if (q != null) unparseable.Add($"{label}#{u.Id}");
                }
                if (usages.Count < PerPage) { complete = true; break; }
            }
            if (!complete) truncated.Add(label);
            result.Add(new UsageComponentDto(compId, c.Name, total, count));
        }
        return new UsageDto(id, day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), result, truncated.Count > 0, truncated, unparseable);
    }
}
