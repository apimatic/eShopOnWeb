using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MaxioRawError = MaxioAdvancedBilling.Core.ErrorResponse.RawError;
using MaxioRequests = MaxioAdvancedBilling.Requests;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Subscription billing over Maxio Advanced Billing (system of record).
/// Subscribe order: validate plan against the offered list -> claim (PK on user id) -> Maxio calls -> record result.
/// </summary>
public class MaxioSubscriptionBilling : ISubscriptionBilling
{
    private const int PlanPageSize = 200;      // the endpoint maximum
    private const int MaxPlanPages = 10;       // provider-independent cap on the plan walk
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);      // whole handler, every SDK call included
    private static readonly TimeSpan ReconcileBudget = TimeSpan.FromSeconds(10); // re-read that settles an unknown write
    private static readonly TimeSpan PendingLease = TimeSpan.FromMinutes(2);     // a Pending claim older than this is treated as Unknown

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _settings;
    private readonly CatalogContext _db;
    private readonly TimeProvider _clock;
    private readonly ILogger<MaxioSubscriptionBilling> _logger;

    public MaxioSubscriptionBilling(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> settings,
        CatalogContext db,
        ILogger<MaxioSubscriptionBilling> logger,
        TimeProvider? clock = null)
    {
        _client = client;
        _settings = settings.Value;
        _db = db;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    private static string SubscriptionReference(string userId) => "eshop-sub-" + userId;

    // ---------------------------------------------------------------- plans

    public async Task<SubscriptionPlanList> GetPlansAsync(CancellationToken cancellationToken)
    {
        using var budget = StartBudget(cancellationToken);
        return await GetPlansCoreAsync(new Ctx(cancellationToken, budget.Token));
    }

    private async Task<SubscriptionPlanList> GetPlansCoreAsync(Ctx ctx)
    {
        var plans = new List<SubscriptionPlan>();
        var truncated = false;
        for (var page = 1; page <= MaxPlanPages; page++)
        {
            var current = page;
            var batch = await ExecuteAsync(ctx, write: false, "ListProductsForProductFamily", ct =>
                _client.ProductFamilies.ListProductsForProductFamily(
                    new MaxioRequests.ProductFamilies.ListProductsForProductFamilyRequest
                    {
                        ProductFamilyId = "handle:" + _settings.ProductFamilyHandle,
                        Page = current,
                        PerPage = PlanPageSize
                    },
                    cancellationToken: ct));

            foreach (var item in batch)
            {
                var p = item.Product;
                if (p.ArchivedAt != null || string.IsNullOrEmpty(p.Handle)) continue;
                plans.Add(new SubscriptionPlan(p.Handle, p.Name ?? p.Handle, p.Description, p.PriceInCents ?? 0,
                    p.Interval, p.IntervalUnit?.Value));
            }

            if (batch.Count < PlanPageSize) break;
            if (page == MaxPlanPages) truncated = true;
        }

        if (truncated)
            _logger.LogWarning("Plan list for the configured product family was cut at {Pages} pages.", MaxPlanPages);
        return new SubscriptionPlanList(plans, truncated);
    }

    // ------------------------------------------------------------ subscribe

    public async Task<SubscribeResult> SubscribeAsync(string userId, string email, string planHandle, CancellationToken cancellationToken)
    {
        using var budget = StartBudget(cancellationToken);
        var ctx = new Ctx(cancellationToken, budget.Token);

        // Cross-operation invariant: the handle must be one of the plans this application offers.
        var offered = await GetPlansCoreAsync(ctx);
        var plan = offered.Plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.Ordinal));
        if (plan == null)
        {
            if (offered.IsTruncated)
                throw new BillingException(BillingFailure.Unavailable, "The plan list is incomplete; cannot validate the plan right now.");
            throw new BillingException(BillingFailure.InvalidPlan, "Unknown plan.");
        }

        // Claim: a primary-key insert; the store refuses the second caller before anything reaches Maxio.
        var claim = new SubscriptionClaim(userId, plan.Handle, _clock.GetUtcNow());
        try
        {
            _db.SubscriptionClaims.Add(claim);
            await _db.SaveChangesAsync(ctx.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The store refuses a duplicate key differently per provider (DbUpdateException on SQL Server, a raw
            // ArgumentException on the in-memory provider), so confirm by looking for the existing row.
            _db.Entry(claim).State = EntityState.Detached;
            var existing = await _db.SubscriptionClaims.AsNoTracking().FirstOrDefaultAsync(c => c.UserId == userId, ctx.Token);
            if (existing == null) throw; // not a duplicate: a genuine store failure
            existing = await _db.SubscriptionClaims.FirstAsync(c => c.UserId == userId, ctx.Token);
            _logger.LogInformation(ex, "Subscribe for user {UserId} met an existing claim in state {State}.", userId, existing.State);
            return await HandleExistingClaimAsync(existing, plan, email, ctx);
        }

        return await RunClaimedAsync(claim, email, ctx);
    }

    private async Task<SubscribeResult> HandleExistingClaimAsync(SubscriptionClaim existing, SubscriptionPlan plan, string email, Ctx ctx)
    {
        var userId = existing.UserId;
        if (existing.State == SubscriptionClaimState.Completed)
        {
            var current = await TryFindSubscriptionAsync(userId, ctx.Token);
            return new SubscribeResult(SubscribeOutcome.AlreadySubscribed, current);
        }

        var age = _clock.GetUtcNow() - existing.UpdatedAt;
        if (existing.State == SubscriptionClaimState.Pending && age < PendingLease)
            throw new BillingException(BillingFailure.InProgress, "A subscription request for this account is already in progress.");

        // Unknown (or an abandoned Pending): settle against Maxio first.
        _logger.LogWarning("Reconciling {State} claim for user {UserId} against Maxio.", existing.State, userId);
        var found = await ReconcileSubscriptionAsync(userId);
        if (found != null)
        {
            await CompleteAsync(existing, found);
            return new SubscribeResult(SubscribeOutcome.AlreadySubscribed, Map(found));
        }

        // Nothing at Maxio: take the claim over (concurrency token: only one recoverer wins) and re-drive.
        existing.MarkPending(plan.Handle, _clock.GetUtcNow());
        try
        {
            await _db.SaveChangesAsync(ctx.Token);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BillingException(BillingFailure.InProgress, "A subscription request for this account is already in progress.");
        }
        return await RunClaimedAsync(existing, email, ctx);
    }

    private async Task<SubscribeResult> RunClaimedAsync(SubscriptionClaim claim, string email, Ctx ctx)
    {
        var userId = claim.UserId;
        try
        {
            var customerId = claim.CustomerId ?? await EnsureCustomerAsync(userId, email, ctx);
            if (claim.CustomerId != customerId)
            {
                claim.SetCustomer(customerId, _clock.GetUtcNow());
                await _db.SaveChangesAsync(ctx.Token);
            }

            var response = await ExecuteAsync(ctx, write: true, "CreateSubscription", ct =>
                _client.Subscriptions.CreateSubscription(
                    new MaxioRequests.Subscriptions.CreateSubscriptionOperationRequest
                    {
                        Body = new CreateSubscriptionRequest
                        {
                            Subscription = new CreateSubscription
                            {
                                ProductHandle = claim.PlanHandle,
                                CustomerId = customerId,
                                // No card is captured at signup: bill by invoice (remittance) rather than automatic card collection.
                                PaymentCollectionMethod = MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance,
                                Reference = SubscriptionReference(userId)
                            }
                        }
                    },
                    cancellationToken: ct));

            var created = response.Subscription
                ?? throw new BillingException(BillingFailure.Indeterminate, "Maxio returned no subscription.");
            await CompleteAsync(claim, created);
            _logger.LogInformation("User {UserId} subscribed to {Plan} (Maxio subscription {SubscriptionId}).",
                userId, claim.PlanHandle, created.Id);
            return new SubscribeResult(SubscribeOutcome.Created, Map(created));
        }
        catch (BillingException ex) when (ex.Failure == BillingFailure.Indeterminate)
        {
            // The write may have landed: settle by re-reading Maxio by the reference we sent.
            var found = await ReconcileSubscriptionAsync(userId);
            if (found != null)
            {
                await CompleteAsync(claim, found);
                return new SubscribeResult(SubscribeOutcome.Created, Map(found));
            }
            await SetUnknownAsync(claim);
            _logger.LogWarning(ex, "Subscribe for user {UserId} left unconfirmed; will be reconciled on the next request.", userId);
            return new SubscribeResult(SubscribeOutcome.PendingConfirmation, null);
        }
        catch (BillingException)
        {
            // Definitive refusal: nothing was written, release the claim so a corrected retry can proceed.
            await ReleaseAsync(claim);
            throw;
        }
        catch (OperationCanceledException)
        {
            await SetUnknownAsync(claim);
            throw;
        }
    }

    private async Task<int> EnsureCustomerAsync(string userId, string email, Ctx ctx)
    {
        var existing = await TryReadCustomerIdAsync(userId, ctx);
        if (existing != null) return existing.Value;

        // First/last name are required by Maxio; derive them from the e-mail local part (no name data on the identity user).
        var local = email.Split('@')[0];
        try
        {
            var created = await ExecuteAsync(ctx, write: true, "CreateCustomer", ct =>
                _client.Customers.CreateCustomer(
                    new MaxioRequests.Customers.CreateCustomerOperationRequest
                    {
                        Body = new CreateCustomerRequest
                        {
                            Customer = new CreateCustomer
                            {
                                FirstName = string.IsNullOrWhiteSpace(local) ? "Shopper" : local,
                                LastName = "eShopOnWeb",
                                Email = email,
                                Reference = userId
                            }
                        }
                    },
                    cancellationToken: ct));
            return created.Customer.Id
                ?? throw new BillingException(BillingFailure.Indeterminate, "Maxio returned a customer without an id.");
        }
        catch (BillingException ex) when (ex.Failure == BillingFailure.Indeterminate)
        {
            using var rc = new CancellationTokenSource(ReconcileBudget);
            var found = await TryReadCustomerIdAsync(userId, new Ctx(default, rc.Token));
            if (found != null) return found.Value;
            throw;
        }
    }

    // ------------------------------------------------------- my subscriptions

    public async Task<IReadOnlyList<SubscriptionSummary>> GetSubscriptionsAsync(string userId, CancellationToken cancellationToken)
    {
        using var budget = StartBudget(cancellationToken);
        var ctx = new Ctx(cancellationToken, budget.Token);

        var claim = await _db.SubscriptionClaims.FirstOrDefaultAsync(c => c.UserId == userId, ctx.Token);
        if (claim != null && claim.State != SubscriptionClaimState.Completed &&
            (claim.State == SubscriptionClaimState.Unknown || _clock.GetUtcNow() - claim.UpdatedAt >= PendingLease))
        {
            try
            {
                var found = await ReconcileSubscriptionAsync(userId);
                if (found != null) await CompleteAsync(claim, found);
            }
            catch (BillingException ex)
            {
                _logger.LogWarning(ex, "Reconciliation for user {UserId} failed; listing from Maxio anyway.", userId);
            }
        }

        var customerId = claim?.CustomerId ?? await TryReadCustomerIdAsync(userId, ctx);
        if (customerId == null) return Array.Empty<SubscriptionSummary>();

        var list = await ExecuteAsync(ctx, write: false, "ListCustomerSubscriptions", ct =>
            _client.Customers.ListCustomerSubscriptions(
                new MaxioRequests.Customers.ListCustomerSubscriptionsRequest { CustomerId = customerId.Value },
                cancellationToken: ct));

        return list.Where(r => r.Subscription != null).Select(r => Map(r.Subscription!)).ToList();
    }

    // ------------------------------------------------------------- lookups

    private async Task<int?> TryReadCustomerIdAsync(string userId, Ctx ctx)
    {
        try
        {
            var r = await ExecuteAsync(ctx, write: false, "ReadCustomerByReference", ct =>
                _client.Customers.ReadCustomerByReference(
                    new MaxioRequests.Customers.ReadCustomerByReferenceRequest { Reference = userId },
                    cancellationToken: ct));
            return r.Customer.Id;
        }
        catch (BillingException ex) when (ex.Failure == BillingFailure.NotFound)
        {
            return null;
        }
    }

    private async Task<Subscription?> FindSubscriptionAsync(string userId, Ctx ctx)
    {
        try
        {
            var r = await ExecuteAsync(ctx, write: false, "FindSubscription", ct =>
                _client.Subscriptions.FindSubscription(
                    new MaxioRequests.Subscriptions.FindSubscriptionRequest { Reference = SubscriptionReference(userId) },
                    cancellationToken: ct));
            return r.Subscription;
        }
        catch (BillingException ex) when (ex.Failure == BillingFailure.NotFound)
        {
            return null;
        }
    }

    private async Task<SubscriptionSummary?> TryFindSubscriptionAsync(string userId, CancellationToken token)
    {
        var s = await FindSubscriptionAsync(userId, new Ctx(token, token));
        return s == null ? null : Map(s);
    }

    /// <summary>Re-read on its own short budget: the call budget may be the very thing that expired.</summary>
    private async Task<Subscription?> ReconcileSubscriptionAsync(string userId)
    {
        using var rc = new CancellationTokenSource(ReconcileBudget);
        try
        {
            return await FindSubscriptionAsync(userId, new Ctx(default, rc.Token));
        }
        catch (BillingException ex)
        {
            _logger.LogWarning(ex, "Reconciliation read for user {UserId} failed; outcome stays unknown.", userId);
            return null;
        }
    }

    // ------------------------------------------------------- claim bookkeeping

    private async Task CompleteAsync(SubscriptionClaim claim, Subscription sub)
    {
        claim.MarkCompleted(sub.Customer?.Id, sub.Id, _clock.GetUtcNow());
        await SaveQuietlyAsync("complete");
    }

    private async Task SetUnknownAsync(SubscriptionClaim claim)
    {
        claim.MarkUnknown(_clock.GetUtcNow());
        await SaveQuietlyAsync("mark unknown");
    }

    private async Task ReleaseAsync(SubscriptionClaim claim)
    {
        _db.SubscriptionClaims.Remove(claim);
        await SaveQuietlyAsync("release");
    }

    /// <summary>Bookkeeping after the provider acted must not turn a settled outcome into an error; Maxio stays authoritative.</summary>
    private async Task SaveQuietlyAsync(string what)
    {
        try
        {
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Could not {Action} the subscription claim; Maxio remains the source of truth.", what);
        }
    }

    // ------------------------------------------------------------- mapping

    private static SubscriptionSummary Map(Subscription s) => new(
        s.Id?.ToString() ?? string.Empty,
        s.Product?.Handle,
        s.Product?.Name,
        s.ProductPriceInCents ?? s.Product?.PriceInCents,
        s.State?.Value,
        s.NextAssessmentAt,
        s.CreatedAt);

    // ------------------------------------------------- the one SDK boundary

    private readonly record struct Ctx(CancellationToken Caller, CancellationToken Token);

    private CancellationTokenSource StartBudget(CancellationToken caller)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(caller);
        cts.CancelAfter(CallBudget);
        return cts;
    }

    /// <summary>
    /// Every SDK call goes through here: one catch ladder, one mapping of provider failures to <see cref="BillingException"/>.
    /// For a write, a failure that leaves the outcome unknown becomes <see cref="BillingFailure.Indeterminate"/>.
    /// </summary>
    private async Task<T> ExecuteAsync<T>(Ctx ctx, bool write, string operation, Func<CancellationToken, Task<T>> call)
    {
        try
        {
            return await call(ctx.Token);
        }
        catch (OperationCanceledException) when (!ctx.Caller.IsCancellationRequested)
        {
            // our own budget expired
            throw Fail(operation, write ? BillingFailure.Indeterminate : BillingFailure.Unavailable, null, "call budget exceeded", null);
        }
        catch (ApiException<CreateSubscriptionError> ex)
        {
            var detail = ex.Error.TryGetErrorListResponse1(out var list)
                ? string.Join("; ", list.Errors)
                : ex.Error.TryGetRawError(out var raw) ? Truncate(raw.ReadAsString()) : null;
            throw FromStatus(operation, write, ex, detail);
        }
        catch (ApiException<CreateCustomerError> ex)
        {
            var detail = ex.Error.TryGetRawError(out var raw) ? Truncate(raw.ReadAsString()) : "validation failed";
            throw FromStatus(operation, write, ex, detail);
        }
        catch (ApiException<MaxioRawError> ex)
        {
            throw FromStatus(operation, write, ex, Truncate(ex.Error.ReadAsString()));
        }
        catch (ResponseDeserializationException ex)
        {
            var status = (int)ex.StatusCode;
            if (status is >= 200 and < 300)
                throw Fail(operation, write ? BillingFailure.Indeterminate : BillingFailure.Unavailable, status, "unreadable response", ex);
            throw FromStatus(operation, write, ex, "unreadable error body");
        }
        catch (ApiException ex)
        {
            throw FromStatus(operation, write, ex, null);
        }
        catch (SdkConnectionException ex)
        {
            throw Fail(operation, write ? BillingFailure.Indeterminate : BillingFailure.Unavailable, null, "provider unreachable or timed out", ex);
        }
        catch (AuthSchemeException ex)
        {
            throw Fail(operation, BillingFailure.Unavailable, null, "credentials could not be applied", ex);
        }
    }

    private BillingException FromStatus(string operation, bool write, ApiException ex, string? detail)
    {
        var status = (int)ex.StatusCode;
        var failure = status switch
        {
            404 => BillingFailure.NotFound,
            401 or 403 or 429 => BillingFailure.Unavailable, // our credentials / our quota, not the caller's fault
            >= 400 and < 500 => BillingFailure.Rejected,
            >= 500 => write ? BillingFailure.Indeterminate : BillingFailure.Unavailable,
            _ => BillingFailure.Unavailable
        };
        return Fail(operation, failure, status, detail ?? $"HTTP {status}", ex);
    }

    private BillingException Fail(string operation, BillingFailure failure, int? status, string detail, Exception? inner)
    {
        // Provider detail goes to the logs only; the exception message is caller-safe.
        _logger.Log(failure == BillingFailure.NotFound ? LogLevel.Information : LogLevel.Error,
            inner, "Maxio {Operation} failed: {Failure} (HTTP {Status}): {Detail}", operation, failure, status, detail);
        var message = failure switch
        {
            BillingFailure.Rejected => $"The billing provider rejected the request (HTTP {status}).",
            BillingFailure.NotFound => "Not found.",
            _ => "The billing provider is temporarily unavailable."
        };
        return new BillingException(failure, message, status, inner);
    }

    private static string Truncate(string s) => s.Length <= 500 ? s : s[..500];
}
