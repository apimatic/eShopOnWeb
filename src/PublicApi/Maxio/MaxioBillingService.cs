using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// All calls to Maxio Advanced Billing. Every failure that stops the app from carrying out a request leaves
/// here as a <see cref="BillingException"/> carrying the status to answer with and a reason.
/// </summary>
public class MaxioBillingService
{
    public const string FAMILY_HANDLE_PREFIX = "eshop-brand-";
    private const int MAX_PAGES = 50;
    private const int PAGE_SIZE = 200;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(MaxioAdvancedBillingClient client, ILogger<MaxioBillingService> logger)
    {
        _client = client;
        _logger = logger;
    }

    // ---------------------------------------------------------------- error boundary

    private static string RawText(ApiError e) =>
        e.TryGetRawError(out var raw) ? Trim(raw.ReadAsString()) : string.Empty;

    private static string Trim(string s) => s.Length > 300 ? s[..300] : s;

    private static string Join(IEnumerable<string>? errors) => string.Join("; ", errors ?? []);

    private static string ListErr(Func<ErrorListResponse1?> get, ApiError e) =>
        get() is { } l ? Join(l.Errors) : RawText(e);

    /// <summary>Maps a provider status to the status this API answers with.</summary>
    private static int MapStatus(HttpStatusCode status) => (int)status switch
    {
        404 => 404,
        400 or 422 or 409 => 422,
        401 or 403 => 502, // our credential, not the caller's fault
        429 => 503,
        _ => 502
    };

    private BillingException Translate(string op, HttpStatusCode status, string detail, Exception ex)
    {
        _logger.LogWarning("Maxio {Operation} answered {Status}: {Detail}", op, (int)status, detail);
        var code = MapStatus(status);
        var reason = code == 502 ? "The billing system could not process the request." :
            code == 503 ? "The billing system is busy, try again later." :
            code == 404 ? "Not found in the billing system." :
            string.IsNullOrWhiteSpace(detail) ? "The billing system rejected the request." : detail;
        return new BillingException(code, reason, ex);
    }

    private async Task<T> Exec<T, TError>(string op, Func<Task<T>> call, Func<TError, string> describe)
        where TError : class
    {
        try
        {
            return await call();
        }
        catch (ApiException<TError> ex)
        {
            throw Translate(op, ex.StatusCode, describe(ex.Error), ex);
        }
        catch (ApiException ex) // includes ResponseDeserializationException
        {
            throw Translate(op, ex.StatusCode, string.Empty, ex);
        }
        catch (SdkTimeoutException ex)
        {
            _logger.LogError(ex, "Maxio {Operation} timed out", op);
            throw new BillingException(504, "The billing system did not answer in time.", ex);
        }
        catch (SdkConnectionException ex)
        {
            _logger.LogError(ex, "Maxio {Operation} connection failure", op);
            throw new BillingException(502, "The billing system is unreachable.", ex);
        }
        catch (AuthSchemeException ex)
        {
            _logger.LogError(ex, "Maxio credentials unusable for {Operation}", op);
            throw new BillingException(502, "The billing system is not available.", ex);
        }
    }

    private Task<T> ExecRaw<T>(string op, Func<Task<T>> call) =>
        Exec<T, RawError>(op, call, e => Trim(e.ReadAsString()));

    private static bool IsNotFound(BillingException ex) => ex.StatusCode == 404;

    // ---------------------------------------------------------------- plans

    public record PlanInfo(int Id, string Handle, string Name, string? Description, long PriceInCents, int Interval,
        string IntervalUnit, string Brand, int DefaultPricePointId);

    public record PlanList(List<PlanInfo> Plans, bool Truncated);

    /// <summary>The plans this app offers: non-archived products in the product families created for catalog brands.</summary>
    public async Task<PlanList> ListPlansAsync(CancellationToken ct)
    {
        var families = await ExecRaw("ListProductFamilies", () => _client.ProductFamilies.ListProductFamilies(
            new MaxioAdvancedBilling.Requests.ProductFamilies.ListProductFamiliesRequest(), cancellationToken: ct));

        var plans = new List<PlanInfo>();
        var truncated = false;
        foreach (var fr in families)
        {
            var family = fr.ProductFamily;
            if (family?.Id is null || family.ArchivedAt is not null ||
                family.Handle is null || !family.Handle.StartsWith(FAMILY_HANDLE_PREFIX, StringComparison.Ordinal))
                continue;

            var familyId = family.Id.Value;
            for (var page = 1; ; page++)
            {
                if (page > MAX_PAGES) { truncated = true; break; }
                var products = await Exec<IReadOnlyList<ProductResponse>, ListProductsForProductFamilyError>(
                    "ListProductsForProductFamily",
                    () => _client.ProductFamilies.ListProductsForProductFamily(
                        new MaxioAdvancedBilling.Requests.ProductFamilies.ListProductsForProductFamilyRequest
                        {
                            ProductFamilyId = familyId.ToString(CultureInfo.InvariantCulture),
                            Page = page,
                            PerPage = PAGE_SIZE,
                        }, cancellationToken: ct),
                    e => e.TryGetString(out var s) ? s : RawText(e));

                foreach (var pr in products)
                {
                    var p = pr.Product;
                    if (p?.Id is null || p.Handle is null || p.ArchivedAt is not null) continue;
                    plans.Add(new PlanInfo(p.Id.Value, p.Handle, p.Name ?? p.Handle, p.Description,
                        p.PriceInCents ?? 0, p.Interval ?? 1, p.IntervalUnit?.Value ?? "month",
                        family.Name ?? string.Empty, p.DefaultProductPricePointId ?? 0));
                }
                if (products.Count < PAGE_SIZE) break;
            }
        }
        return new PlanList(plans, truncated);
    }

    private async Task<PlanInfo> RequireOfferedPlanByHandleAsync(string handle, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(handle))
            throw new BillingException(400, "A plan handle is required.");
        var list = await ListPlansAsync(ct);
        var plan = list.Plans.FirstOrDefault(p => string.Equals(p.Handle, handle, StringComparison.Ordinal));
        if (plan is null)
            throw new BillingException(404, $"No plan with handle '{handle}' is offered.");
        return plan;
    }

    private async Task<PlanInfo> RequireOfferedPlanByIdAsync(int planId, CancellationToken ct)
    {
        var list = await ListPlansAsync(ct);
        var plan = list.Plans.FirstOrDefault(p => p.Id == planId);
        if (plan is null)
            throw new BillingException(404, $"No plan with id {planId} is offered.");
        return plan;
    }

    // ---------------------------------------------------------------- customers

    private async Task<int> FindOrCreateCustomerAsync(string user, CancellationToken ct)
    {
        async Task<int?> Read()
        {
            try
            {
                var r = await ExecRaw("ReadCustomerByReference", () => _client.Customers.ReadCustomerByReference(
                    new MaxioAdvancedBilling.Requests.Customers.ReadCustomerByReferenceRequest { Reference = user },
                    cancellationToken: ct));
                return r.Customer?.Id;
            }
            catch (BillingException ex) when (IsNotFound(ex))
            {
                return null;
            }
        }

        var existing = await Read();
        if (existing is not null) return existing.Value;

        var local = user.Contains('@') ? user[..user.IndexOf('@')] : user;
        var email = user.Contains('@') ? user : $"{Regex.Replace(user, "[^A-Za-z0-9]", "")}@example.invalid";
        try
        {
            var created = await Exec<CustomerResponse, CreateCustomerError>("CreateCustomer",
                () => _client.Customers.CreateCustomer(
                    new MaxioAdvancedBilling.Requests.Customers.CreateCustomerOperationRequest
                    {
                        Body = new CreateCustomerRequest
                        {
                            Customer = new CreateCustomer
                            {
                                FirstName = string.IsNullOrWhiteSpace(local) ? "Shopper" : local,
                                LastName = "Shopper",
                                Email = email,
                                Reference = user
                            }
                        }
                    }, cancellationToken: ct),
                e => e.TryGetCustomerErrorResponse1(out var c) ? c.ToString() ?? string.Empty : RawText(e));
            return created.Customer?.Id ?? throw new BillingException(502, "The billing system returned no customer.");
        }
        catch (BillingException) // lost a concurrent create, or the outcome is unknown: settle by re-reading
        {
            var again = await Read();
            if (again is not null) return again.Value;
            throw;
        }
    }

    // ---------------------------------------------------------------- subscriptions

    public record SubscriptionInfo(int Id, string State, string? PlanHandle, string? PlanName, bool Paid,
        long BalanceInCents, DateTimeOffset? CurrentPeriodEndsAt, DateTimeOffset? CanceledAt);

    private async Task<Subscription> ReadOwnedAsync(string user, int id, CancellationToken ct)
    {
        Subscription? sub;
        try
        {
            var r = await ExecRaw("ReadSubscription", () => _client.Subscriptions.ReadSubscription(
                new MaxioAdvancedBilling.Requests.Subscriptions.ReadSubscriptionRequest { SubscriptionId = id },
                cancellationToken: ct));
            sub = r.Subscription;
        }
        catch (BillingException ex) when (IsNotFound(ex))
        {
            throw new BillingException(404, "Subscription not found.");
        }
        if (sub is null || !string.Equals(sub.Customer?.Reference, user, StringComparison.Ordinal))
            throw new BillingException(404, "Subscription not found.");
        return sub;
    }

    private async Task<Subscription> ReadAnyAsync(int id, CancellationToken ct)
    {
        try
        {
            var r = await ExecRaw("ReadSubscription", () => _client.Subscriptions.ReadSubscription(
                new MaxioAdvancedBilling.Requests.Subscriptions.ReadSubscriptionRequest { SubscriptionId = id },
                cancellationToken: ct));
            return r.Subscription ?? throw new BillingException(404, "Subscription not found.");
        }
        catch (BillingException ex) when (IsNotFound(ex))
        {
            throw new BillingException(404, "Subscription not found.");
        }
    }

    private async Task<decimal> OpenInvoiceDueAsync(int id, CancellationToken ct)
    {
        decimal due = 0;
        for (var page = 1; ; page++)
        {
            if (page > 20)
                throw new BillingException(502, "Cannot determine what is owed on this subscription.");
            var inv = await ExecRaw("ListInvoices", () => _client.Invoices.ListInvoices(
                new MaxioAdvancedBilling.Requests.Invoices.ListInvoicesRequest
                {
                    SubscriptionId = id,
                    Status = InvoiceStatus.Open,
                    Page = page,
                    PerPage = PAGE_SIZE
                }, cancellationToken: ct));
            var items = inv.Invoices ?? [];
            foreach (var i in items)
            {
                if (decimal.TryParse(i.DueAmount, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) due += d;
            }
            if (items.Count < PAGE_SIZE) break;
        }
        return due;
    }

    private async Task<SubscriptionInfo> ToInfoAsync(Subscription s, CancellationToken ct)
    {
        if (s.Id is null) throw new BillingException(502, "The billing system returned no subscription id.");
        if (s.BalanceInCents is null)
            throw new BillingException(502, "Cannot determine what is owed on this subscription.");
        // The shopper owes nothing when neither the outstanding balance nor any open invoice is due.
        var owes = s.BalanceInCents.Value > 0 || await OpenInvoiceDueAsync(s.Id.Value, ct) > 0;
        return new SubscriptionInfo(s.Id.Value, s.State?.Value ?? "unknown", s.Product?.Handle, s.Product?.Name,
            !owes, s.BalanceInCents.Value, s.CurrentPeriodEndsAt, s.CanceledAt);
    }

    public async Task<SubscriptionInfo> SubscribeAsync(string user, string planHandle, string claimReference, CancellationToken ct)
    {
        var plan = await RequireOfferedPlanByHandleAsync(planHandle, ct);
        var customerId = await FindOrCreateCustomerAsync(user, ct);
        try
        {
            var r = await Exec<SubscriptionResponse, CreateSubscriptionError>("CreateSubscription",
                () => _client.Subscriptions.CreateSubscription(
                    new MaxioAdvancedBilling.Requests.Subscriptions.CreateSubscriptionOperationRequest
                    {
                        Body = new CreateSubscriptionRequest
                        {
                            Subscription = new CreateSubscription
                            {
                                ProductHandle = plan.Handle,
                                CustomerId = customerId,
                                Reference = claimReference,
                                PaymentCollectionMethod = CollectionMethod.Remittance
                            }
                        }
                    }, cancellationToken: ct),
                e => ListErr(() => e.TryGetErrorListResponse1(out var l) ? l : null, e));
            return await ToInfoAsync(r.Subscription ?? throw new BillingException(502, "No subscription returned."), ct);
        }
        catch (BillingException ex) when (ex.StatusCode >= 500)
        {
            // Unknown outcome: the write may have landed. Settle it by looking the subscription up by the reference we sent.
            var found = await TryFindByReferenceAsync(claimReference, ct);
            if (found?.Id is not null) return await ToInfoAsync(found, ct);
            throw;
        }
    }

    private async Task<Subscription?> TryFindByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var r = await Exec<SubscriptionResponse, FindSubscriptionError>("FindSubscription",
                () => _client.Subscriptions.FindSubscription(
                    new MaxioAdvancedBilling.Requests.Subscriptions.FindSubscriptionRequest { Reference = reference },
                    cancellationToken: ct),
                RawText);
            return r.Subscription;
        }
        catch (BillingException)
        {
            return null;
        }
    }

    public async Task<SubscriptionInfo> GetSubscriptionAsync(string user, int id, CancellationToken ct) =>
        await ToInfoAsync(await ReadOwnedAsync(user, id, ct), ct);

    private static void RequireState(Subscription s, string action, params string[] allowed)
    {
        var state = s.State?.Value ?? "unknown";
        if (!allowed.Contains(state))
            throw new BillingException(409, $"Cannot {action} a subscription that is {state.Replace('_', ' ')}.");
    }

    /// <summary>After a write whose connection failed, re-read and accept the write if it landed.</summary>
    private async Task<Subscription> SettleAsync(int id, Func<Subscription, bool> landed, BillingException original, CancellationToken ct)
    {
        try
        {
            var sub = await ReadAnyAsync(id, ct);
            if (landed(sub)) return sub;
        }
        catch (BillingException)
        {
            // fall through
        }
        throw original;
    }

    public async Task<SubscriptionInfo> PauseAsync(string user, int id, CancellationToken ct)
    {
        var current = await ReadOwnedAsync(user, id, ct);
        RequireState(current, "pause", "active", "trialing");
        Subscription result;
        try
        {
            var r = await Exec<SubscriptionResponse, PauseSubscriptionError>("PauseSubscription",
                () => _client.SubscriptionStatus.PauseSubscription(
                    new MaxioAdvancedBilling.Requests.SubscriptionStatus.PauseSubscriptionRequest { SubscriptionId = id },
                    cancellationToken: ct),
                e => ListErr(() => e.TryGetErrorListResponse1(out var l) ? l : null, e));
            result = r.Subscription ?? throw new BillingException(502, "No subscription returned.");
        }
        catch (BillingException ex) when (ex.StatusCode >= 500)
        {
            result = await SettleAsync(id, s => s.State?.Value is "on_hold" or "paused", ex, ct);
        }
        return await ToInfoAsync(result, ct);
    }

    public async Task<SubscriptionInfo> ResumeAsync(string user, int id, CancellationToken ct)
    {
        var current = await ReadOwnedAsync(user, id, ct);
        RequireState(current, "resume", "on_hold", "paused");
        Subscription result;
        try
        {
            var r = await Exec<SubscriptionResponse, ResumeSubscriptionError>("ResumeSubscription",
                () => _client.SubscriptionStatus.ResumeSubscription(
                    new MaxioAdvancedBilling.Requests.SubscriptionStatus.ResumeSubscriptionRequest { SubscriptionId = id },
                    cancellationToken: ct),
                e => ListErr(() => e.TryGetErrorListResponse1(out var l) ? l : null, e));
            result = r.Subscription ?? throw new BillingException(502, "No subscription returned.");
        }
        catch (BillingException ex) when (ex.StatusCode >= 500)
        {
            result = await SettleAsync(id, s => s.State?.Value is not ("on_hold" or "paused"), ex, ct);
        }
        return await ToInfoAsync(result, ct);
    }

    public async Task<SubscriptionInfo> CancelAsync(string user, int id, CancellationToken ct)
    {
        var current = await ReadOwnedAsync(user, id, ct);
        var state = current.State?.Value ?? "unknown";
        if (state is "canceled" or "expired" or "failed_to_create")
            throw new BillingException(409, $"The subscription is already {state.Replace('_', ' ')}.");
        Subscription result;
        try
        {
            var r = await Exec<SubscriptionResponse, CancelSubscriptionError>("CancelSubscription",
                () => _client.SubscriptionStatus.CancelSubscription(
                    new MaxioAdvancedBilling.Requests.SubscriptionStatus.CancelSubscriptionRequest
                    {
                        SubscriptionId = id,
                        Body = new CancellationRequest { Subscription = new CancellationOptions() }
                    }, cancellationToken: ct),
                e => e.TryGetCancelSubscriptionErrorResponse(out var c) ? c.ToString() ?? string.Empty : RawText(e));
            result = r.Subscription ?? throw new BillingException(502, "No subscription returned.");
        }
        catch (BillingException ex) when (ex.StatusCode >= 500)
        {
            result = await SettleAsync(id, s => s.State?.Value == "canceled", ex, ct);
        }
        return await ToInfoAsync(result, ct);
    }

    public async Task<SubscriptionInfo> RestartAsync(string user, int id, CancellationToken ct)
    {
        var current = await ReadOwnedAsync(user, id, ct);
        RequireState(current, "restart", "canceled");
        Subscription result;
        try
        {
            var r = await Exec<SubscriptionResponse, ReactivateSubscriptionError>("ReactivateSubscription",
                () => _client.SubscriptionStatus.ReactivateSubscription(
                    new MaxioAdvancedBilling.Requests.SubscriptionStatus.ReactivateSubscriptionOperationRequest
                    {
                        SubscriptionId = id
                    }, cancellationToken: ct),
                e => ListErr(() => e.TryGetErrorListResponse1(out var l) ? l : null, e));
            result = r.Subscription ?? throw new BillingException(502, "No subscription returned.");
        }
        catch (BillingException ex) when (ex.StatusCode >= 500)
        {
            result = await SettleAsync(id, s => s.State?.Value != "canceled", ex, ct);
        }
        return await ToInfoAsync(result, ct);
    }

    public async Task<SubscriptionInfo> SwitchPlanAsync(string user, int id, string planHandle, CancellationToken ct)
    {
        var current = await ReadOwnedAsync(user, id, ct);
        var plan = await RequireOfferedPlanByHandleAsync(planHandle, ct);
        if (string.Equals(current.Product?.Handle, plan.Handle, StringComparison.Ordinal))
            throw new BillingException(400, "The subscription is already on that plan.");

        var state = current.State?.Value ?? "unknown";
        Subscription result;
        try
        {
            if (state is "active" or "trialing")
            {
                var r = await Exec<SubscriptionResponse, MigrateSubscriptionProductError>("MigrateSubscriptionProduct",
                    () => _client.SubscriptionProducts.MigrateSubscriptionProduct(
                        new MaxioAdvancedBilling.Requests.SubscriptionProducts.MigrateSubscriptionProductRequest
                        {
                            SubscriptionId = id,
                            Body = new SubscriptionProductMigrationRequest
                            {
                                Migration = new SubscriptionProductMigration { ProductHandle = plan.Handle }
                            }
                        }, cancellationToken: ct),
                    e => ListErr(() => e.TryGetErrorListResponse1(out var l) ? l : null, e));
                result = r.Subscription ?? throw new BillingException(502, "No subscription returned.");
            }
            else if (state is "canceled" or "trial_ended")
            {
                // A migration is only valid for live subscriptions: change the product in place. The
                // subscription stays canceled, now on the new plan; the shopper restarts it separately.
                var r = await Exec<SubscriptionResponse, UpdateSubscriptionError>("UpdateSubscription",
                    () => _client.Subscriptions.UpdateSubscription(
                        new MaxioAdvancedBilling.Requests.Subscriptions.UpdateSubscriptionOperationRequest
                        {
                            SubscriptionId = id,
                            Body = new UpdateSubscriptionRequest
                            {
                                Subscription = new UpdateSubscription { ProductHandle = plan.Handle }
                            }
                        }, cancellationToken: ct),
                    e => ListErr(() => e.TryGetErrorListResponse1(out var l) ? l : null, e));
                result = r.Subscription ?? throw new BillingException(502, "No subscription returned.");
            }
            else
            {
                throw new BillingException(409, $"Cannot switch plan on a subscription that is {state.Replace('_', ' ')}.");
            }
        }
        catch (BillingException ex) when (ex.StatusCode >= 500)
        {
            result = await SettleAsync(id, s => s.Product?.Handle == plan.Handle, ex, ct);
        }

        // Confirm the new plan really is on the subscription.
        if (result.Product?.Handle != plan.Handle)
            result = await ReadAnyAsync(id, ct);
        if (result.Product?.Handle != plan.Handle)
            throw new BillingException(502, "The billing system did not put the subscription on the new plan.");
        return await ToInfoAsync(result, ct);
    }

    // ---------------------------------------------------------------- usage

    public record UsageItem(long? Id, string? Quantity, string? Memo, DateTimeOffset? CreatedAt);

    public record ComponentUsage(int ComponentId, string? Handle, string? Name, string? UnitName, List<UsageItem> Usages,
        decimal? TotalQuantity);

    public record UsageResult(DateOnly Date, List<ComponentUsage> Components, bool Truncated);

    public async Task<UsageResult> GetUsageAsync(string user, int id, DateOnly date, CancellationToken ct)
    {
        await ReadOwnedAsync(user, id, ct);
        var comps = await ExecRaw("ListSubscriptionComponents", () => _client.SubscriptionComponents.ListSubscriptionComponents(
            new MaxioAdvancedBilling.Requests.SubscriptionComponents.ListSubscriptionComponentsRequest { SubscriptionId = id },
            cancellationToken: ct));

        var since = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var until = since.AddDays(1);
        var truncated = false;
        var result = new List<ComponentUsage>();

        foreach (var cr in comps)
        {
            var c = cr.Component;
            if (c?.ComponentId is null || c.Kind?.Value != "metered_component") continue;
            var componentId = c.ComponentId.Value;
            var usages = new List<UsageItem>();
            for (var page = 1; ; page++)
            {
                if (page > MAX_PAGES) { truncated = true; break; }
                var list = await ExecRaw("ListUsages", () => _client.SubscriptionComponents.ListUsages(
                    new MaxioAdvancedBilling.Requests.SubscriptionComponents.ListUsagesRequest
                    {
                        SubscriptionIdOrReference = id,
                        ComponentId = componentId,
                        SinceDate = since,
                        UntilDate = until,
                        Page = page,
                        PerPage = PAGE_SIZE
                    }, cancellationToken: ct));
                foreach (var ur in list)
                {
                    var u = ur.Usage;
                    if (u is null) continue;
                    // The range's upper bound is inclusive of midnight: keep only the requested calendar day.
                    if (u.CreatedAt is { } created && DateOnly.FromDateTime(created.DateTime) != date) continue;
                    usages.Add(new UsageItem(u.Id, QuantityText(u.Quantity), u.Memo, u.CreatedAt));
                }
                if (list.Count < PAGE_SIZE) break;
            }

            decimal? total = null;
            if (usages.All(x => decimal.TryParse(x.Quantity, NumberStyles.Any, CultureInfo.InvariantCulture, out _)))
                total = usages.Sum(x => decimal.Parse(x.Quantity!, NumberStyles.Any, CultureInfo.InvariantCulture));
            result.Add(new ComponentUsage(componentId, c.ComponentHandle, c.Name, c.UnitName, usages, total));
        }
        return new UsageResult(date, result, truncated);
    }

    private static IEnumerable<string> ErrorTexts(object? value)
    {
        if (value is System.Text.Json.JsonElement el)
        {
            if (el.ValueKind == System.Text.Json.JsonValueKind.Array)
                return el.EnumerateArray().Select(x => x.ToString());
            return [el.ToString()];
        }
        return [value?.ToString() ?? string.Empty];
    }

    private static string? QuantityText(MaxioAdvancedBilling.Models.AnyOf.Quantity1? q)
    {
        if (q is null) return null;
        if (q.TryGetInt(out var i)) return i.ToString(CultureInfo.InvariantCulture);
        if (q.TryGetString(out var s)) return s;
        return null;
    }

    // ---------------------------------------------------------------- staff: plans

    public static string Slug(string name)
    {
        var sb = new StringBuilder();
        foreach (var ch in name.ToLowerInvariant())
            sb.Append(char.IsLetterOrDigit(ch) && ch < 128 ? ch : '-');
        var slug = Regex.Replace(sb.ToString(), "-+", "-").Trim('-');
        return slug.Length == 0 ? "plan" : slug;
    }

    public string PlanHandleFor(string name, int brandId) => $"{Slug(name)}-b{brandId}";

    private async Task<int> FindOrCreateFamilyAsync(int brandId, string brandName, CancellationToken ct)
    {
        var handle = FAMILY_HANDLE_PREFIX + brandId.ToString(CultureInfo.InvariantCulture);

        async Task<int?> Find()
        {
            var families = await ExecRaw("ListProductFamilies", () => _client.ProductFamilies.ListProductFamilies(
                new MaxioAdvancedBilling.Requests.ProductFamilies.ListProductFamiliesRequest(), cancellationToken: ct));
            return families.FirstOrDefault(f => f.ProductFamily?.Handle == handle)?.ProductFamily?.Id;
        }

        var existing = await Find();
        if (existing is not null) return existing.Value;
        try
        {
            var created = await Exec<ProductFamilyResponse, CreateProductFamilyError>("CreateProductFamily",
                () => _client.ProductFamilies.CreateProductFamily(
                    new MaxioAdvancedBilling.Requests.ProductFamilies.CreateProductFamilyOperationRequest
                    {
                        Body = new CreateProductFamilyRequest
                        {
                            ProductFamily = new CreateProductFamily { Name = brandName, Handle = handle }
                        }
                    }, cancellationToken: ct),
                e => ListErr(() => e.TryGetErrorListResponse1(out var l) ? l : null, e));
            return created.ProductFamily?.Id ?? throw new BillingException(502, "No product family returned.");
        }
        catch (BillingException)
        {
            var again = await Find();
            if (again is not null) return again.Value;
            throw;
        }
    }

    public async Task<PlanInfo> CreatePlanAsync(string name, decimal monthlyPrice, int brandId, string brandName, CancellationToken ct)
    {
        var familyId = await FindOrCreateFamilyAsync(brandId, brandName, ct);
        var handle = PlanHandleFor(name, brandId);
        var cents = (long)Math.Round(monthlyPrice * 100m, MidpointRounding.AwayFromZero);

        async Task<Product?> Lookup()
        {
            try
            {
                var r = await ExecRaw("ReadProductByHandle", () => _client.Products.ReadProductByHandle(
                    new MaxioAdvancedBilling.Requests.Products.ReadProductByHandleRequest { ApiHandle = handle },
                    cancellationToken: ct));
                return r.Product;
            }
            catch (BillingException ex) when (IsNotFound(ex))
            {
                return null;
            }
        }

        Product? product;
        try
        {
            var r = await Exec<ProductResponse, CreateProductError>("CreateProduct",
                () => _client.Products.CreateProduct(
                    new MaxioAdvancedBilling.Requests.Products.CreateProductRequest
                    {
                        ProductFamilyId = familyId.ToString(CultureInfo.InvariantCulture),
                        Body = new CreateOrUpdateProductRequest
                        {
                            Product = new CreateOrUpdateProduct
                            {
                                Name = name,
                                Handle = handle,
                                Description = name,
                                PriceInCents = cents,
                                Interval = 1,
                                IntervalUnit = IntervalUnit.Month,
                                RequireCreditCard = false
                            }
                        }
                    }, cancellationToken: ct),
                e => ListErr(() => e.TryGetErrorListResponse1(out var l) ? l : null, e));
            product = r.Product;
        }
        catch (BillingException ex) when (ex.StatusCode >= 500)
        {
            // Unknown outcome: look the product up by the handle we sent.
            product = await Lookup();
            if (product is null) throw;
        }
        if (product?.Id is null || product.Handle is null)
            throw new BillingException(502, "The billing system returned no plan.");
        return new PlanInfo(product.Id.Value, product.Handle, product.Name ?? name, product.Description,
            product.PriceInCents ?? cents, product.Interval ?? 1, product.IntervalUnit?.Value ?? "month", brandName,
            product.DefaultProductPricePointId ?? 0);
    }

    public record CurrencyPriceInfo(int PlanId, string Currency, string? FormattedPrice, int Price);

    public async Task<CurrencyPriceInfo> AddCurrencyPriceAsync(int planId, string currency, int price, CancellationToken ct)
    {
        var plan = await RequireOfferedPlanByIdAsync(planId, ct);
        if (plan.DefaultPricePointId == 0)
            throw new BillingException(409, "The plan has no price point to price in another currency.");

        async Task<IReadOnlyList<CurrencyPrice>> ExistingPrices()
        {
            var pp = await ExecRaw("ReadProductPricePoint", () => _client.ProductPricePoints.ReadProductPricePoint(
                new MaxioAdvancedBilling.Requests.ProductPricePoints.ReadProductPricePointRequest
                {
                    ProductId = planId,
                    PricePointId = plan.DefaultPricePointId,
                    CurrencyPrices = true
                }, cancellationToken: ct));
            return pp.PricePoint?.CurrencyPrices ?? [];
        }

        var already = (await ExistingPrices()).FirstOrDefault(c =>
            string.Equals(c.Currency, currency, StringComparison.OrdinalIgnoreCase));
        if (already is not null)
            throw new BillingException(409, $"The plan already has a price in {currency}.");

        var created = await Exec<CurrencyPricesResponse, CreateProductCurrencyPricesError>("CreateProductCurrencyPrices",
            () => _client.ProductPricePoints.CreateProductCurrencyPrices(
                new MaxioAdvancedBilling.Requests.ProductPricePoints.CreateProductCurrencyPricesOperationRequest
                {
                    ProductPricePointId = plan.DefaultPricePointId,
                    Body = new CreateProductCurrencyPricesRequest
                    {
                        CurrencyPrices =
                        [
                            new CreateProductCurrencyPrice { Currency = currency, Price = price, Role = CurrencyPriceRole.Baseline }
                        ]
                    }
                }, cancellationToken: ct),
            e => e.TryGetErrorArrayMapResponse1(out var m) && m.Errors is not null
                ? string.Join("; ", m.Errors.Select(kv => $"{kv.Key}: {string.Join(", ", ErrorTexts(kv.Value).Distinct())}"))
                : RawText(e));

        var made = created.CurrencyPrices?.FirstOrDefault(c => string.Equals(c.Currency, currency, StringComparison.OrdinalIgnoreCase));
        if (made is null)
            throw new BillingException(502, "The billing system did not confirm the currency price.");
        return new CurrencyPriceInfo(planId, made.Currency ?? currency, made.FormattedPrice, price);
    }

    // ---------------------------------------------------------------- staff: next billing date

    public async Task<SubscriptionInfo> SetNextBillingDateAsync(int id, DateOnly date, CancellationToken ct)
    {
        var current = await ReadAnyAsync(id, ct);
        RequireState(current, "change the billing date of", "active", "trialing", "past_due", "unpaid", "soft_failure");
        var at = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        Subscription result;
        try
        {
            var r = await Exec<SubscriptionResponse, UpdateSubscriptionError>("UpdateSubscription",
                () => _client.Subscriptions.UpdateSubscription(
                    new MaxioAdvancedBilling.Requests.Subscriptions.UpdateSubscriptionOperationRequest
                    {
                        SubscriptionId = id,
                        Body = new UpdateSubscriptionRequest
                        {
                            Subscription = new UpdateSubscription { NextBillingAt = at }
                        }
                    }, cancellationToken: ct),
                e => ListErr(() => e.TryGetErrorListResponse1(out var l) ? l : null, e));
            result = r.Subscription ?? throw new BillingException(502, "No subscription returned.");
        }
        catch (BillingException ex) when (ex.StatusCode >= 500)
        {
            result = await SettleAsync(id, s => s.CurrentPeriodEndsAt is { } e && DateOnly.FromDateTime(e.UtcDateTime) == date, ex, ct);
        }

        // The update response carries no next_billing_at: verify by re-reading the period end.
        var verified = await ReadAnyAsync(id, ct);
        if (verified.CurrentPeriodEndsAt is not { } ends || DateOnly.FromDateTime(ends.UtcDateTime) != date)
            throw new BillingException(502, "The billing system did not apply the new billing date.");
        return await ToInfoAsync(verified, ct);
    }
}
