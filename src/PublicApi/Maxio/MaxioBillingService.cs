using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
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
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using SdkProduct = MaxioAdvancedBilling.Models.Product;
using SdkSubscription = MaxioAdvancedBilling.Models.Subscription;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Everything the PublicApi does against Maxio. Every Maxio refusal surfaces as a <see cref="BillingException"/>
/// carrying a 4xx/5xx status and a reason.
/// </summary>
public partial class MaxioBillingService
{
    private const string BrandFamilyPrefix = "eshop-brand-";
    private const int MaxProductPages = 25;
    private const int PerPage = 200;
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan ClaimExpiry = TimeSpan.FromMinutes(5);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly CatalogContext _db;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(MaxioAdvancedBillingClient client, CatalogContext db, ILogger<MaxioBillingService> logger)
    {
        _client = client;
        _db = db;
        _logger = logger;
    }

    // ------------------------------------------------------------------ plumbing

    /// <summary>One total budget for a whole operation, linked to the caller's own cancellation.</summary>
    public async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        try
        {
            return await work(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new BillingException(504, "Maxio did not answer within the time budget.");
        }
    }

    private static BillingException Translate(ApiException ex, string? detail)
    {
        var status = (int)ex.StatusCode;
        var reason = string.IsNullOrWhiteSpace(detail) ? $"Maxio rejected the request (HTTP {status})." : $"Maxio: {detail}";
        return status switch
        {
            404 => new BillingException(404, reason, ex),
            409 => new BillingException(409, reason, ex),
            400 or 422 => new BillingException(422, reason, ex),
            401 or 403 => new BillingException(502, "Billing provider unavailable.", ex),   // our credentials, not the caller's fault
            429 => new BillingException(503, "Billing provider is busy, try again later.", ex),
            >= 400 and < 500 => new BillingException(422, reason, ex),
            _ => new BillingException(502, "Billing provider failed.", ex),
        };
    }

    private BillingException Fail(Exception ex)
    {
        _logger.LogWarning(ex, "Maxio call failed");
        return ex switch
        {
            BillingException b => b,
            ResponseDeserializationException r => new BillingException(502, "Billing provider returned a response that could not be processed.", r),
            ApiException a => Translate(a, null),
            SdkTimeoutException t => new BillingException(504, "Billing provider timed out.", t),
            SdkConnectionException c => new BillingException(502, "Billing provider unreachable.", c),
            AuthSchemeException a => new BillingException(502, "Billing provider credentials missing or rejected.", a),
            _ => new BillingException(500, "Unexpected billing error."),
        };
    }

    private static string? ErrList(ErrorListResponse1? e) => e == null ? null : string.Join("; ", e.Errors);

    private static string? Raw(RawError r)
    {
        try { return r.ReadAsString(); } catch { return null; }
    }

    /// <summary>Calls Maxio, translating every SDK failure. <paramref name="detail"/> reads the typed error body.</summary>
    private async Task<T> Call<T, TErr>(Func<Task<T>> call, Func<TErr, string?> detail) where TErr : ApiError
    {
        try { return await call(); }
        catch (ApiException<TErr> ex) { throw Translate(ex, detail(ex.Error)); }
        catch (ApiException<RawError> ex) { throw Translate(ex, Raw(ex.Error)); }
        catch (ResponseDeserializationException ex) { throw Fail(ex); }
        catch (ApiException ex) { throw Translate(ex, null); }
        catch (BillingException) { throw; }
        catch (SdkException ex) { throw Fail(ex); }
    }

    /// <summary>Call for operations whose error is a typed ErrorListResponse1 on 422 (all of them expose TryGetErrorListResponse1).</summary>
    private Task<T> CallRaw<T>(Func<Task<T>> call) => CallPlain(call);

    private async Task<T> CallPlain<T>(Func<Task<T>> call)
    {
        try { return await call(); }
        catch (ApiException<RawError> ex) { throw Translate(ex, Raw(ex.Error)); }
        catch (ResponseDeserializationException ex) { throw Fail(ex); }
        catch (ApiException ex) { throw Translate(ex, null); }
        catch (BillingException) { throw; }
        catch (SdkException ex) { throw Fail(ex); }
    }

    private static bool IsNotFound(Exception ex) => ex is ApiException a && a.StatusCode == HttpStatusCode.NotFound;

    /// <summary>
    /// A write whose connection fails may still have landed: re-read what Maxio holds. A found result settles
    /// the write; otherwise the outcome is reported as unknown (never as a plain failure).
    /// </summary>
    private async Task<T> Write<T>(Func<Task<T>> write, Func<Task<T?>> reconcile, string what) where T : class
    {
        try
        {
            return await write();
        }
        catch (Exception ex) when (ex is SdkConnectionException or ResponseDeserializationException { StatusCode: >= HttpStatusCode.OK and < HttpStatusCode.MultipleChoices })
        {
            _logger.LogWarning(ex, "Maxio write '{What}' has an unknown outcome; re-reading", what);
            T? settled = null;
            try { settled = await reconcile(); } catch (Exception inner) { _logger.LogWarning(inner, "Reconcile of '{What}' failed", what); }
            if (settled != null) return settled;
            throw new BillingException(504, $"The outcome of '{what}' is unknown (the billing provider did not confirm it). Check the current state before retrying.", ex);
        }
    }

    // --- claims: claim -> SDK call -> release. The primary key refuses the second concurrent caller. ---

    private async Task ClaimAsync(string key, string? reference, string conflictMessage, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                _db.ChangeTracker.Clear();
                _db.MaxioClaims.Add(new MaxioClaim { Key = key, Reference = reference, CreatedAt = DateTimeOffset.UtcNow });
                await _db.SaveChangesAsync(ct);
                return;
            }
            catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException or ArgumentException)
            {
                _db.ChangeTracker.Clear();
                var existing = await _db.MaxioClaims.AsNoTracking().FirstOrDefaultAsync(c => c.Key == key, ct);
                if (existing != null && DateTimeOffset.UtcNow - existing.CreatedAt > ClaimExpiry && attempt == 0)
                {
                    await ReleaseAsync(key);   // stale claim of a crashed request: expire it and retry once
                    continue;
                }
                throw new BillingException(409, conflictMessage, ex);
            }
        }
    }

    private async Task ReleaseAsync(string key)
    {
        try
        {
            _db.ChangeTracker.Clear();
            _db.MaxioClaims.Remove(new MaxioClaim { Key = key });
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not release claim {Key}", key);
        }
        finally
        {
            _db.ChangeTracker.Clear();
        }
    }

    // ------------------------------------------------------------------ plans

    private static int? BrandIdOf(SdkProduct p)
    {
        var handle = p.ProductFamily?.Handle;
        if (handle != null && handle.StartsWith(BrandFamilyPrefix, StringComparison.Ordinal)
            && int.TryParse(handle.AsSpan(BrandFamilyPrefix.Length), out var id)) return id;
        return null;
    }

    private static PlanDto ToDto(SdkProduct p) => new(
        p.Id ?? 0, p.Handle, p.Name, p.Description, p.PriceInCents,
        p.PriceInCents.HasValue ? p.PriceInCents.Value / 100m : null,
        p.Interval, p.IntervalUnit?.Value,
        BrandIdOf(p), BrandIdOf(p) != null ? p.ProductFamily?.Name : null);

    /// <summary>All plans a shopper may subscribe to (non-archived products); Truncated when the page cap was reached.</summary>
    public async Task<PlanListResult> ListPlansAsync(CancellationToken ct)
    {
        var plans = new List<PlanDto>();
        var truncated = false;
        for (var page = 1; ; page++)
        {
            if (page > MaxProductPages) { truncated = true; break; }
            var current = await CallPlain(() => _client.Products.ListProducts(new ListProductsRequest { Page = page, PerPage = PerPage }, cancellationToken: ct));
            foreach (var wrapper in current)
            {
                var p = wrapper.Product;
                if (p.ArchivedAt != null || p.ProductFamily?.ArchivedAt != null) continue;
                plans.Add(ToDto(p));
            }
            if (current.Count < PerPage) break;
        }
        return new PlanListResult(plans, truncated);
    }

    private async Task<PlanDto> RequireOfferedPlanAsync(string? handle, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(handle)) throw new BillingException(400, "planHandle is required.");
        var catalog = await ListPlansAsync(ct);
        var plan = catalog.Plans.FirstOrDefault(p => string.Equals(p.Handle, handle.Trim(), StringComparison.Ordinal));
        if (plan != null) return plan;
        if (catalog.Truncated) throw new BillingException(503, "The plan catalog is too large to verify this plan.");
        throw new BillingException(400, $"'{handle}' is not a plan on offer.");
    }

    // ------------------------------------------------------------------ customer & ownership

    private async Task<Customer?> FindCustomerAsync(string userName, CancellationToken ct)
    {
        try
        {
            return (await CallPlain(() => _client.Customers.ReadCustomerByReference(new ReadCustomerByReferenceRequest { Reference = userName }, cancellationToken: ct))).Customer;
        }
        catch (BillingException b) when (b.StatusCode == 404)
        {
            return null;
        }
    }

    private async Task<Customer> EnsureCustomerAsync(string userName, CancellationToken ct)
    {
        var existing = await FindCustomerAsync(userName, ct);
        if (existing != null) return existing;

        var key = $"cust:{userName}";
        try
        {
            await ClaimAsync(key, userName, "Your account is being set up for billing; try again in a moment.", ct);
        }
        catch (BillingException b) when (b.StatusCode == 409)
        {
            // another caller is creating the customer: reuse theirs, never create a second one
            for (var i = 0; i < 10; i++)
            {
                await Task.Delay(500, ct);
                var other = await FindCustomerAsync(userName, ct);
                if (other != null) return other;
            }
            throw;
        }

        try
        {
            var local = userName.Split('@')[0];
            var request = new CreateCustomerOperationRequest
            {
                Body = new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = string.IsNullOrWhiteSpace(local) ? "Shopper" : local,
                        LastName = "Shopper",
                        Email = userName,
                        Reference = userName,
                    }
                }
            };
            var created = await Write(
                () => Call<CustomerResponse, CreateCustomerError>(
                    () => _client.Customers.CreateCustomer(request, cancellationToken: ct),
                    e => e.TryGetCustomerErrorResponse1(out var l) ? l.Errors?.ToString() : null),
                async () => new CustomerResponse { Customer = await FindCustomerAsync(userName, ct) } is { Customer: not null } r ? r : null,
                "create customer");
            return created.Customer;
        }
        finally
        {
            await ReleaseAsync(key);
        }
    }

    /// <summary>The caller's own subscriptions' ids; 404 for anything else (never reveal other shoppers' data).</summary>
    private async Task<SdkSubscription> OwnedSubscriptionAsync(string userName, int subscriptionId, CancellationToken ct)
    {
        var notFound = new BillingException(404, "Subscription not found.");
        var customer = await FindCustomerAsync(userName, ct);
        if (customer?.Id == null) throw notFound;

        var mine = await CallPlain(() => _client.Customers.ListCustomerSubscriptions(new ListCustomerSubscriptionsRequest { CustomerId = customer.Id.Value }, cancellationToken: ct));
        if (!mine.Any(s => s.Subscription.Id == subscriptionId)) throw notFound;

        var sub = (await CallPlain(() => _client.Subscriptions.ReadSubscription(new ReadSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: ct))).Subscription;
        if (!string.Equals(sub.Customer?.Reference, userName, StringComparison.Ordinal)) throw notFound;
        return sub;
    }

    private async Task<SdkSubscription> ReadAsync(int id, CancellationToken ct) =>
        (await CallPlain(() => _client.Subscriptions.ReadSubscription(new ReadSubscriptionRequest { SubscriptionId = id }, cancellationToken: ct))).Subscription;

    // ------------------------------------------------------------------ subscription view

    private async Task<SubscriptionDto> ToViewAsync(SdkSubscription s, CancellationToken ct)
    {
        var balance = s.BalanceInCents ?? 0;
        var determined = true;
        var owes = balance > 0;

        if (!owes && s.Id.HasValue)
        {
            const int maxPages = 10;
            for (var page = 1; ; page++)
            {
                if (page > maxPages) { determined = false; break; }
                var res = await CallPlain(() => _client.Invoices.ListInvoices(new ListInvoicesRequest { SubscriptionId = s.Id, Status = InvoiceStatus.Open, Page = page, PerPage = PerPage }, cancellationToken: ct));
                if (res.Invoices.Any(i => decimal.TryParse(i.DueAmount, NumberStyles.Any, CultureInfo.InvariantCulture, out var due) && due > 0))
                {
                    owes = true;
                    break;
                }
                if (res.Invoices.Count < PerPage) break;
            }
        }

        return new SubscriptionDto(
            s.Id ?? 0, s.State?.Value, s.Product?.Handle, s.Product?.Name, s.Product?.Id,
            balance, Paid: determined && !owes, PaidDetermined: determined,
            s.CurrentPeriodEndsAt, s.NextAssessmentAt, s.OnHoldAt);
    }

    public async Task<SubscriptionDto> GetSubscriptionAsync(string userName, int subscriptionId, CancellationToken ct) =>
        await ToViewAsync(await OwnedSubscriptionAsync(userName, subscriptionId, ct), ct);

    // ------------------------------------------------------------------ subscribe

    public async Task<SubscriptionDto> SubscribeAsync(string userName, string? planHandle, CancellationToken ct)
    {
        var plan = await RequireOfferedPlanAsync(planHandle, ct);
        var customer = await EnsureCustomerAsync(userName, ct);

        var key = $"sub:{userName}:{plan.Handle}";
        var reference = Guid.NewGuid().ToString("N");
        await ClaimAsync(key, reference, "A subscription request for this plan is already in progress.", ct);
        try
        {
            var existing = await CallPlain(() => _client.Customers.ListCustomerSubscriptions(new ListCustomerSubscriptionsRequest { CustomerId = customer.Id!.Value }, cancellationToken: ct));
            if (existing.Any(s => s.Subscription.Product?.Handle == plan.Handle
                                  && s.Subscription.State != SubscriptionState.Canceled
                                  && s.Subscription.State != SubscriptionState.Expired))
                throw new BillingException(409, "You are already subscribed to this plan.");

            async Task<SubscriptionResponse> Create(CollectionMethod method) =>
                await Write(
                    () => Call<SubscriptionResponse, CreateSubscriptionError>(
                        () => _client.Subscriptions.CreateSubscription(new CreateSubscriptionOperationRequest
                        {
                            Body = new CreateSubscriptionRequest
                            {
                                Subscription = new CreateSubscription
                                {
                                    ProductHandle = plan.Handle,
                                    CustomerId = customer.Id,
                                    PaymentCollectionMethod = method,
                                    Reference = reference,
                                }
                            }
                        }, cancellationToken: ct),
                        e => e.TryGetErrorListResponse1(out var l) ? ErrList(l) : null),
                    async () => await FindByReferenceAsync(reference, ct),
                    "create subscription");

            SubscriptionResponse created;
            try
            {
                created = await Create(CollectionMethod.Remittance);
            }
            catch (BillingException b) when (b.StatusCode == 422 && b.Message.Contains("collection", StringComparison.OrdinalIgnoreCase))
            {
                created = await Create(CollectionMethod.Invoice);
            }
            return await ToViewAsync(created.Subscription, ct);
        }
        finally
        {
            await ReleaseAsync(key);
        }
    }

    private async Task<SubscriptionResponse?> FindByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            return await _client.Subscriptions.FindSubscription(new FindSubscriptionRequest { Reference = reference }, cancellationToken: ct);
        }
        catch (ApiException<FindSubscriptionError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    // ------------------------------------------------------------------ state changes

    /// <summary>Runs one state change under the per-subscription claim, then re-reads the subscription for the answer.</summary>
    private async Task<SubscriptionDto> ChangeAsync(string userName, int subscriptionId, Func<SdkSubscription, BillingException?> guard,
        Func<SdkSubscription, Task<SdkSubscription>> change, string what, CancellationToken ct)
    {
        var current = await OwnedSubscriptionAsync(userName, subscriptionId, ct);
        var refused = guard(current);
        if (refused != null) throw refused;

        var key = $"op:{subscriptionId}";
        await ClaimAsync(key, what, "Another change to this subscription is in progress.", ct);
        try
        {
            var updated = await change(current);
            return await ToViewAsync(updated, ct);
        }
        finally
        {
            await ReleaseAsync(key);
        }
    }

    private static BillingException Conflict(SdkSubscription s, string action) =>
        new(409, $"Cannot {action} a subscription that is '{s.State?.Value}'.");

    public Task<SubscriptionDto> PauseAsync(string userName, int id, CancellationToken ct) =>
        ChangeAsync(userName, id,
            s => s.State == SubscriptionState.OnHold ? new BillingException(409, "The subscription is already paused.")
               : s.State == SubscriptionState.Active || s.State == SubscriptionState.Trialing || s.State == SubscriptionState.PastDue || s.State == SubscriptionState.SoftFailure ? null
               : Conflict(s, "pause"),
            async s => (await Write(
                () => Call<SubscriptionResponse, PauseSubscriptionError>(
                    () => _client.SubscriptionStatus.PauseSubscription(new PauseSubscriptionRequest { SubscriptionId = id }, cancellationToken: ct),
                    e => e.TryGetErrorListResponse1(out var l) ? ErrList(l) : null),
                async () => await ReconcileState(id, SubscriptionState.OnHold, ct), "pause subscription")).Subscription,
            "pause", ct);

    public Task<SubscriptionDto> ResumeAsync(string userName, int id, CancellationToken ct) =>
        ChangeAsync(userName, id,
            s => s.State == SubscriptionState.OnHold ? null : Conflict(s, "resume"),
            async s => (await Write(
                () => Call<SubscriptionResponse, ResumeSubscriptionError>(
                    () => _client.SubscriptionStatus.ResumeSubscription(new ResumeSubscriptionRequest { SubscriptionId = id }, cancellationToken: ct),
                    e => e.TryGetErrorListResponse1(out var l) ? ErrList(l) : null),
                async () => await ReconcileNotState(id, SubscriptionState.OnHold, ct), "resume subscription")).Subscription,
            "resume", ct);

    public Task<SubscriptionDto> CancelAsync(string userName, int id, CancellationToken ct) =>
        ChangeAsync(userName, id,
            s => s.State == SubscriptionState.Canceled ? new BillingException(409, "The subscription is already canceled.")
               : s.State == SubscriptionState.Expired ? Conflict(s, "cancel") : null,
            async s => (await Write(
                () => CallPlain(() => _client.SubscriptionStatus.CancelSubscription(new CancelSubscriptionRequest
                {
                    SubscriptionId = id,
                    Body = new CancellationRequest { Subscription = new CancellationOptions() }
                }, cancellationToken: ct)),
                async () => await ReconcileState(id, SubscriptionState.Canceled, ct), "cancel subscription")).Subscription,
            "cancel", ct);

    public Task<SubscriptionDto> RestartAsync(string userName, int id, CancellationToken ct) =>
        ChangeAsync(userName, id,
            s => s.State == SubscriptionState.Canceled ? null : Conflict(s, "restart"),
            async s => (await Write(
                () => Call<SubscriptionResponse, ReactivateSubscriptionError>(
                    () => _client.SubscriptionStatus.ReactivateSubscription(new ReactivateSubscriptionOperationRequest { SubscriptionId = id }, cancellationToken: ct),
                    e => e.TryGetErrorListResponse1(out var l) ? ErrList(l) : null),
                async () => await ReconcileNotState(id, SubscriptionState.Canceled, ct), "restart subscription")).Subscription,
            "restart", ct);

    private async Task<SubscriptionResponse?> ReconcileState(int id, SubscriptionState expected, CancellationToken ct)
    {
        var s = await ReadAsync(id, ct);
        return s.State == expected ? new SubscriptionResponse { Subscription = s } : null;
    }

    private async Task<SubscriptionResponse?> ReconcileNotState(int id, SubscriptionState notExpected, CancellationToken ct)
    {
        var s = await ReadAsync(id, ct);
        return s.State != notExpected ? new SubscriptionResponse { Subscription = s } : null;
    }

    public async Task<SubscriptionDto> SwitchAsync(string userName, int id, string? planHandle, CancellationToken ct)
    {
        var plan = await RequireOfferedPlanAsync(planHandle, ct);

        return await ChangeAsync(userName, id,
            s => s.Product?.Handle == plan.Handle && s.State != SubscriptionState.Canceled
                ? new BillingException(409, "The subscription is already on this plan.") : null,
            async s =>
            {
                Task<SubscriptionResponse> Migrate() => Write(
                    () => Call<SubscriptionResponse, MigrateSubscriptionProductError>(
                        () => _client.SubscriptionProducts.MigrateSubscriptionProduct(new MigrateSubscriptionProductRequest
                        {
                            SubscriptionId = id,
                            Body = new SubscriptionProductMigrationRequest
                            {
                                Migration = new SubscriptionProductMigration { ProductHandle = plan.Handle }
                            }
                        }, cancellationToken: ct),
                        e => e.TryGetErrorListResponse1(out var l) ? ErrList(l) : null),
                    async () =>
                    {
                        var r = await ReadAsync(id, ct);
                        return r.Product?.Handle == plan.Handle ? new SubscriptionResponse { Subscription = r } : null;
                    },
                    "switch plan");

                SubscriptionResponse result;
                try
                {
                    result = await Migrate();
                }
                catch (BillingException b) when (s.State == SubscriptionState.Canceled && b.StatusCode == 422)
                {
                    // Maxio only migrates active/trialing subscriptions: put a canceled one back on, then move it.
                    await Write(
                        () => Call<SubscriptionResponse, ReactivateSubscriptionError>(
                            () => _client.SubscriptionStatus.ReactivateSubscription(new ReactivateSubscriptionOperationRequest { SubscriptionId = id }, cancellationToken: ct),
                            e => e.TryGetErrorListResponse1(out var l) ? ErrList(l) : null),
                        async () => await ReconcileNotState(id, SubscriptionState.Canceled, ct), "reactivate for switch");
                    result = await Migrate();
                }

                // never report a switch that Maxio did not apply
                var after = await ReadAsync(id, ct);
                if (after.Product?.Handle != plan.Handle || after.State == SubscriptionState.Canceled)
                    throw new BillingException(409, "The plan switch was not applied by the billing provider.");
                return after;
            },
            "switch", ct);
    }

    // ------------------------------------------------------------------ usage for a day

    public async Task<UsageDayDto> GetUsageAsync(string userName, int subscriptionId, string? dateText, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dateText) || !Regex.IsMatch(dateText, @"^\d{4}-\d{2}-\d{2}$")
            || !DateTime.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new BillingException(400, "date is required in the form YYYY-MM-DD.");

        await OwnedSubscriptionAsync(userName, subscriptionId, ct);

        var comps = await CallPlain(() => _client.SubscriptionComponents.ListSubscriptionComponents(new ListSubscriptionComponentsRequest { SubscriptionId = subscriptionId }, cancellationToken: ct));
        var since = new DateTimeOffset(date, TimeSpan.Zero);
        var until = since.AddDays(1);

        var result = new List<ComponentUsageDto>();
        var anyTruncated = false;
        foreach (var wrapper in comps)
        {
            var c = wrapper.Component;
            if (c.Kind != ComponentKind.MeteredComponent || c.ComponentId == null) continue;

            var entries = new List<UsageEntryDto>();
            decimal total = 0;
            var unparsable = false;
            var truncated = false;
            for (var page = 1; ; page++)
            {
                if (page > MaxProductPages) { truncated = true; break; }
                var usages = await CallPlain(() => _client.SubscriptionComponents.ListUsages(new ListUsagesRequest
                {
                    SubscriptionIdOrReference = subscriptionId,
                    ComponentId = c.ComponentId.Value,
                    SinceDate = since,
                    UntilDate = until,
                    Page = page,
                    PerPage = PerPage,
                }, cancellationToken: ct));

                foreach (var uw in usages)
                {
                    var u = uw.Usage;
                    // the until bound reaches midnight of the next day: keep only the picked day
                    if (u.CreatedAt != null && u.CreatedAt.Value.Date != date.Date) continue;

                    string? qtext = null;
                    if (u.Quantity != null)
                    {
                        if (u.Quantity.TryGetInt(out var qi)) { qtext = qi.ToString(CultureInfo.InvariantCulture); total += qi; }
                        else if (u.Quantity.TryGetString(out var qs))
                        {
                            qtext = qs;
                            if (decimal.TryParse(qs, NumberStyles.Any, CultureInfo.InvariantCulture, out var qd)) total += qd; else unparsable = true;
                        }
                    }
                    entries.Add(new UsageEntryDto(u.Id, qtext, u.Memo, u.CreatedAt));
                }
                if (usages.Count < PerPage) break;
            }
            anyTruncated |= truncated;
            result.Add(new ComponentUsageDto(c.ComponentId, c.ComponentHandle, c.Name, c.UnitName, total, unparsable, truncated, entries));
        }
        return new UsageDayDto(subscriptionId, dateText, anyTruncated, result);
    }

    // ------------------------------------------------------------------ staff: plans

    private static string Slug(string name)
    {
        var s = Regex.Replace(name.Trim().ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return string.IsNullOrEmpty(s) ? "plan" : s;
    }

    private async Task<ProductFamily?> FindFamilyAsync(string handle, CancellationToken ct)
    {
        var families = await CallPlain(() => _client.ProductFamilies.ListProductFamilies(new ListProductFamiliesRequest(), cancellationToken: ct));
        return families.Select(f => f.ProductFamily).FirstOrDefault(f => f?.Handle == handle);
    }

    public async Task<CreatedPlanDto> CreatePlanAsync(string? name, decimal? price, int? brandId, string? brandName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new BillingException(400, "name is required.");
        if (price == null || price <= 0 || decimal.Round(price.Value, 2) != price.Value)
            throw new BillingException(400, "price must be a positive monthly amount with at most 2 decimals.");

        CatalogBrand? brand = null;
        if (brandId != null)
            brand = await _db.CatalogBrands.AsNoTracking().FirstOrDefaultAsync(b => b.Id == brandId, ct);
        else if (!string.IsNullOrWhiteSpace(brandName))
            brand = (await _db.CatalogBrands.AsNoTracking().ToListAsync(ct))
                .FirstOrDefault(b => string.Equals(b.Brand, brandName.Trim(), StringComparison.OrdinalIgnoreCase));
        else
            throw new BillingException(400, "brandId (or brand) is required.");
        if (brand == null) throw new BillingException(400, "The brand is not one of the shop's catalog brands.");

        var familyHandle = BrandFamilyPrefix + brand.Id;
        var family = await FindFamilyAsync(familyHandle, ct);
        if (family == null)
        {
            var famKey = $"fam:{brand.Id}";
            try
            {
                await ClaimAsync(famKey, familyHandle, "brand family being created", ct);
                try
                {
                    var created = await Write(
                        () => Call<ProductFamilyResponse, CreateProductFamilyError>(
                            () => _client.ProductFamilies.CreateProductFamily(new CreateProductFamilyOperationRequest
                            {
                                Body = new CreateProductFamilyRequest
                                {
                                    ProductFamily = new CreateProductFamily { Name = brand.Brand, Handle = familyHandle }
                                }
                            }, cancellationToken: ct),
                            e => e.TryGetErrorListResponse1(out var l) ? ErrList(l) : null),
                        async () => new ProductFamilyResponse { ProductFamily = await FindFamilyAsync(familyHandle, ct) } is { ProductFamily: not null } r ? r : null,
                        "create product family");
                    family = created.ProductFamily;
                }
                finally { await ReleaseAsync(famKey); }
            }
            catch (BillingException b) when (b.StatusCode == 409 && b.InnerException is not ApiException)
            {
                family = await FindFamilyAsync(familyHandle, ct);   // someone else created it: reuse
                if (family == null) throw;
            }
        }

        var handle = Slug(name);
        var planKey = $"plan:{handle}";
        await ClaimAsync(planKey, handle, "A plan with this name is already being created.", ct);
        try
        {
            try
            {
                await CallPlain(() => _client.Products.ReadProductByHandle(new ReadProductByHandleRequest { ApiHandle = handle }, cancellationToken: ct));
                throw new BillingException(409, $"A plan with handle '{handle}' already exists.");
            }
            catch (BillingException b) when (b.StatusCode == 404) { /* free to create */ }

            var made = await Write(
                () => Call<ProductResponse, CreateProductError>(
                    () => _client.Products.CreateProduct(new CreateProductRequest
                    {
                        ProductFamilyId = familyHandle.Insert(0, "handle:"),
                        Body = new CreateOrUpdateProductRequest
                        {
                            Product = new CreateOrUpdateProduct
                            {
                                Name = name.Trim(),
                                Description = name.Trim(),
                                PriceInCents = (long)decimal.Round(price.Value * 100m),
                                Interval = 1,
                                IntervalUnit = IntervalUnit.Month,
                                Handle = handle,
                                RequireCreditCard = false,
                            }
                        }
                    }, cancellationToken: ct),
                    e => e.TryGetErrorListResponse1(out var l) ? ErrList(l) : null),
                async () =>
                {
                    try { return await _client.Products.ReadProductByHandle(new ReadProductByHandleRequest { ApiHandle = handle }, cancellationToken: ct); }
                    catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound) { return null; }
                },
                "create product");
            return new CreatedPlanDto(made.Product.Id, made.Product.Handle, made.Product.Name, price.Value, brand.Id, brand.Brand);
        }
        finally
        {
            await ReleaseAsync(planKey);
        }
    }

    public async Task<CurrencyPriceDto> SetCurrencyPriceAsync(int planId, string? currency, decimal? price, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(currency) || !Regex.IsMatch(currency.Trim(), "^[A-Za-z]{3}$"))
            throw new BillingException(400, "currency must be a 3-letter ISO code.");
        if (price == null || price <= 0) throw new BillingException(400, "price must be positive.");
        if (price != decimal.Truncate(price.Value))
            throw new BillingException(400, "price must be a whole number of currency units.");
        var code = currency.Trim().ToUpperInvariant();

        var catalog = await ListPlansAsync(ct);
        var plan = catalog.Plans.FirstOrDefault(p => p.PlanId == planId);
        if (plan == null)
        {
            if (catalog.Truncated) throw new BillingException(503, "The plan catalog is too large to verify this plan.");
            throw new BillingException(404, "Plan not found.");
        }

        var site = (await CallPlain(() => _client.Sites.ReadSite(cancellationToken: ct))).Site;
        if (string.Equals(site.Currency, code, StringComparison.OrdinalIgnoreCase))
            throw new BillingException(400, $"{code} is the plan's primary currency.");
        var allowed = site.NonPrimaryCurrencies ?? new List<string>();
        if (!allowed.Contains(code, StringComparer.OrdinalIgnoreCase))
            throw new BillingException(400, allowed.Count == 0
                ? "No additional currencies are enabled on the billing site."
                : $"Unsupported currency; allowed: {string.Join(", ", allowed)}.");

        var product = (await CallPlain(() => _client.Products.ReadProduct(new ReadProductRequest { ProductId = planId }, cancellationToken: ct))).Product;
        if (product.DefaultProductPricePointId == null) throw new BillingException(409, "The plan has no default price point.");
        var pricePointId = product.DefaultProductPricePointId.Value;

        var key = $"cur:{planId}:{code}";
        await ClaimAsync(key, code, "A price for this currency is already being set.", ct);
        try
        {
            async Task<CurrencyPrice?> Existing()
            {
                var pp = (await CallPlain(() => _client.ProductPricePoints.ReadProductPricePoint(new ReadProductPricePointRequest
                {
                    ProductId = planId,
                    PricePointId = pricePointId,
                    CurrencyPrices = true,
                }, cancellationToken: ct))).PricePoint;
                return pp.CurrencyPrices?.FirstOrDefault(c => string.Equals(c.Currency, code, StringComparison.OrdinalIgnoreCase));
            }

            var current = await Existing();
            if (current?.Id != null)
            {
                await Write(
                    () => Call<CurrencyPricesResponse, UpdateProductCurrencyPricesError>(
                        () => _client.ProductPricePoints.UpdateProductCurrencyPrices(new UpdateProductCurrencyPricesRequest
                        {
                            ProductPricePointId = pricePointId,
                            Body = new UpdateCurrencyPricesRequest
                            {
                                CurrencyPrices = new[] { new UpdateCurrencyPrice { Id = current.Id.Value, Price = (double)price.Value } }
                            }
                        }, cancellationToken: ct),
                        e => e.TryGetErrorArrayMapResponse1(out var m) ? m.ToString() : null),
                    async () => (await Existing())?.Price == (double)price.Value ? new CurrencyPricesResponse { CurrencyPrices = new List<CurrencyPrice>() } : null,
                    "update currency price");
                return new CurrencyPriceDto(planId, code, price.Value, "updated");
            }

            await Write(
                () => Call<CurrencyPricesResponse, CreateProductCurrencyPricesError>(
                    () => _client.ProductPricePoints.CreateProductCurrencyPrices(new CreateProductCurrencyPricesOperationRequest
                    {
                        ProductPricePointId = pricePointId,
                        Body = new CreateProductCurrencyPricesRequest
                        {
                            CurrencyPrices = new[]
                            {
                                new CreateProductCurrencyPrice { Currency = code, Price = (int)price.Value, Role = CurrencyPriceRole.Baseline }
                            }
                        }
                    }, cancellationToken: ct),
                    e => e.TryGetErrorArrayMapResponse1(out var m) ? m.ToString() : null),
                async () => await Existing() != null ? new CurrencyPricesResponse { CurrencyPrices = new List<CurrencyPrice>() } : null,
                "create currency price");
            return new CurrencyPriceDto(planId, code, price.Value, "created");
        }
        finally
        {
            await ReleaseAsync(key);
        }
    }

    // ------------------------------------------------------------------ staff: next billing date

    public async Task<NextBillingDto> SetNextBillingDateAsync(int subscriptionId, string? dateText, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dateText)
            || !DateTimeOffset.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var when))
            throw new BillingException(400, "nextBillingDate is required (ISO date or date-time).");
        if (when <= DateTimeOffset.UtcNow) throw new BillingException(400, "nextBillingDate must be in the future.");

        var current = await ReadAsync(subscriptionId, ct);
        if (current.State == SubscriptionState.Canceled || current.State == SubscriptionState.Expired
            || current.State == SubscriptionState.OnHold || current.State == SubscriptionState.Pending)
            throw new BillingException(409, $"A '{current.State?.Value}' subscription is not billed, so a billing date cannot be set.");

        var key = $"op:{subscriptionId}";
        await ClaimAsync(key, "next-billing-date", "Another change to this subscription is in progress.", ct);
        try
        {
            var updated = await Write(
                () => Call<SubscriptionResponse, UpdateSubscriptionError>(
                    () => _client.Subscriptions.UpdateSubscription(new UpdateSubscriptionOperationRequest
                    {
                        SubscriptionId = subscriptionId,
                        Body = new UpdateSubscriptionRequest { Subscription = new UpdateSubscription { NextBillingAt = when } }
                    }, cancellationToken: ct),
                    e => e.TryGetErrorListResponse1(out var l) ? ErrList(l) : null),
                async () =>
                {
                    var r = await ReadAsync(subscriptionId, ct);
                    return r.CurrentPeriodEndsAt?.UtcDateTime.Date == when.UtcDateTime.Date ? new SubscriptionResponse { Subscription = r } : null;
                },
                "set next billing date");

            var held = await ReadAsync(subscriptionId, ct);
            if (held.CurrentPeriodEndsAt != null && Math.Abs((held.CurrentPeriodEndsAt.Value - when).TotalHours) > 36)
                throw new BillingException(409, $"The billing provider holds {held.CurrentPeriodEndsAt:O} instead of the requested date.");
            return new NextBillingDto(subscriptionId, when, held.CurrentPeriodEndsAt, held.NextAssessmentAt);
        }
        finally
        {
            await ReleaseAsync(key);
        }
    }
}
