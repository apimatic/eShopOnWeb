using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using Sdk = MaxioAdvancedBilling;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// The one place that talks to Maxio (the billing system of record) through the SDK, and that turns
/// every failure into a <see cref="BillingException"/> carrying the 4xx/5xx status to answer with.
/// </summary>
public class MaxioBillingService
{
    public const string BrandFamilyPrefix = "eshop-brand-";
    private const string CustomerReferencePrefix = "eshop:";
    private const int MaxPages = 20;
    private const int PerPage = 200;
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ClaimStaleAfter = TimeSpan.FromMinutes(10);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly CatalogContext _db;
    private readonly IReadRepository<CatalogBrand> _brands;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(
        MaxioAdvancedBillingClient client,
        CatalogContext db,
        IReadRepository<CatalogBrand> brands,
        UserManager<ApplicationUser> users,
        ILogger<MaxioBillingService> logger)
    {
        _client = client;
        _db = db;
        _brands = brands;
        _users = users;
        _logger = logger;
    }

    // ------------------------------------------------------------------ budget / error translation

    /// <summary>One total budget for a whole request, linked to the caller's own cancellation.</summary>
    private async Task<T> Bounded<T>(CancellationToken ct, Func<CancellationToken, Task<T>> body)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Budget);
        try
        {
            return await body(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new BillingException(504, "The billing system did not answer in time.");
        }
    }

    private static BillingException FromStatus(HttpStatusCode code, string? detail)
    {
        var status = (int)code;
        return status switch
        {
            401 or 403 => new BillingException(502, "The billing system is unavailable."),
            429 => new BillingException(503, "The billing system is busy; try again shortly."),
            404 => new BillingException(404, detail ?? "Not found in the billing system."),
            >= 400 and < 500 => new BillingException(status,
                string.IsNullOrWhiteSpace(detail) ? $"The billing system rejected the request (HTTP {status})." : $"The billing system rejected the request: {detail}"),
            _ => new BillingException(502, "The billing system failed to process the request.")
        };
    }

    /// <summary>Case A operations: the typed body is read inside the catch where its type is known.</summary>
    private async Task<T> Run<T, TError>(Func<Task<T>> call, Func<TError, string?> detail) where TError : class
    {
        try
        {
            return await call();
        }
        catch (ApiException<TError> ex)
        {
            throw FromStatus(ex.StatusCode, detail(ex.Error));
        }
        catch (Exception ex) when (ex is SdkException)
        {
            throw Translate(ex);
        }
    }

    /// <summary>Case B operations (raw error) and every non-HTTP failure.</summary>
    private async Task<T> Run<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (Exception ex) when (ex is SdkException)
        {
            throw Translate(ex);
        }
    }

    private BillingException Translate(Exception ex)
    {
        switch (ex)
        {
            case ApiException<RawError> raw:
                return FromStatus(raw.StatusCode, null);
            case ResponseDeserializationException de:
                // 2xx: the call may have worked but we cannot read the result -> unknown outcome.
                if ((int)de.StatusCode < 300)
                {
                    _logger.LogError(de, "Maxio returned an unreadable success body for {Call}", de.RequestUri);
                    return new BillingException(502, "The billing system returned a response that could not be processed.", outcomeUnknown: true, inner: de);
                }
                return FromStatus(de.StatusCode, null);
            case ApiException api:
                return FromStatus(api.StatusCode, null);
            case SdkTimeoutException to:
                _logger.LogError(to, "Maxio timed out");
                return new BillingException(504, "The billing system did not answer in time.", outcomeUnknown: true, inner: to);
            case SdkConnectionException conn:
                _logger.LogError(conn, "Maxio unreachable");
                return new BillingException(502, "The billing system could not be reached.", outcomeUnknown: true, inner: conn);
            case AuthSchemeException auth:
                _logger.LogError(auth, "Maxio credentials are missing or rejected");
                return new BillingException(502, "The billing system is unavailable.", inner: auth);
            default:
                return new BillingException(502, "The billing system failed to process the request.", inner: ex);
        }
    }

    private static string? ListDetail(ErrorListResponse1? list) =>
        list is { Errors.Count: > 0 } ? string.Join("; ", list.Errors) : null;

    private static string? ErrorListDetail(CreateSubscriptionError e) => e.TryGetErrorListResponse1(out var v) ? ListDetail(v) : null;
    private static string? ErrorListDetail(UpdateSubscriptionError e) => e.TryGetErrorListResponse1(out var v) ? ListDetail(v) : null;
    private static string? ErrorListDetail(PauseSubscriptionError e) => e.TryGetErrorListResponse1(out var v) ? ListDetail(v) : null;
    private static string? ErrorListDetail(ResumeSubscriptionError e) => e.TryGetErrorListResponse1(out var v) ? ListDetail(v) : null;
    private static string? ErrorListDetail(ReactivateSubscriptionError e) => e.TryGetErrorListResponse1(out var v) ? ListDetail(v) : null;
    private static string? ErrorListDetail(CreateProductError e) => e.TryGetErrorListResponse1(out var v) ? ListDetail(v) : null;
    private static string? ErrorListDetail(CreateProductFamilyError e) => e.TryGetErrorListResponse1(out var v) ? ListDetail(v) : null;

    private static string? CancelDetail(CancelSubscriptionError e)
    {
        if (e.TryGetCancelSubscriptionErrorResponse(out var body) && body.TryGetErrorListResponse1(out var list))
            return ListDetail(list);
        return null;
    }

    private static string? CustomerDetail(CreateCustomerError e) =>
        e.TryGetCustomerErrorResponse1(out _) ? "the customer details were not accepted" : null;

    private static string? CurrencyDetail(CreateProductCurrencyPricesError e)
    {
        if (e.TryGetErrorArrayMapResponse1(out var v) && v.Errors is { Count: > 0 })
            return string.Join("; ", v.Errors.Select(kv => $"{kv.Key}: {kv.Value}"));
        return null;
    }

    // ------------------------------------------------------------------ claims (duplicate prevention)

    /// <summary>
    /// Claim -> SDK call -> record the result. The claim is a row whose primary key refuses the second
    /// caller; it is taken before Maxio is called and released once the outcome is settled.
    /// </summary>
    private async Task<T> WithClaim<T>(string key, Func<Task<T>> operation)
    {
        if (!await TryClaim(key))
            throw new BillingException(409, "An identical request is already being processed.");
        try
        {
            var result = await operation();
            await Release(key);
            return result;
        }
        catch (BillingException ex) when (ex.OutcomeUnknown)
        {
            // keep the claim, marked unknown: it expires, so one transient failure is not a permanent refusal.
            await MarkUnknown(key);
            throw;
        }
        catch
        {
            await Release(key);
            throw;
        }
    }

    private async Task<bool> TryClaim(string key)
    {
        var stale = DateTime.UtcNow - ClaimStaleAfter;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var claim = new MaxioOperationClaim { Key = key, ClaimedAtUtc = DateTime.UtcNow };
            try
            {
                _db.MaxioOperationClaims.Add(claim);
                await _db.SaveChangesAsync(CancellationToken.None);
                return true;
            }
            catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException or ArgumentException)
            {
                // The primary key refused the second claim.
                _db.Entry(claim).State = EntityState.Detached;
                if (attempt == 1) return false;
                // A claim left behind by a crashed or unknown outcome expires.
                var removed = await ExpireStale(key, stale);
                if (!removed) return false;
            }
        }
        return false;
    }

    private async Task<bool> ExpireStale(string key, DateTime olderThanUtc)
    {
        try
        {
            var existing = await _db.MaxioOperationClaims.AsNoTracking().FirstOrDefaultAsync(c => c.Key == key);
            if (existing is null) return true; // released in between: try again
            if (existing.ClaimedAtUtc >= olderThanUtc) return false;
            await _db.MaxioOperationClaims.Where(c => c.Key == key && c.ClaimedAtUtc < olderThanUtc)
                .ExecuteDeleteAsync();
            return true;
        }
        catch (InvalidOperationException)
        {
            // provider without bulk delete (in-memory): fall back to a tracked delete
            var existing = await _db.MaxioOperationClaims.FirstOrDefaultAsync(c => c.Key == key);
            if (existing is null) return true;
            if (existing.ClaimedAtUtc >= olderThanUtc) return false;
            _db.MaxioOperationClaims.Remove(existing);
            await _db.SaveChangesAsync(CancellationToken.None);
            return true;
        }
    }

    private async Task Release(string key)
    {
        var existing = await _db.MaxioOperationClaims.FirstOrDefaultAsync(c => c.Key == key);
        if (existing is null) return;
        _db.MaxioOperationClaims.Remove(existing);
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task MarkUnknown(string key)
    {
        var existing = await _db.MaxioOperationClaims.FirstOrDefaultAsync(c => c.Key == key);
        if (existing is null) return;
        existing.Result = "unknown";
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>
    /// A write whose connection failed after Maxio may have acted: settle the outcome by re-reading before answering.
    /// </summary>
    private async Task<T> Write<T>(Func<Task<T>> write, Func<CancellationToken, Task<T?>> reconcile) where T : class
    {
        BillingException? unknown = null;
        try
        {
            return await write();
        }
        catch (BillingException ex) when (ex.OutcomeUnknown)
        {
            unknown = ex;
        }
        catch (OperationCanceledException)
        {
            unknown = new BillingException(504, "The billing system did not answer in time.", outcomeUnknown: true);
        }

        T? found = null;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            found = await reconcile(cts.Token);
        }
        catch (Exception ex) when (ex is BillingException or OperationCanceledException)
        {
            // still unknown
        }
        if (found is not null) return found;
        throw new BillingException(unknown.Status,
            "The outcome of the request could not be confirmed with the billing system. Check the subscription before trying again.",
            outcomeUnknown: true, inner: unknown);
    }

    // ------------------------------------------------------------------ identity / customer

    private static string CustomerReference(string userName) => CustomerReferencePrefix + userName.Trim().ToLowerInvariant();

    private static string Hash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).Substring(0, 20).ToLowerInvariant();
    }

    private async Task<Customer?> FindCustomer(string reference, CancellationToken tok)
    {
        try
        {
            var response = await Run(() => _client.Customers.ReadCustomerByReference(
                new Sdk.Requests.Customers.ReadCustomerByReferenceRequest { Reference = reference }, cancellationToken: tok));
            return response.Customer;
        }
        catch (BillingException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    private async Task<Customer> EnsureCustomer(string userName, CancellationToken tok)
    {
        var reference = CustomerReference(userName);
        var existing = await FindCustomer(reference, tok);
        if (existing is not null) return existing;

        var user = await _users.FindByNameAsync(userName);
        var email = !string.IsNullOrWhiteSpace(user?.Email) ? user!.Email! : userName;
        if (!email.Contains('@'))
            throw new BillingException(400, "The signed-in user has no email address, which the billing system requires.");
        var local = email.Split('@')[0];

        var claimKey = "customer:" + reference;
        if (!await TryClaim(claimKey))
        {
            // another request is creating the same customer: wait briefly for it to appear
            for (var i = 0; i < 6; i++)
            {
                await Task.Delay(500, tok);
                var created = await FindCustomer(reference, tok);
                if (created is not null) return created;
            }
            throw new BillingException(409, "Your billing profile is still being set up; try again shortly.");
        }

        try
        {
            var customer = await Write(
                async () => (await Run<CustomerResponse, CreateCustomerError>(() => _client.Customers.CreateCustomer(
                    new Sdk.Requests.Customers.CreateCustomerOperationRequest
                    {
                        Body = new CreateCustomerRequest
                        {
                            Customer = new CreateCustomer
                            {
                                FirstName = local,
                                LastName = "eShop",
                                Email = email,
                                Reference = reference
                            }
                        }
                    }, cancellationToken: tok), CustomerDetail)),
                async t => new CustomerResponse { Customer = await FindCustomer(reference, t) ?? throw new BillingException(404, "none") });
            await Release(claimKey);
            return customer.Customer ?? throw new BillingException(502, "The billing system returned no customer.");
        }
        catch (BillingException ex) when (ex.OutcomeUnknown)
        {
            await MarkUnknown(claimKey);
            throw;
        }
        catch
        {
            await Release(claimKey);
            throw;
        }
    }

    private async Task<Subscription> ReadSubscription(int id, CancellationToken tok)
    {
        try
        {
            var response = await Run(() => _client.Subscriptions.ReadSubscription(
                new Sdk.Requests.Subscriptions.ReadSubscriptionRequest { SubscriptionId = id }, cancellationToken: tok));
            return response.Subscription ?? throw new BillingException(502, "The billing system returned no subscription.");
        }
        catch (BillingException ex) when (ex.Status == 404)
        {
            throw new BillingException(404, "Subscription not found.");
        }
    }

    /// <summary>A subscription is the caller's only if Maxio says its customer carries the caller's reference.</summary>
    private async Task<Subscription> ReadOwned(string userName, int id, CancellationToken tok)
    {
        var sub = await ReadSubscription(id, tok);
        if (!string.Equals(sub.Customer?.Reference, CustomerReference(userName), StringComparison.Ordinal))
            throw new BillingException(404, "Subscription not found.");
        return sub;
    }

    private static SubscriptionDto ToDto(Subscription s)
    {
        if (s.BalanceInCents is null)
            throw new BillingException(502, "The billing system did not report the subscription balance.");
        return new SubscriptionDto
        {
            Id = s.Id ?? 0,
            State = s.State?.Value ?? "unknown",
            PlanId = s.Product?.Id,
            PlanHandle = s.Product?.Handle,
            PlanName = s.Product?.Name,
            BalanceInCents = s.BalanceInCents.Value,
            Paid = s.BalanceInCents.Value <= 0,
            NextBillingAt = s.NextAssessmentAt ?? s.CurrentPeriodEndsAt,
            CancelAtEndOfPeriod = s.CancelAtEndOfPeriod ?? false
        };
    }

    private static bool StateIs(Subscription s, params SubscriptionState[] states) =>
        s.State is not null && states.Any(x => x == s.State);

    private static BillingException WrongState(Subscription s, string action) =>
        new(409, $"Cannot {action} a subscription that is {s.State?.Value ?? "in an unknown state"}.");

    // ------------------------------------------------------------------ plans

    private sealed record OfferedPlan(Product Product, int BrandId, string Brand, int FamilyId);

    private static string PlanFamilyHandle(int brandId) => BrandFamilyPrefix + brandId.ToString(CultureInfo.InvariantCulture);

    private static bool TryBrandId(string? familyHandle, out int id)
    {
        id = 0;
        return familyHandle is not null
               && familyHandle.StartsWith(BrandFamilyPrefix, StringComparison.Ordinal)
               && int.TryParse(familyHandle.AsSpan(BrandFamilyPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out id);
    }

    private async Task<List<ProductFamily>> BrandFamilies(IReadOnlyList<CatalogBrand> brands, CancellationToken tok)
    {
        var families = await Run(() => _client.ProductFamilies.ListProductFamilies(
            new Sdk.Requests.ProductFamilies.ListProductFamiliesRequest(), cancellationToken: tok));
        return families
            .Select(f => f.ProductFamily)
            .Where(f => f is { Id: not null } && f.ArchivedAt is null && TryBrandId(f.Handle, out var id) && brands.Any(b => b.Id == id))
            .Select(f => f!)
            .ToList();
    }

    /// <summary>
    /// The plans this shop offers: non-archived products in the family of a brand that is still in the catalog.
    /// </summary>
    private async Task<(List<OfferedPlan> Plans, bool Truncated)> OfferedPlans(CancellationToken tok)
    {
        var brands = await _brands.ListAsync(tok);
        var families = await BrandFamilies(brands, tok);
        var plans = new List<OfferedPlan>();
        var truncated = false;
        foreach (var family in families)
        {
            TryBrandId(family.Handle, out var brandId);
            var brand = brands.First(b => b.Id == brandId).Brand;
            for (var page = 1; ; page++)
            {
                if (page > MaxPages) { truncated = true; break; }
                var items = await Run<IReadOnlyList<ProductResponse>, ListProductsForProductFamilyError>(
                    () => _client.ProductFamilies.ListProductsForProductFamily(
                        new Sdk.Requests.ProductFamilies.ListProductsForProductFamilyRequest
                        {
                            ProductFamilyId = family.Id!.Value.ToString(CultureInfo.InvariantCulture),
                            Page = page,
                            PerPage = PerPage
                        }, cancellationToken: tok),
                    e => e.TryGetString(out var s) ? s : null);
                plans.AddRange(items.Select(i => i.Product).Where(p => p is { Id: not null, Handle: not null } && p.ArchivedAt is null)
                    .Select(p => new OfferedPlan(p!, brandId, brand, family.Id!.Value)));
                if (items.Count < PerPage) break;
            }
        }
        return (plans, truncated);
    }

    private static PlanDto ToDto(OfferedPlan p) => new()
    {
        Id = p.Product.Id!.Value,
        Handle = p.Product.Handle!,
        Name = p.Product.Name ?? p.Product.Handle!,
        PriceInCents = p.Product.PriceInCents ?? 0,
        MonthlyPrice = (p.Product.PriceInCents ?? 0) / 100m,
        BrandId = p.BrandId,
        Brand = p.Brand
    };

    private async Task<OfferedPlan> RequireOffered(string? handle, CancellationToken tok)
    {
        if (string.IsNullOrWhiteSpace(handle))
            throw new BillingException(400, "planHandle is required.");
        var (plans, truncated) = await OfferedPlans(tok);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Product.Handle, handle.Trim(), StringComparison.Ordinal));
        if (plan is not null) return plan;
        if (truncated)
            throw new BillingException(503, "The plan list could not be read completely; try again shortly.");
        throw new BillingException(404, $"'{handle}' is not a plan that can be subscribed to.");
    }

    public Task<ListPlansResponse> ListPlans(CancellationToken ct) => Bounded(ct, async tok =>
    {
        var (plans, truncated) = await OfferedPlans(tok);
        return new ListPlansResponse { Plans = plans.Select(ToDto).ToList(), Truncated = truncated };
    });

    // ------------------------------------------------------------------ shopper features

    public Task<SubscriptionDto> Subscribe(string userName, string? planHandle, CancellationToken ct) => Bounded(ct, async tok =>
    {
        var plan = await RequireOffered(planHandle, tok);
        var customer = await EnsureCustomer(userName, tok);
        var customerId = customer.Id ?? throw new BillingException(502, "The billing system returned no customer id.");

        var existing = await Run(() => _client.Customers.ListCustomerSubscriptions(
            new Sdk.Requests.Customers.ListCustomerSubscriptionsRequest { CustomerId = customerId }, cancellationToken: tok));
        var mine = existing.Select(e => e.Subscription).Where(s => s is not null).Select(s => s!).ToList();
        var live = mine.FirstOrDefault(s => s.Product?.Handle == plan.Product.Handle
                                            && !StateIs(s, SubscriptionState.Canceled, SubscriptionState.Expired, SubscriptionState.FailedToCreate));
        if (live is not null)
            throw new BillingException(409, "You already have a subscription to this plan.");

        // the plan's metered components, so usage recorded elsewhere has something to land on
        var components = new List<CreateSubscriptionComponent>();
        for (var page = 1; ; page++)
        {
            if (page > MaxPages)
                throw new BillingException(503, "The components of the plan could not be read completely; try again shortly.");
            var batch = await Run(() => _client.Components.ListComponentsForProductFamily(
                new Sdk.Requests.Components.ListComponentsForProductFamilyRequest
                {
                    ProductFamilyId = plan.FamilyId,
                    Page = page,
                    PerPage = PerPage
                }, cancellationToken: tok));
            foreach (var c in batch.Select(b => b.Component))
                if (c?.Id is not null && c.Kind == ComponentKind.MeteredComponent && c.Archived != true)
                    components.Add(new CreateSubscriptionComponent { ComponentId = c.Id.Value });
            if (batch.Count < PerPage) break;
        }

        // deterministic per (shopper, plan, how many subscriptions they already had to it): reconcilable, and a
        // later re-subscription gets a new reference
        var priorToPlan = mine.Count(s => s.Product?.Handle == plan.Product.Handle);
        var reference = "eshop-sub-" + Hash($"{CustomerReference(userName)}|{plan.Product.Handle}|{priorToPlan}");

        var created = await WithClaim($"subscribe:{reference}", () => Write(
            () => Create(CollectionMethod.Remittance),
            async t =>
            {
                try
                {
                    var found = await Run<SubscriptionResponse, FindSubscriptionError>(
                        () => _client.Subscriptions.FindSubscription(
                            new Sdk.Requests.Subscriptions.FindSubscriptionRequest { Reference = reference }, cancellationToken: t),
                        _ => null);
                    return found;
                }
                catch (BillingException ex) when (ex.Status == 404) { return null; }
            }));

        async Task<SubscriptionResponse> Create(CollectionMethod method)
        {
            try
            {
                return await Run<SubscriptionResponse, CreateSubscriptionError>(
                    () => _client.Subscriptions.CreateSubscription(
                        new Sdk.Requests.Subscriptions.CreateSubscriptionOperationRequest
                        {
                            Body = new CreateSubscriptionRequest
                            {
                                Subscription = new CreateSubscription
                                {
                                    ProductHandle = plan.Product.Handle,
                                    CustomerId = customerId,
                                    PaymentCollectionMethod = method,
                                    Reference = reference,
                                    Components = components.Count > 0 ? components : null
                                }
                            }
                        }, cancellationToken: tok),
                    ErrorListDetail);
            }
            catch (BillingException ex) when (ex.Status == 422 && method == CollectionMethod.Remittance)
            {
                // a site on the legacy invoicing architecture rejects remittance; invoice is its equivalent
                _logger.LogWarning("Maxio rejected remittance collection ({Reason}); retrying once with invoice collection", ex.Message);
                return await Create(CollectionMethod.Invoice);
            }
        }

        _logger.LogInformation("Subscribed {User} to {Plan} as Maxio subscription {Id}", userName, plan.Product.Handle, created.Subscription?.Id);
        return ToDto(created.Subscription ?? throw new BillingException(502, "The billing system returned no subscription."));
    });

    public Task<SubscriptionDto> GetSubscription(string userName, int id, CancellationToken ct) => Bounded(ct, async tok =>
        ToDto(await ReadOwned(userName, id, tok)));

    public Task<SubscriptionDto> Pause(string userName, int id, CancellationToken ct) => Bounded(ct, async tok =>
    {
        var sub = await ReadOwned(userName, id, tok);
        if (!StateIs(sub, SubscriptionState.Active, SubscriptionState.Trialing))
            throw WrongState(sub, "pause");
        if (sub.NextAssessmentAt is { } next && next < DateTimeOffset.UtcNow.AddHours(24))
            throw new BillingException(409, "A subscription cannot be paused within 24 hours of its next billing date.");

        var result = await Write(
            async () => (await Run<SubscriptionResponse, PauseSubscriptionError>(() => _client.SubscriptionStatus.PauseSubscription(
                new Sdk.Requests.SubscriptionStatus.PauseSubscriptionRequest { SubscriptionId = id }, cancellationToken: tok), ErrorListDetail)).Subscription,
            async t => { var s = await ReadSubscription(id, t); return StateIs(s, SubscriptionState.OnHold) ? s : null; });
        return ToDto(result ?? throw new BillingException(502, "The billing system returned no subscription."));
    });

    public Task<SubscriptionDto> Resume(string userName, int id, CancellationToken ct) => Bounded(ct, async tok =>
    {
        var sub = await ReadOwned(userName, id, tok);
        if (!StateIs(sub, SubscriptionState.OnHold))
            throw WrongState(sub, "resume");

        var result = await Write(
            async () => (await Run<SubscriptionResponse, ResumeSubscriptionError>(() => _client.SubscriptionStatus.ResumeSubscription(
                new Sdk.Requests.SubscriptionStatus.ResumeSubscriptionRequest { SubscriptionId = id }, cancellationToken: tok), ErrorListDetail)).Subscription,
            async t => { var s = await ReadSubscription(id, t); return StateIs(s, SubscriptionState.OnHold) ? null : s; });
        return ToDto(result ?? throw new BillingException(502, "The billing system returned no subscription."));
    });

    public Task<SubscriptionDto> Cancel(string userName, int id, CancellationToken ct) => Bounded(ct, async tok =>
    {
        var sub = await ReadOwned(userName, id, tok);
        if (!StateIs(sub, SubscriptionState.Active, SubscriptionState.Trialing, SubscriptionState.PastDue, SubscriptionState.SoftFailure,
                SubscriptionState.Unpaid, SubscriptionState.OnHold, SubscriptionState.TrialEnded))
            throw WrongState(sub, "cancel");

        var result = await Write(
            async () => (await Run<SubscriptionResponse, CancelSubscriptionError>(() => _client.SubscriptionStatus.CancelSubscription(
                new Sdk.Requests.SubscriptionStatus.CancelSubscriptionRequest
                {
                    SubscriptionId = id,
                    Body = new CancellationRequest { Subscription = new CancellationOptions() }
                }, cancellationToken: tok), CancelDetail)).Subscription,
            async t => { var s = await ReadSubscription(id, t); return StateIs(s, SubscriptionState.Canceled) ? s : null; });
        return ToDto(result ?? throw new BillingException(502, "The billing system returned no subscription."));
    });

    public Task<SubscriptionDto> Restart(string userName, int id, CancellationToken ct) => Bounded(ct, async tok =>
    {
        var sub = await ReadOwned(userName, id, tok);
        if (!StateIs(sub, SubscriptionState.Canceled))
            throw WrongState(sub, "restart");

        var result = await WithClaim($"restart:{id}", () => Write(
            async () => (await Run<SubscriptionResponse, ReactivateSubscriptionError>(() => _client.SubscriptionStatus.ReactivateSubscription(
                new Sdk.Requests.SubscriptionStatus.ReactivateSubscriptionOperationRequest
                {
                    SubscriptionId = id,
                    Body = new ReactivateSubscriptionRequest()
                }, cancellationToken: tok), ErrorListDetail)).Subscription,
            async t => { var s = await ReadSubscription(id, t); return StateIs(s, SubscriptionState.Canceled) ? null : s; }));
        return ToDto(result ?? throw new BillingException(502, "The billing system returned no subscription."));
    });

    public Task<SubscriptionDto> Switch(string userName, int id, string? planHandle, CancellationToken ct) => Bounded(ct, async tok =>
    {
        var sub = await ReadOwned(userName, id, tok);
        if (StateIs(sub, SubscriptionState.Pending, SubscriptionState.Assessing, SubscriptionState.FailedToCreate,
                SubscriptionState.AwaitingSignup, SubscriptionState.Expired) || sub.State is null)
            throw WrongState(sub, "switch");
        var plan = await RequireOffered(planHandle, tok);
        if (plan.Product.Handle == sub.Product?.Handle)
            throw new BillingException(409, "The subscription is already on this plan.");

        var handle = plan.Product.Handle!;
        var result = await WithClaim($"switch:{id}:{handle}", () => Write(
            async () =>
            {
                var response = await Run<SubscriptionResponse, UpdateSubscriptionError>(() => _client.Subscriptions.UpdateSubscription(
                    new Sdk.Requests.Subscriptions.UpdateSubscriptionOperationRequest
                    {
                        SubscriptionId = id,
                        Body = new UpdateSubscriptionRequest { Subscription = new UpdateSubscription { ProductHandle = handle } }
                    }, cancellationToken: tok), ErrorListDetail);
                return response.Subscription;
            },
            async t => { var s = await ReadSubscription(id, t); return s.Product?.Handle == handle ? s : null; }));

        if (result?.Product?.Handle != handle)
            throw new BillingException(502, "The billing system did not move the subscription to the new plan.");
        return ToDto(result);
    });

    public Task<UsageResponse> Usage(string userName, int id, DateOnly date, CancellationToken ct) => Bounded(ct, async tok =>
    {
        await ReadOwned(userName, id, tok);
        var comps = await Run(() => _client.SubscriptionComponents.ListSubscriptionComponents(
            new Sdk.Requests.SubscriptionComponents.ListSubscriptionComponentsRequest { SubscriptionId = id }, cancellationToken: tok));
        var response = new UsageResponse { SubscriptionId = id, Date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };

        // the API's window is [since, until] with until inclusive of midnight, so a single day cannot be asked
        // for exactly: widen by a day each side and keep the usages created on the requested calendar day
        var since = new DateTimeOffset(date.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var until = new DateTimeOffset(date.AddDays(2).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        foreach (var c in comps.Select(x => x.Component).Where(x => x?.ComponentId is not null && x.Kind == ComponentKind.MeteredComponent))
        {
            var entry = new UsageComponentDto { ComponentId = c!.ComponentId!.Value, Handle = c.ComponentHandle, Name = c.Name, UnitName = c.UnitName };
            for (var page = 1; ; page++)
            {
                if (page > MaxPages) { entry.Truncated = true; response.Truncated = true; break; }
                var usages = await Run(() => _client.SubscriptionComponents.ListUsages(
                    new Sdk.Requests.SubscriptionComponents.ListUsagesRequest
                    {
                        SubscriptionIdOrReference = id,
                        ComponentId = c.ComponentId.Value,
                        SinceDate = since,
                        UntilDate = until,
                        Page = page,
                        PerPage = PerPage
                    }, cancellationToken: tok));
                foreach (var u in usages.Select(x => x.Usage))
                {
                    if (u?.CreatedAt is null || DateOnly.FromDateTime(u.CreatedAt.Value.DateTime) != date) continue;
                    entry.Quantity += ReadQuantity(u.Quantity);
                }
                if (usages.Count < PerPage) break;
            }
            response.Components.Add(entry);
        }
        return response;
    });

    private static decimal ReadQuantity(Sdk.Models.AnyOf.Quantity1? q)
    {
        if (q is null) return 0m;
        if (q.TryGetInt(out var i)) return i;
        if (q.TryGetString(out var s) && decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)) return d;
        throw new BillingException(502, "The billing system reported a usage quantity that could not be read.");
    }

    // ------------------------------------------------------------------ staff features

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

    public Task<PlanDto> CreatePlan(string? name, decimal? monthlyPrice, string? brand, CancellationToken ct) => Bounded(ct, async tok =>
    {
        if (string.IsNullOrWhiteSpace(name)) throw new BillingException(400, "name is required.");
        if (monthlyPrice is null or <= 0) throw new BillingException(400, "monthlyPrice must be greater than zero.");
        if (decimal.Round(monthlyPrice.Value, 2) != monthlyPrice.Value) throw new BillingException(400, "monthlyPrice can have at most two decimals.");
        if (string.IsNullOrWhiteSpace(brand)) throw new BillingException(400, "brand is required.");

        // the brand must be one of the catalog of the shop
        var brands = await _brands.ListAsync(tok);
        var catalogBrand = brands.FirstOrDefault(b => string.Equals(b.Brand, brand.Trim(), StringComparison.OrdinalIgnoreCase))
                           ?? (int.TryParse(brand, NumberStyles.None, CultureInfo.InvariantCulture, out var bid) ? brands.FirstOrDefault(b => b.Id == bid) : null);
        if (catalogBrand is null)
            throw new BillingException(400, $"'{brand}' is not a brand of the catalog. Known brands: {string.Join(", ", brands.Select(b => b.Brand))}.");

        var handle = Slug(name);
        if (handle.Length == 0) throw new BillingException(400, "name must contain letters or digits.");

        // the plan handle must be new on the site
        try
        {
            await Run(() => _client.Products.ReadProductByHandle(
                new Sdk.Requests.Products.ReadProductByHandleRequest { ApiHandle = handle }, cancellationToken: tok));
            throw new BillingException(409, $"A plan with handle '{handle}' already exists.");
        }
        catch (BillingException ex) when (ex.Status == 404) { /* free */ }

        var familyId = await EnsureBrandFamily(catalogBrand, tok);
        var product = await WithClaim($"plan:{handle}", () => Write(
            () => Run<ProductResponse, CreateProductError>(() => _client.Products.CreateProduct(
                new Sdk.Requests.Products.CreateProductRequest
                {
                    ProductFamilyId = familyId.ToString(CultureInfo.InvariantCulture),
                    Body = new CreateOrUpdateProductRequest
                    {
                        Product = new CreateOrUpdateProduct
                        {
                            Name = name.Trim(),
                            Handle = handle,
                            Description = name.Trim(),
                            PriceInCents = (long)(monthlyPrice.Value * 100m),
                            Interval = 1,
                            IntervalUnit = IntervalUnit.Month,
                            RequireCreditCard = false
                        }
                    }
                }, cancellationToken: tok), ErrorListDetail),
            async t =>
            {
                try
                {
                    return await Run(() => _client.Products.ReadProductByHandle(
                        new Sdk.Requests.Products.ReadProductByHandleRequest { ApiHandle = handle }, cancellationToken: t));
                }
                catch (BillingException ex) when (ex.Status == 404) { return null; }
            }));

        var p = product.Product ?? throw new BillingException(502, "The billing system returned no plan.");
        return ToDto(new OfferedPlan(p, catalogBrand.Id, catalogBrand.Brand, familyId));
    });

    private async Task<int> EnsureBrandFamily(CatalogBrand brand, CancellationToken tok)
    {
        var handle = PlanFamilyHandle(brand.Id);
        async Task<int?> Find(CancellationToken t)
        {
            var all = await Run(() => _client.ProductFamilies.ListProductFamilies(
                new Sdk.Requests.ProductFamilies.ListProductFamiliesRequest(), cancellationToken: t));
            return all.Select(f => f.ProductFamily).FirstOrDefault(f => f?.Handle == handle)?.Id;
        }

        var existing = await Find(tok);
        if (existing is not null) return existing.Value;

        var created = await WithClaim($"family:{handle}", () => Write(
            () => Run<ProductFamilyResponse, CreateProductFamilyError>(() => _client.ProductFamilies.CreateProductFamily(
                new Sdk.Requests.ProductFamilies.CreateProductFamilyOperationRequest
                {
                    Body = new CreateProductFamilyRequest
                    {
                        ProductFamily = new CreateProductFamily { Name = brand.Brand, Handle = handle }
                    }
                }, cancellationToken: tok), ErrorListDetail),
            async t =>
            {
                var id = await Find(t);
                return id is null ? null : new ProductFamilyResponse { ProductFamily = new ProductFamily { Id = id, Handle = handle } };
            }));
        return created.ProductFamily?.Id ?? throw new BillingException(502, "The billing system returned no product family.");
    }

    public Task<CurrencyPriceDto> AddCurrencyPrice(int planId, string? currency, decimal? price, CancellationToken ct) => Bounded(ct, async tok =>
    {
        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3 || !currency.Trim().All(char.IsLetter))
            throw new BillingException(400, "currency must be a three-letter ISO code.");
        if (price is null or <= 0) throw new BillingException(400, "price must be greater than zero.");
        if (decimal.Truncate(price.Value) != price.Value)
            throw new BillingException(400, "price must be a whole number: the billing system takes whole currency units.");
        var code = currency.Trim().ToUpperInvariant();

        var brands = await _brands.ListAsync(tok);
        Product? product;
        try
        {
            product = (await Run(() => _client.Products.ReadProduct(
                new Sdk.Requests.Products.ReadProductRequest { ProductId = planId }, cancellationToken: tok))).Product;
        }
        catch (BillingException ex) when (ex.Status == 404)
        {
            throw new BillingException(404, "Plan not found.");
        }
        if (product is null || product.ArchivedAt is not null
            || !TryBrandId(product.ProductFamily?.Handle, out var brandId) || brands.All(b => b.Id != brandId))
            throw new BillingException(404, "Plan not found.");
        var pricePointId = product.DefaultProductPricePointId
                           ?? throw new BillingException(409, "The plan has no default price point to price in another currency.");

        var site = (await Run(() => _client.Sites.ReadSite(cancellationToken: tok))).Site;
        var allowed = site.NonPrimaryCurrencies ?? new List<string>();
        if (!allowed.Contains(code, StringComparer.OrdinalIgnoreCase))
            throw new BillingException(400, $"'{code}' is not an additional currency of the site. Enabled: {(allowed.Count == 0 ? "none" : string.Join(", ", allowed))}.");

        var response = await WithClaim($"currency:{planId}:{code}", () => Write(
            () => Run<CurrencyPricesResponse, CreateProductCurrencyPricesError>(() => _client.ProductPricePoints.CreateProductCurrencyPrices(
                new Sdk.Requests.ProductPricePoints.CreateProductCurrencyPricesOperationRequest
                {
                    ProductPricePointId = pricePointId,
                    Body = new CreateProductCurrencyPricesRequest
                    {
                        CurrencyPrices = new List<CreateProductCurrencyPrice>
                        {
                            new() { Currency = code, Price = (int)price.Value, Role = CurrencyPriceRole.Baseline }
                        }
                    }
                }, cancellationToken: tok), CurrencyDetail),
            async t =>
            {
                var read = await Run(() => _client.ProductPricePoints.ReadProductPricePoint(
                    new Sdk.Requests.ProductPricePoints.ReadProductPricePointRequest
                    {
                        ProductId = planId,
                        PricePointId = pricePointId,
                        CurrencyPrices = true
                    }, cancellationToken: t));
                var hit = read.PricePoint?.CurrencyPrices?.FirstOrDefault(c => string.Equals(c.Currency, code, StringComparison.OrdinalIgnoreCase));
                return hit is null ? null : new CurrencyPricesResponse { CurrencyPrices = new List<CurrencyPrice> { hit } };
            }));

        var created = response.CurrencyPrices?.FirstOrDefault(c => string.Equals(c.Currency, code, StringComparison.OrdinalIgnoreCase))
                      ?? throw new BillingException(502, "The billing system did not confirm the currency price.");
        return new CurrencyPriceDto { PlanId = planId, Currency = code, Price = (decimal)(created.Price ?? (double)price.Value) };
    });

    public Task<SubscriptionDto> SetNextBillingDate(int id, DateOnly date, CancellationToken ct) => Bounded(ct, async tok =>
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (date <= today) throw new BillingException(400, "The next billing date must be in the future.");

        var sub = await ReadSubscription(id, tok);
        if (!StateIs(sub, SubscriptionState.Active, SubscriptionState.Trialing))
            throw new BillingException(409, $"A subscription that is {sub.State?.Value ?? "in an unknown state"} is not billed, so it has no next billing date to set.");

        // noon UTC keeps the calendar date in any site time zone
        var at = new DateTimeOffset(date.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);

        static bool Matches(Subscription s, DateOnly d) =>
            (s.CurrentPeriodEndsAt is { } p && DateOnly.FromDateTime(p.UtcDateTime) == d)
            || (s.NextAssessmentAt is { } n && DateOnly.FromDateTime(n.UtcDateTime) == d);

        await Write(
            async () => (await Run<SubscriptionResponse, UpdateSubscriptionError>(() => _client.Subscriptions.UpdateSubscription(
                new Sdk.Requests.Subscriptions.UpdateSubscriptionOperationRequest
                {
                    SubscriptionId = id,
                    Body = new UpdateSubscriptionRequest { Subscription = new UpdateSubscription { NextBillingAt = at } }
                }, cancellationToken: tok), ErrorListDetail)).Subscription,
            async t => { var s = await ReadSubscription(id, t); return Matches(s, date) ? s : null; });

        // the update response does not carry the date: confirm it from a fresh read
        var after = await ReadSubscription(id, tok);
        if (!Matches(after, date))
            throw new BillingException(502, "The billing system did not apply the next billing date.");
        _logger.LogInformation("Next billing date of Maxio subscription {Id} set to {Date}", id, date);
        return ToDto(after);
    });
}
