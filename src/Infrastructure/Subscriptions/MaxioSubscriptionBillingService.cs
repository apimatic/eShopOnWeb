using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions;

/// <summary>
/// Subscription billing on top of Maxio Advanced Billing. Every write follows
/// claim (app store, primary key) -> SDK call -> record result, and a write whose
/// connection fails is settled by re-reading Maxio by the deterministic reference.
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const int PlansPerPage = 200;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly CatalogContext _db;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        CatalogContext db,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _db = db;
        _settings = settings.Value;
        _logger = logger;
    }

    private sealed record Budget(CancellationToken Token, CancellationToken Caller);

    private CancellationTokenSource NewBudget(CancellationToken caller, out Budget budget)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(caller);
        cts.CancelAfter(TimeSpan.FromSeconds(_settings.RequestBudgetSeconds));
        budget = new Budget(cts.Token, caller);
        return cts;
    }

    // ---------------------------------------------------------------- plans

    public async Task<SubscriptionPlanList> ListPlansAsync(CancellationToken ct)
    {
        using var cts = NewBudget(ct, out var budget);
        return await ListPlansCoreAsync(budget);
    }

    private async Task<SubscriptionPlanList> ListPlansCoreAsync(Budget budget)
    {
        var plans = new List<SubscriptionPlan>();
        var complete = false;
        for (var page = 1; page <= Math.Max(1, _settings.MaxPlanPages); page++)
        {
            var p = page;
            var batch = await Sdk(false, budget, t => _client.ProductFamilies.ListProductsForProductFamily(
                new ListProductsForProductFamilyRequest
                {
                    ProductFamilyId = "handle:" + _settings.ProductFamilyHandle,
                    Page = p,
                    PerPage = PlansPerPage
                }, cancellationToken: t));

            foreach (var item in batch)
            {
                var product = item.Product;
                if (product is null || product.ArchivedAt is not null || string.IsNullOrWhiteSpace(product.Handle))
                    continue;
                plans.Add(new SubscriptionPlan(
                    product.Handle!,
                    product.Name ?? product.Handle!,
                    product.Description,
                    (product.PriceInCents ?? 0) / 100m,
                    product.Interval ?? 1,
                    product.IntervalUnit?.Value ?? "month",
                    (product.TrialInterval ?? 0) > 0,
                    (product.InitialChargeInCents ?? 0) / 100m,
                    product.RequireCreditCard ?? false));
            }

            if (batch.Count < PlansPerPage) { complete = true; break; }
        }

        if (!complete)
            _logger.LogWarning("Plan listing for the configured product family was truncated at {Pages} pages", _settings.MaxPlanPages);
        return new SubscriptionPlanList(plans, complete);
    }

    // ------------------------------------------------------------ subscribe

    public async Task<SubscribeResult> SubscribeAsync(BillingUser user, string? planHandle, CancellationToken ct)
    {
        using var cts = NewBudget(ct, out var budget);
        try
        {
            var wanted = string.IsNullOrWhiteSpace(planHandle) ? _settings.DefaultPlanHandle : planHandle;
            if (string.IsNullOrWhiteSpace(wanted))
                return new SubscribeResult(SubscribeOutcome.PlanNotAvailable, Message: "A plan handle is required.");

            // Invariant: the plan must be one Maxio lists for the configured family.
            var listing = await ListPlansCoreAsync(budget);
            var plan = listing.Plans.FirstOrDefault(p => string.Equals(p.Handle, wanted, StringComparison.Ordinal));
            if (plan is null)
            {
                return listing.IsComplete
                    ? new SubscribeResult(SubscribeOutcome.PlanNotAvailable, Message: $"Plan '{wanted}' is not available.")
                    : new SubscribeResult(SubscribeOutcome.ProviderUnavailable, Message: "The plan list could not be fully loaded; try again.");
            }

            var customerId = await EnsureCustomerAsync(user, budget);
            if (customerId is null)
                return new SubscribeResult(SubscribeOutcome.InProgress, Message: "Your account is being set up; retry shortly.");

            return await EnsureSubscriptionAsync(user, plan, customerId.Value, budget);
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Subscribe for user {UserId} failed: {Message} (status {Status}, unknown outcome {Unknown})",
                user.UserId, ex.Message, ex.StatusCode, ex.OutcomeUnknown);
            return ex.IsCallerRefusal
                ? new SubscribeResult(SubscribeOutcome.Refused, Message: ex.Message)
                : new SubscribeResult(SubscribeOutcome.ProviderUnavailable,
                    Message: ex.OutcomeUnknown
                        ? "The billing system did not confirm the request. Check your subscriptions shortly; do not resubmit."
                        : ex.Message);
        }
    }

    private bool IsStale(DateTime claimedAtUtc) =>
        DateTime.UtcNow - claimedAtUtc > TimeSpan.FromSeconds(_settings.StaleClaimSeconds);

    /// <returns>customer id, or null when another caller is creating it right now.</returns>
    private async Task<int?> EnsureCustomerAsync(BillingUser user, Budget budget)
    {
        var claim = new MaxioCustomerLink
        {
            UserId = user.UserId,
            CustomerReference = "eshop-user-" + user.UserId,
            Status = LinkStatus.Claimed,
            ClaimedAtUtc = DateTime.UtcNow
        };

        // claim: the primary key on UserId refuses the second caller
        try
        {
            _db.CustomerLinks.Add(claim);
            await _db.SaveChangesAsync(budget.Caller);
        }
        catch (Exception ex) when (ex is DbUpdateException or ArgumentException)
        {
            _db.Entry(claim).State = EntityState.Detached;
            var existing = await _db.CustomerLinks.FindAsync(new object[] { user.UserId }, budget.Caller);
            if (existing is null) return null;
            if (existing.MaxioCustomerId is not null) return existing.MaxioCustomerId;
            if (existing.Status == LinkStatus.Claimed && !IsStale(existing.ClaimedAtUtc)) return null;

            // Unknown or abandoned claim: settle by looking Maxio up by reference.
            var found = await FindCustomerIdAsync(existing.CustomerReference, budget);
            if (found is not null)
            {
                existing.MaxioCustomerId = found;
                existing.Status = LinkStatus.Created;
                await _db.SaveChangesAsync(budget.Caller);
                return found;
            }
            // Maxio never saw it: take the claim over (concurrency token on ClaimedAtUtc arbitrates).
            try
            {
                existing.Status = LinkStatus.Claimed;
                existing.ClaimedAtUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync(budget.Caller);
            }
            catch (DbUpdateConcurrencyException)
            {
                return null;
            }
            claim = existing;
        }

        return await CompleteClaimedWriteAsync(
            claim.CustomerReference,
            () => FindCustomerIdAsync(claim.CustomerReference, budget),
            async () =>
            {
                var name = user.Email?.Split('@')[0] ?? user.UserName ?? "Customer";
                var response = await Sdk(true, budget, t => _client.Customers.CreateCustomer(
                    new CreateCustomerOperationRequest
                    {
                        Body = new CreateCustomerRequest
                        {
                            Customer = new CreateCustomer
                            {
                                FirstName = string.IsNullOrWhiteSpace(name) ? "Customer" : name,
                                LastName = "eShopOnWeb",
                                Email = user.Email ?? (user.UserId + "@users.invalid"),
                                Reference = claim.CustomerReference
                            }
                        }
                    }, cancellationToken: t));
                return response.Customer?.Id;
            },
            id => { claim.MaxioCustomerId = id; claim.Status = LinkStatus.Created; return _db.SaveChangesAsync(budget.Caller); },
            () => { claim.Status = LinkStatus.Unknown; return _db.SaveChangesAsync(budget.Caller); },
            () => { _db.CustomerLinks.Remove(claim); return _db.SaveChangesAsync(budget.Caller); },
            budget);
    }

    private async Task<SubscribeResult> EnsureSubscriptionAsync(BillingUser user, SubscriptionPlan plan, int customerId, Budget budget)
    {
        var claim = new MaxioSubscriptionLink
        {
            UserId = user.UserId,
            PlanHandle = plan.Handle,
            SubscriptionReference = $"eshop-sub-{user.UserId}-{plan.Handle}",
            Status = LinkStatus.Claimed,
            ClaimedAtUtc = DateTime.UtcNow
        };

        // claim: the composite primary key (UserId, PlanHandle) refuses the second caller
        try
        {
            _db.SubscriptionLinks.Add(claim);
            await _db.SaveChangesAsync(budget.Caller);
        }
        catch (Exception ex) when (ex is DbUpdateException or ArgumentException)
        {
            _db.Entry(claim).State = EntityState.Detached;
            var existing = await _db.SubscriptionLinks.FindAsync(new object[] { user.UserId, plan.Handle }, budget.Caller);
            if (existing is null)
                return new SubscribeResult(SubscribeOutcome.InProgress, Message: "Subscription is being created; retry shortly.");

            if (existing.MaxioSubscriptionId is null)
            {
                if (existing.Status == LinkStatus.Claimed && !IsStale(existing.ClaimedAtUtc))
                    return new SubscribeResult(SubscribeOutcome.InProgress, Message: "Subscription is being created; retry shortly.");

                var found = await FindSubscriptionAsync(existing.SubscriptionReference, budget);
                if (found?.Id is not null)
                {
                    existing.MaxioSubscriptionId = found.Id;
                    existing.Status = LinkStatus.Created;
                    await _db.SaveChangesAsync(budget.Caller);
                }
                else
                {
                    try
                    {
                        existing.Status = LinkStatus.Claimed;
                        existing.ClaimedAtUtc = DateTime.UtcNow;
                        await _db.SaveChangesAsync(budget.Caller);
                    }
                    catch (DbUpdateConcurrencyException)
                    {
                        return new SubscribeResult(SubscribeOutcome.InProgress, Message: "Subscription is being created; retry shortly.");
                    }
                    return await CreateSubscriptionAsync(existing, plan, customerId, budget);
                }
            }

            var view = await ReadViewAsync(existing, budget);
            return new SubscribeResult(SubscribeOutcome.Existing, view);
        }

        return await CreateSubscriptionAsync(claim, plan, customerId, budget);
    }

    private async Task<SubscribeResult> CreateSubscriptionAsync(MaxioSubscriptionLink claim, SubscriptionPlan plan, int customerId, Budget budget)
    {
        Subscription? created = null;
        var id = await CompleteClaimedWriteAsync(
            claim.SubscriptionReference,
            async () => (await FindSubscriptionAsync(claim.SubscriptionReference, budget))?.Id,
            async () =>
            {
                var response = await Sdk(true, budget, t => _client.Subscriptions.CreateSubscription(
                    new CreateSubscriptionOperationRequest
                    {
                        Body = new CreateSubscriptionRequest
                        {
                            Subscription = new CreateSubscription
                            {
                                ProductHandle = plan.Handle,
                                CustomerId = customerId,
                                Reference = claim.SubscriptionReference,
                                PaymentCollectionMethod = plan.RequiresPaymentMethod ? null : CollectionFor(_settings.NoPaymentMethodCollection)
                            }
                        }
                    }, cancellationToken: t));
                created = response.Subscription;
                return created?.Id;
            },
            sid => { claim.MaxioSubscriptionId = sid; claim.Status = LinkStatus.Created; return _db.SaveChangesAsync(budget.Caller); },
            () => { claim.Status = LinkStatus.Unknown; return _db.SaveChangesAsync(budget.Caller); },
            () => { _db.SubscriptionLinks.Remove(claim); return _db.SaveChangesAsync(budget.Caller); },
            budget);

        _logger.LogInformation("Subscription {SubscriptionId} recorded for user {UserId} on plan {Plan}", id, claim.UserId, claim.PlanHandle);
        var view = created is not null && created.Id == id
            ? ToView(claim.PlanHandle, created)
            : await ReadViewAsync(claim, budget);
        return new SubscribeResult(SubscribeOutcome.Created, view);
    }

    /// <summary>
    /// Runs a claimed write: look up by reference first (store loss / abandoned claim), then call;
    /// a failed write is re-read by reference in this method's own catch path before anything is reported.
    /// </summary>
    private async Task<int> CompleteClaimedWriteAsync(
        string reference,
        Func<Task<int?>> lookup,
        Func<Task<int?>> create,
        Func<int, Task> markCreated,
        Func<Task> markUnknown,
        Func<Task> release,
        Budget budget)
    {
        BillingProviderException failure;
        try
        {
            var already = await lookup();
            if (already is not null) { await markCreated(already.Value); return already.Value; }

            var id = await create();
            if (id is null)
                throw new BillingProviderException("Maxio returned no identifier for the created entity.", 200, outcomeUnknown: true);
            await markCreated(id.Value);
            return id.Value;
        }
        catch (BillingProviderException ex)
        {
            failure = ex;
        }

        // Settle the failed write before reporting anything.
        int? settled = null;
        try { settled = await lookup(); }
        catch (BillingProviderException) { /* could not re-read either */ }

        if (settled is not null)
        {
            await markCreated(settled.Value);
            return settled.Value;
        }

        if (failure.OutcomeUnknown)
        {
            _logger.LogWarning("Write {Reference} has an unknown outcome; marked for settlement on the next request", reference);
            await markUnknown();
        }
        else
        {
            await release();
        }
        throw failure;
    }

    // ------------------------------------------------------ my subscriptions

    public async Task<IReadOnlyList<SubscriptionView>> ListMySubscriptionsAsync(BillingUser user, CancellationToken ct)
    {
        using var cts = NewBudget(ct, out var budget);
        var rows = await _db.SubscriptionLinks.Where(l => l.UserId == user.UserId).ToListAsync(ct);
        var views = new List<SubscriptionView>();
        foreach (var row in rows.OrderBy(r => r.ClaimedAtUtc))
        {
            try
            {
                if (row.MaxioSubscriptionId is null && !(row.Status == LinkStatus.Claimed && !IsStale(row.ClaimedAtUtc)))
                {
                    // sweep: settle an unknown / abandoned row by its reference
                    var found = await FindSubscriptionAsync(row.SubscriptionReference, budget);
                    if (found?.Id is not null)
                    {
                        row.MaxioSubscriptionId = found.Id;
                        row.Status = LinkStatus.Created;
                        await _db.SaveChangesAsync(ct);
                    }
                }
                views.Add(row.MaxioSubscriptionId is null
                    ? new SubscriptionView(row.PlanHandle, null, null, row.Status == LinkStatus.Claimed ? "processing" : "unconfirmed", null, null, row.SubscriptionReference)
                    : await ReadViewAsync(row, budget));
            }
            catch (BillingProviderException ex)
            {
                _logger.LogWarning("Could not read subscription for user {UserId} plan {Plan}: {Message}", user.UserId, row.PlanHandle, ex.Message);
                views.Add(new SubscriptionView(row.PlanHandle, null, null, "unavailable", null, row.MaxioSubscriptionId, row.SubscriptionReference));
            }
        }
        return views;
    }

    // -------------------------------------------------------------- helpers

    private async Task<SubscriptionView> ReadViewAsync(MaxioSubscriptionLink link, Budget budget)
    {
        var id = link.MaxioSubscriptionId!.Value;
        var response = await Sdk(false, budget, t => _client.Subscriptions.ReadSubscription(
            new ReadSubscriptionRequest { SubscriptionId = id }, cancellationToken: t));
        if (response.Subscription is null)
            throw new BillingProviderException("Maxio returned no subscription.", 200);
        return ToView(link.PlanHandle, response.Subscription);
    }

    private static SubscriptionView ToView(string planHandle, Subscription s) => new(
        s.Product?.Handle ?? planHandle,
        s.Product?.Name,
        s.ProductPriceInCents is null ? null : s.ProductPriceInCents / 100m,
        s.State?.Value ?? "unknown",
        s.NextAssessmentAt ?? s.CurrentPeriodEndsAt,
        s.Id,
        s.Reference);

    private static CollectionMethod CollectionFor(string? name) =>
        string.Equals(name, "invoice", StringComparison.OrdinalIgnoreCase) ? CollectionMethod.Invoice : CollectionMethod.Remittance;

    private async Task<int?> FindCustomerIdAsync(string reference, Budget budget)
    {
        try
        {
            var response = await Sdk(false, budget, t => _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = reference }, cancellationToken: t));
            return response.Customer?.Id;
        }
        catch (BillingProviderException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
    }

    private async Task<Subscription?> FindSubscriptionAsync(string reference, Budget budget)
    {
        try
        {
            var response = await Sdk(false, budget, t => _client.Subscriptions.FindSubscription(
                new FindSubscriptionRequest { Reference = reference }, cancellationToken: t));
            return response.Subscription;
        }
        catch (BillingProviderException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
    }

    /// <summary>One catch ladder for every SDK call; converts SDK failures into <see cref="BillingProviderException"/>.</summary>
    private async Task<T> Sdk<T>(bool isWrite, Budget budget, Func<CancellationToken, Task<T>> call)
    {
        try
        {
            return await call(budget.Token);
        }
        catch (ApiException<CreateSubscriptionError> ex)
        {
            var detail = ex.Error.TryGetErrorListResponse1(out var list) ? string.Join("; ", list.Errors) : null;
            throw new BillingProviderException(
                $"Maxio rejected the request (HTTP {(int)ex.StatusCode})" + (detail is null ? "." : ": " + detail),
                (int)ex.StatusCode, inner: ex);
        }
        catch (ResponseDeserializationException ex)
        {
            var success = (int)ex.StatusCode is >= 200 and < 300;
            throw new BillingProviderException("Maxio returned a response that could not be processed.",
                (int)ex.StatusCode, outcomeUnknown: isWrite && success, inner: ex);
        }
        catch (ApiException ex)
        {
            throw new BillingProviderException($"Maxio rejected the request (HTTP {(int)ex.StatusCode}).", (int)ex.StatusCode, inner: ex);
        }
        catch (SdkConnectionException ex)
        {
            throw new BillingProviderException("The billing system could not be reached in time.", null, outcomeUnknown: isWrite, inner: ex);
        }
        catch (AuthSchemeException ex)
        {
            throw new BillingProviderException("The billing system credentials are not usable.", 401, inner: ex);
        }
        catch (OperationCanceledException ex) when (!budget.Caller.IsCancellationRequested)
        {
            throw new BillingProviderException("The billing request exceeded its time budget.", null, outcomeUnknown: isWrite, inner: ex);
        }
    }
}
