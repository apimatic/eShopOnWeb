using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Mail;
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
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Maxio Advanced Billing backed subscription service.
/// Order for every provider write: claim (own store, primary key) -> SDK call -> record result.
/// </summary>
public sealed class MaxioSubscriptionService : ISubscriptionService
{
    // --- constants (a fifth config key is not allowed) ---
    private const int PlanPageSize = 200;                                   // provider max
    private const int PlanPageCap = 5;                                      // = 1000 products
    private static readonly TimeSpan ReadBudget = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan SubscribeBudget = TimeSpan.FromSeconds(40);
    private static readonly TimeSpan ReconcileBudget = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StalePending = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan UnknownExpiry = TimeSpan.FromMinutes(30);
    // The sandbox rejects a card-less automatic-collection signup ("No payment method was on file for the balance"),
    // and this API takes no card details. Remittance (invoice) collection needs none. Product decision: see handoff notes.
    private static readonly CollectionMethod SignupCollectionMethod = CollectionMethod.Remittance;
    private const string LastNamePlaceholder = "eShopOnWeb User";

    private readonly MaxioAdvancedBillingClient _client;
    private readonly CatalogContext _db;
    private readonly ILogger<MaxioSubscriptionService> _logger;
    private readonly TimeProvider _time;
    private readonly string _familyHandle;

    public MaxioSubscriptionService(MaxioAdvancedBillingClient client, CatalogContext db,
        IOptions<MaxioOptions> options, ILogger<MaxioSubscriptionService> logger, TimeProvider time)
    {
        _client = client;
        _db = db;
        _logger = logger;
        _time = time;
        _familyHandle = options.Value.ProductFamilyHandle!;
    }

    // ------------------------------------------------------------------ plans

    public async Task<PlanList> ListPlansAsync(Guid correlationId, CancellationToken cancellationToken)
    {
        using var cts = Budget(cancellationToken, ReadBudget);
        return await ListPlansCoreAsync(correlationId, cts.Token, cancellationToken);
    }

    /// <summary>Walks the plan pages by hand (no Pageable) with a page cap; IsPartial tells the caller about a cut-short list.</summary>
    private async Task<PlanList> ListPlansCoreAsync(Guid corr, CancellationToken token, CancellationToken caller)
    {
        var plans = new List<PlanDto>();
        var partial = false;

        for (var page = 1; page <= PlanPageCap; page++)
        {
            var currentPage = page;
            var products = await Run("ListProductsForProductFamily", corr, write: false, token, caller,
                t => _client.ProductFamilies.ListProductsForProductFamily(new ListProductsForProductFamilyRequest
                {
                    ProductFamilyId = "handle:" + _familyHandle,
                    Page = currentPage,
                    PerPage = PlanPageSize
                }, cancellationToken: t));

            foreach (var response in products)
            {
                var p = response?.Product;
                if (p is null || p.ArchivedAt is not null || string.IsNullOrWhiteSpace(p.Handle)) continue;
                plans.Add(new PlanDto(p.Handle!, p.Name ?? p.Handle!, p.Description, p.PriceInCents, p.Interval, p.IntervalUnit?.Value));
            }

            if (products.Count < PlanPageSize) break;
            if (page == PlanPageCap)
            {
                partial = true;
                _logger.LogWarning("[{CorrelationId}] Maxio plan list hit the page cap ({Cap} pages); result is partial", corr, PlanPageCap);
            }
        }

        return new PlanList(plans, partial);
    }

    // ------------------------------------------------------------------ subscribe

    public async Task<SubscribeResult> SubscribeAsync(string userName, string? planHandle, Guid correlationId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
            throw new SubscriptionException(SubscriptionFailure.InvalidPlan, "planHandle is required.");

        var userKey = NormalizeUserKey(userName);
        var email = EmailFor(userKey)
            ?? throw new SubscriptionException(SubscriptionFailure.InvalidIdentity,
                "The signed-in user name is not usable as a billing customer email address.");

        using var cts = Budget(cancellationToken, SubscribeBudget);
        var token = cts.Token;

        // Invariant: the plan must be one this application offers (read before any claim or provider write).
        var offered = await ListPlansCoreAsync(correlationId, token, cancellationToken);
        var requested = planHandle.Trim();
        var plan = offered.Plans.FirstOrDefault(p => string.Equals(p.Handle, requested, StringComparison.Ordinal));
        if (plan is null)
        {
            if (offered.IsPartial)
                throw new SubscriptionException(SubscriptionFailure.Unavailable, "The plan list could not be fully loaded; try again later.");
            throw new SubscriptionException(SubscriptionFailure.UnknownPlan, "unknown_plan: the requested plan is not offered.");
        }

        var claimKey = $"{userKey}|{plan.Handle}";
        _logger.LogInformation("[{CorrelationId}] Subscribe accepted: claim {ClaimKey}", correlationId, claimKey);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var (mine, existing) = await TryClaimSubscriptionAsync(claimKey, cancellationToken);
            if (mine is not null)
            {
                _logger.LogInformation("[{CorrelationId}] Claim {ClaimKey}: new", correlationId, claimKey);
                return await ExecuteSubscribeAsync(userKey, email, plan, claimKey, correlationId, token, cancellationToken);
            }

            var age = _time.GetUtcNow() - existing!.ClaimedAt;
            switch (existing.Status)
            {
                case ClaimStatus.Completed:
                    {
                        var found = await FindExistingSubscriptionAsync(userKey, plan.Handle, correlationId, token, cancellationToken);
                        if (found is not null)
                        {
                            _logger.LogInformation("[{CorrelationId}] Claim {ClaimKey}: duplicate, replaying subscription {SubscriptionId}", correlationId, claimKey, found.Id);
                            return new SubscribeResult(SubscribeOutcome.Existing, Map(found));
                        }
                        // Provider no longer has it: the Completed claim is stale, release and retake it.
                        _logger.LogWarning("[{CorrelationId}] Claim {ClaimKey}: completed claim has no provider subscription, released", correlationId, claimKey);
                        await ReleaseClaimAsync(claimKey);
                        continue;
                    }
                case ClaimStatus.Pending when age < StalePending:
                    _logger.LogWarning("[{CorrelationId}] Claim {ClaimKey}: in progress", correlationId, claimKey);
                    throw new SubscriptionException(SubscriptionFailure.InProgress,
                        "A subscription request for this plan is already in progress.", retryAfter: TimeSpan.FromSeconds(2));
                default:
                    {
                        // Stale Pending or Unknown: settle against the provider.
                        var found = await FindExistingSubscriptionAsync(userKey, plan.Handle, correlationId, token, cancellationToken);
                        if (found is not null)
                        {
                            await CompleteClaimAsync(claimKey, found.Id);
                            _logger.LogInformation("[{CorrelationId}] Claim {ClaimKey}: reconciled to subscription {SubscriptionId}", correlationId, claimKey, found.Id);
                            return new SubscribeResult(SubscribeOutcome.Existing, Map(found));
                        }
                        if (existing.Status == ClaimStatus.Unknown && age < UnknownExpiry)
                        {
                            _logger.LogWarning("[{CorrelationId}] Claim {ClaimKey}: outcome still unknown", correlationId, claimKey);
                            return new SubscribeResult(SubscribeOutcome.Unknown, null);
                        }
                        _logger.LogWarning("[{CorrelationId}] Claim {ClaimKey}: stale claim without provider subscription, released", correlationId, claimKey);
                        await ReleaseClaimAsync(claimKey);
                        continue;
                    }
            }
        }

        throw new SubscriptionException(SubscriptionFailure.InProgress,
            "A subscription request for this plan is already in progress.", retryAfter: TimeSpan.FromSeconds(2));
    }

    private async Task<SubscribeResult> ExecuteSubscribeAsync(string userKey, string email, PlanDto plan, string claimKey,
        Guid corr, CancellationToken token, CancellationToken caller)
    {
        var writeStarted = false;
        int? customerId = null;
        var subRef = SubscriptionReference(userKey, plan.Handle);

        try
        {
            customerId = await EnsureCustomerAsync(userKey, email, corr, token, caller);

            // Pre-write provider check: a subscription already carrying our reference (covers a lost in-memory claim).
            var already = FindByReference(await ListSubscriptionsAsync(customerId.Value, corr, token, caller), subRef);
            if (already is not null)
            {
                await CompleteClaimAsync(claimKey, already.Id);
                _logger.LogInformation("[{CorrelationId}] Subscription {SubscriptionId} already existed at provider for {ClaimKey}", corr, already.Id, claimKey);
                return new SubscribeResult(SubscribeOutcome.Existing, Map(already));
            }

            writeStarted = true;
            var response = await Run("CreateSubscription", corr, write: true, token, caller,
                t => _client.Subscriptions.CreateSubscription(new CreateSubscriptionOperationRequest
                {
                    Body = new CreateSubscriptionRequest
                    {
                        Subscription = new MaxioAdvancedBilling.Models.CreateSubscription
                        {
                            ProductHandle = plan.Handle,
                            CustomerId = customerId.Value,
                            Reference = subRef,
                            PaymentCollectionMethod = SignupCollectionMethod
                        }
                    }
                }, cancellationToken: t));

            var created = response.Subscription;
            if (created is null)
                throw new SubscriptionException(SubscriptionFailure.BadGateway, "The billing provider returned an empty subscription.",
                    (int)HttpStatusCode.OK) { OutcomeUnknown = true };

            await CompleteClaimAsync(claimKey, created.Id);
            _logger.LogInformation("[{CorrelationId}] Subscribed {ClaimKey}: customer {CustomerId}, subscription {SubscriptionId}", corr, claimKey, customerId, created.Id);
            return new SubscribeResult(SubscribeOutcome.Created, Map(created));
        }
        catch (CustomerOutcomeUnknownException)
        {
            // No subscription write was attempted: free the subscription claim; the customer link stays Unknown.
            await ReleaseClaimAsync(claimKey);
            return new SubscribeResult(SubscribeOutcome.Unknown, null);
        }
        catch (SubscriptionException ex) when (writeStarted && ex.OutcomeUnknown)
        {
            return await ReconcileAfterUnknownSubscriptionWriteAsync(claimKey, customerId!.Value, subRef, ex, corr);
        }
        catch (SubscriptionException) when (writeStarted)
        {
            await ReleaseClaimAsync(claimKey);   // definite provider refusal
            throw;
        }
        catch (Exception) when (!writeStarted)
        {
            await ReleaseClaimAsync(claimKey);   // nothing was written for this subscription
            throw;
        }
        // Anything else after the write started (caller disconnect, unexpected): the claim stays Pending and is settled
        // by the stale-claim reconcile on the next request.
    }

    /// <summary>
    /// The CreateSubscription call failed after the provider may have acted: re-read by our reference with a fresh budget
    /// (the request's own token may be spent). Found -> Completed; not found after an answered 5xx -> released;
    /// otherwise the claim is recorded as Unknown and settled on the next request.
    /// </summary>
    private async Task<SubscribeResult> ReconcileAfterUnknownSubscriptionWriteAsync(string claimKey, int customerId, string subRef,
        SubscriptionException cause, Guid corr)
    {
        _logger.LogWarning("[{CorrelationId}] CreateSubscription outcome unknown ({Failure}); reconciling {ClaimKey}", corr, cause.Failure, claimKey);
        Subscription? found = null;
        var reread = false;
        try
        {
            using var cts = new CancellationTokenSource(ReconcileBudget);
            found = FindByReference(await ListSubscriptionsAsync(customerId, corr, cts.Token, CancellationToken.None), subRef);
            reread = true;
        }
        catch (SubscriptionException)
        {
            // the re-read failed too: still unknown
        }

        if (found is not null)
        {
            await CompleteClaimAsync(claimKey, found.Id);
            _logger.LogInformation("[{CorrelationId}] Reconcile found subscription {SubscriptionId} for {ClaimKey}", corr, found.Id, claimKey);
            return new SubscribeResult(SubscribeOutcome.Created, Map(found));
        }

        if (reread && cause.ProviderStatus is >= 500)
        {
            await ReleaseClaimAsync(claimKey);   // provider answered with an error and holds nothing for our reference
            throw new SubscriptionException(SubscriptionFailure.Unavailable, "The billing provider is temporarily unavailable.", cause.ProviderStatus);
        }

        await MarkClaimUnknownAsync(claimKey);
        _logger.LogWarning("[{CorrelationId}] Claim {ClaimKey} kept as Unknown", corr, claimKey);
        return new SubscribeResult(SubscribeOutcome.Unknown, null);
    }

    private async Task<Subscription?> FindExistingSubscriptionAsync(string userKey, string planHandle, Guid corr, CancellationToken token, CancellationToken caller)
    {
        var customerId = await ResolveCustomerIdAsync(userKey, corr, token, caller);
        if (customerId is null) return null;
        return FindByReference(await ListSubscriptionsAsync(customerId.Value, corr, token, caller), SubscriptionReference(userKey, planHandle));
    }

    // ------------------------------------------------------------------ customer

    private sealed class CustomerOutcomeUnknownException : Exception { }

    private async Task<int?> ResolveCustomerIdAsync(string userKey, Guid corr, CancellationToken token, CancellationToken caller)
    {
        var link = await _db.CustomerLinks.AsNoTracking().FirstOrDefaultAsync(l => l.UserKey == userKey, caller);
        if (link is { Status: ClaimStatus.Completed, MaxioCustomerId: { } id }) return id;
        return await ReadCustomerIdAsync(userKey, corr, token, caller);
    }

    /// <summary>ReadCustomerByReference; a missing reference (404) is "no customer", everything else is a failure.</summary>
    private async Task<int?> ReadCustomerIdAsync(string userKey, Guid corr, CancellationToken token, CancellationToken caller)
    {
        try
        {
            var response = await Run("ReadCustomerByReference", corr, write: false, token, caller,
                t => _client.Customers.ReadCustomerByReference(new ReadCustomerByReferenceRequest { Reference = CustomerReference(userKey) }, cancellationToken: t));
            return response.Customer?.Id
                ?? throw new SubscriptionException(SubscriptionFailure.BadGateway, "The billing provider returned a customer without an id.");
        }
        catch (SubscriptionException ex) when (ex.ProviderStatus == 404)
        {
            return null;
        }
    }

    private async Task<int> EnsureCustomerAsync(string userKey, string email, Guid corr, CancellationToken token, CancellationToken caller)
    {
        var link = await _db.CustomerLinks.FindAsync(new object[] { userKey }, caller);
        if (link is { Status: ClaimStatus.Completed, MaxioCustomerId: { } knownId }) return knownId;

        var owned = false;
        if (link is null)
        {
            // claim: the primary key on UserKey rejects the second concurrent creator
            var entity = new CustomerLink { UserKey = userKey, Status = ClaimStatus.Pending, ClaimedAt = _time.GetUtcNow() };
            try
            {
                _db.CustomerLinks.Add(entity);
                await _db.SaveChangesAsync(caller);
                link = entity;
                owned = true;
            }
            catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException or ArgumentException)
            {
                _db.Entry(entity).State = EntityState.Detached;
                link = await _db.CustomerLinks.FirstOrDefaultAsync(l => l.UserKey == userKey, caller);
                if (link is null) throw;
                _logger.LogInformation("[{CorrelationId}] Customer link {UserKey}: duplicate claim", corr, userKey);
            }
        }

        if (!owned)
        {
            // Loser (or leftover row): the provider is the truth.
            var existingId = await ReadCustomerIdAsync(userKey, corr, token, caller);
            if (existingId is not null)
            {
                await CompleteCustomerLinkAsync(userKey, existingId.Value);
                return existingId.Value;
            }

            var age = _time.GetUtcNow() - link.ClaimedAt;
            if (link.Status == ClaimStatus.Pending && age < StalePending)
                throw new SubscriptionException(SubscriptionFailure.InProgress, "Customer provisioning is in progress.", retryAfter: TimeSpan.FromSeconds(2));
            if (link.Status == ClaimStatus.Unknown && age < UnknownExpiry)
                throw new CustomerOutcomeUnknownException();

            // stale: take the link over
            link.Status = ClaimStatus.Pending;
            link.ClaimedAt = _time.GetUtcNow();
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        else
        {
            // our own claim: check the provider first (the in-memory link table does not survive a restart)
            var existingId = await ReadCustomerIdAsync(userKey, corr, token, caller);
            if (existingId is not null)
            {
                await CompleteCustomerLinkAsync(userKey, existingId.Value);
                return existingId.Value;
            }
        }

        try
        {
            var at = email.IndexOf('@');
            var response = await Run("CreateCustomer", corr, write: true, token, caller,
                t => _client.Customers.CreateCustomer(new CreateCustomerOperationRequest
                {
                    Body = new CreateCustomerRequest
                    {
                        Customer = new MaxioAdvancedBilling.Models.CreateCustomer
                        {
                            FirstName = email[..at],
                            LastName = LastNamePlaceholder,
                            Email = email,
                            Reference = CustomerReference(userKey)
                        }
                    }
                }, cancellationToken: t));

            var id = response.Customer?.Id
                ?? throw new SubscriptionException(SubscriptionFailure.BadGateway, "The billing provider returned a customer without an id.",
                    (int)HttpStatusCode.OK) { OutcomeUnknown = true };
            await CompleteCustomerLinkAsync(userKey, id);
            _logger.LogInformation("[{CorrelationId}] Created Maxio customer {CustomerId} for {UserKey}", corr, id, userKey);
            return id;
        }
        catch (SubscriptionException ex) when (ex.OutcomeUnknown)
        {
            _logger.LogWarning("[{CorrelationId}] CreateCustomer outcome unknown ({Failure}); reconciling {UserKey}", corr, ex.Failure, userKey);
            int? found = null;
            var reread = false;
            try
            {
                using var cts = new CancellationTokenSource(ReconcileBudget);
                found = await ReadCustomerIdAsync(userKey, corr, cts.Token, CancellationToken.None);
                reread = true;
            }
            catch (SubscriptionException)
            {
                // re-read failed too: unknown
            }

            if (found is not null)
            {
                await CompleteCustomerLinkAsync(userKey, found.Value);
                return found.Value;
            }
            if (reread && ex.ProviderStatus is >= 500)
            {
                await ReleaseCustomerLinkAsync(userKey);
                throw new SubscriptionException(SubscriptionFailure.Unavailable, "The billing provider is temporarily unavailable.", ex.ProviderStatus);
            }

            await MarkCustomerLinkUnknownAsync(userKey);
            throw new CustomerOutcomeUnknownException();
        }
        catch (SubscriptionException)
        {
            await ReleaseCustomerLinkAsync(userKey);   // definite refusal
            throw;
        }
    }

    // ------------------------------------------------------------------ my subscriptions

    public async Task<IReadOnlyList<SubscriptionDto>> ListMineAsync(string userName, Guid correlationId, CancellationToken cancellationToken)
    {
        var userKey = NormalizeUserKey(userName);
        using var cts = Budget(cancellationToken, ReadBudget);

        var customerId = await ReadCustomerIdAsync(userKey, correlationId, cts.Token, cancellationToken);
        if (customerId is null) return Array.Empty<SubscriptionDto>();

        var subs = await ListSubscriptionsAsync(customerId.Value, correlationId, cts.Token, cancellationToken);
        return subs
            .Where(s => s.Product?.ProductFamily?.Handle is not { } h
                        || string.Equals(h, _familyHandle, StringComparison.OrdinalIgnoreCase))
            .Select(Map)
            .ToList();
    }

    private async Task<IReadOnlyList<Subscription>> ListSubscriptionsAsync(int customerId, Guid corr, CancellationToken token, CancellationToken caller)
    {
        var list = await Run("ListCustomerSubscriptions", corr, write: false, token, caller,
            t => _client.Customers.ListCustomerSubscriptions(new ListCustomerSubscriptionsRequest { CustomerId = customerId }, cancellationToken: t));
        return list.Select(r => r?.Subscription).Where(s => s is not null).Select(s => s!).ToList();
    }

    // ------------------------------------------------------------------ claim store helpers

    private async Task<(SubscriptionClaim? Mine, SubscriptionClaim? Existing)> TryClaimSubscriptionAsync(string claimKey, CancellationToken ct)
    {
        var entity = new SubscriptionClaim { ClaimKey = claimKey, Status = ClaimStatus.Pending, ClaimedAt = _time.GetUtcNow() };
        try
        {
            _db.SubscriptionClaims.Add(entity);
            await _db.SaveChangesAsync(ct);
            return (entity, null);
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException or ArgumentException)
        {
            _db.Entry(entity).State = EntityState.Detached;
            var existing = await _db.SubscriptionClaims.AsNoTracking().FirstOrDefaultAsync(c => c.ClaimKey == claimKey, ct);
            if (existing is null) throw;   // not a duplicate: a real store failure
            return (null, existing);
        }
    }

    private async Task CompleteClaimAsync(string claimKey, int? subscriptionId)
    {
        var claim = await _db.SubscriptionClaims.FindAsync(new object[] { claimKey }, CancellationToken.None);
        if (claim is null) return;
        claim.Status = ClaimStatus.Completed;
        claim.MaxioSubscriptionId = subscriptionId;
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task MarkClaimUnknownAsync(string claimKey)
    {
        var claim = await _db.SubscriptionClaims.FindAsync(new object[] { claimKey }, CancellationToken.None);
        if (claim is null) return;
        claim.Status = ClaimStatus.Unknown;
        claim.ClaimedAt = _time.GetUtcNow();
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task ReleaseClaimAsync(string claimKey)
    {
        var claim = await _db.SubscriptionClaims.FindAsync(new object[] { claimKey }, CancellationToken.None);
        if (claim is null) return;
        _db.SubscriptionClaims.Remove(claim);
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task CompleteCustomerLinkAsync(string userKey, int customerId)
    {
        var link = await _db.CustomerLinks.FindAsync(new object[] { userKey }, CancellationToken.None);
        if (link is null) return;
        link.Status = ClaimStatus.Completed;
        link.MaxioCustomerId = customerId;
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task MarkCustomerLinkUnknownAsync(string userKey)
    {
        var link = await _db.CustomerLinks.FindAsync(new object[] { userKey }, CancellationToken.None);
        if (link is null) return;
        link.Status = ClaimStatus.Unknown;
        link.ClaimedAt = _time.GetUtcNow();
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task ReleaseCustomerLinkAsync(string userKey)
    {
        var link = await _db.CustomerLinks.FindAsync(new object[] { userKey }, CancellationToken.None);
        if (link is null) return;
        _db.CustomerLinks.Remove(link);
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    // ------------------------------------------------------------------ SDK call boundary

    private static CancellationTokenSource Budget(CancellationToken caller, TimeSpan budget)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(caller);
        cts.CancelAfter(budget);
        return cts;
    }

    /// <summary>
    /// The one place SDK failures become <see cref="SubscriptionException"/> (single translation ladder, specific to general).
    /// <paramref name="write"/> marks calls whose failure may leave the provider state unknown.
    /// </summary>
    private async Task<T> Run<T>(string operation, Guid corr, bool write, CancellationToken token, CancellationToken caller,
        Func<CancellationToken, Task<T>> call)
    {
        try
        {
            return await call(token);
        }
        catch (OperationCanceledException) when (caller.IsCancellationRequested)
        {
            throw;   // the caller went away: not a provider failure
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogError("[{CorrelationId}] {Operation} exceeded its time budget", corr, operation);
            throw new SubscriptionException(SubscriptionFailure.Timeout, "The billing provider did not answer in time.", inner: ex) { OutcomeUnknown = write };
        }
        catch (SdkTimeoutException ex)
        {
            _logger.LogError("[{CorrelationId}] {Operation} timed out after {Timeout}", corr, operation, ex.Timeout);
            throw new SubscriptionException(SubscriptionFailure.Timeout, "The billing provider did not answer in time.", inner: ex) { OutcomeUnknown = write };
        }
        catch (SdkConnectionException ex)
        {
            _logger.LogError("[{CorrelationId}] {Operation} could not reach the billing provider ({Type})", corr, operation, ex.InnerException?.GetType().Name);
            throw new SubscriptionException(SubscriptionFailure.BadGateway, "The billing provider is unreachable.", inner: ex) { OutcomeUnknown = write };
        }
        catch (AuthSchemeException ex)
        {
            _logger.LogError("[{CorrelationId}] {Operation}: credentials could not be applied", corr, operation);
            throw new SubscriptionException(SubscriptionFailure.BillingMisconfigured, "billing_misconfigured", inner: ex);
        }
        catch (ApiException ex)   // typed ApiException<TError>, ResponseDeserializationException and any other answered failure
        {
            throw Translate(operation, corr, write, ex);
        }
    }

    private SubscriptionException Translate(string operation, Guid corr, bool write, ApiException ex)
    {
        var status = (int)ex.StatusCode;
        var detail = DescribeRejection(ex);
        // A missing customer reference (404) is an expected answer of the lookup, not a failure.
        var level = status == 404 && operation == "ReadCustomerByReference" ? LogLevel.Information : LogLevel.Error;
        _logger.Log(level, "[{CorrelationId}] {Operation} failed: HTTP {Status}{Kind}{RequestId}{Detail}", corr, operation, status,
            ex is ResponseDeserializationException ? " (unreadable body)" : "", ProviderRequestId(ex.Headers),
            status == 422 && detail is not null ? ": " + detail : "");

        // An unreadable 2xx body: the write may have landed, the outcome is unknown.
        if (ex is ResponseDeserializationException && status is >= 200 and < 300)
            return new SubscriptionException(SubscriptionFailure.BadGateway, "The billing provider returned a response that could not be processed.", status, inner: ex)
            { OutcomeUnknown = write };

        return status switch
        {
            401 or 403 => new SubscriptionException(SubscriptionFailure.BillingMisconfigured, "billing_misconfigured", status, inner: ex),
            404 when operation == "ListProductsForProductFamily" =>
                new SubscriptionException(SubscriptionFailure.CatalogUnavailable, "catalog_unavailable", status, inner: ex),
            422 => new SubscriptionException(SubscriptionFailure.ProviderRejected, detail ?? "The billing provider rejected the request.", status, inner: ex),
            429 => new SubscriptionException(SubscriptionFailure.Unavailable, "The billing provider is temporarily unavailable.", status, inner: ex),
            >= 500 => new SubscriptionException(SubscriptionFailure.Unavailable, "The billing provider is temporarily unavailable.", status, inner: ex)
            { OutcomeUnknown = write },
            _ => new SubscriptionException(SubscriptionFailure.BadGateway, "The billing provider returned an unexpected response.", status, inner: ex)
        };
    }

    /// <summary>Caller-safe text for a 422, read inside the typed accessors of the operation that failed.</summary>
    private static string? DescribeRejection(ApiException ex)
    {
        string? text = null;
        if (ex is ApiException<CreateSubscriptionError> sub && sub.Error.TryGetErrorListResponse1(out var list))
            text = string.Join("; ", list.Errors.Take(5));
        else if (ex is ApiException<CreateCustomerError> cust && cust.Error.TryGetCustomerErrorResponse1(out _))
            text = "The billing provider rejected the customer details.";

        if (string.IsNullOrWhiteSpace(text)) return null;
        var cleaned = new string(text.Where(c => !char.IsControl(c)).ToArray());
        return cleaned.Length > 300 ? cleaned[..300] : cleaned;
    }

    private static string ProviderRequestId(HttpResponseHeaders? headers)
    {
        if (headers is null) return "";
        foreach (var h in headers)
        {
            if (h.Key.Contains("request-id", StringComparison.OrdinalIgnoreCase) || h.Key.Contains("correlation", StringComparison.OrdinalIgnoreCase))
                return $" ({h.Key}={string.Join(",", h.Value)})";
        }
        return "";
    }

    // ------------------------------------------------------------------ pure helpers

    internal static string NormalizeUserKey(string userName) => (userName ?? string.Empty).Trim().ToLowerInvariant();

    internal static string CustomerReference(string userKey) => $"eshop-user:{userKey}";

    internal static string SubscriptionReference(string userKey, string planHandle) => $"eshop-sub:{userKey}:{planHandle}";

    /// <summary>The token carries only the user name; eShop user names are email addresses. Anything else is not usable.</summary>
    internal static string? EmailFor(string userKey)
    {
        if (string.IsNullOrWhiteSpace(userKey) || !userKey.Contains('@')) return null;
        if (!MailAddress.TryCreate(userKey, out var address) || !string.Equals(address.Address, userKey, StringComparison.Ordinal)) return null;
        return address.User.Length > 0 && address.Host.Length > 0 ? userKey : null;
    }

    private static Subscription? FindByReference(IEnumerable<Subscription> subs, string reference) =>
        subs.FirstOrDefault(s => string.Equals(s.Reference, reference, StringComparison.Ordinal));

    private static SubscriptionDto Map(Subscription s) => new(
        s.Id,
        s.Product?.Handle,
        s.Product?.Name,
        s.ProductPriceInCents ?? s.Product?.PriceInCents,
        s.Product?.Interval,
        s.Product?.IntervalUnit?.Value,
        s.State?.Value,
        s.NextAssessmentAt);
}
