using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Subscription billing with Maxio as the system of record. Duplicate enrollment is prevented by claim
/// rows in <see cref="CatalogContext"/> whose primary key is the user name: the claim is written before
/// any provider write, so a second caller is refused by the store before it reaches Maxio.
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const int PlanPageSize = 200;
    private const int MaxPlanPages = 5;
    private static readonly TimeSpan SettlementBudget = TimeSpan.FromSeconds(15);

    private readonly MaxioBillingGateway _gateway;
    private readonly CatalogContext _db;
    private readonly IMemoryCache _cache;
    private readonly MaxioSettings _settings;
    private readonly TimeProvider _clock;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(MaxioBillingGateway gateway, CatalogContext db, IMemoryCache cache,
        IOptions<MaxioSettings> settings, TimeProvider clock, ILogger<MaxioSubscriptionBillingService> logger)
    {
        _gateway = gateway;
        _db = db;
        _cache = cache;
        _settings = settings.Value;
        _clock = clock;
        _logger = logger;
    }

    private string ProductFamilyHandle => _settings.ProductFamilyHandle!;

    // Validated at startup, so the lookup always succeeds.
    private CollectionMethod PaymentCollectionMethod =>
        CollectionMethod.TryGetKnownValue(_settings.PaymentCollectionMethod.Trim(), out var method) ? method : CollectionMethod.Remittance;

    public Task<SubscriptionPlanCatalog> ListPlansAsync(CancellationToken cancellationToken = default) =>
        WithinBudgetAsync(token => GetPlanCatalogAsync(forceRefresh: false, token), cancellationToken);

    public Task<SubscribeResult> SubscribeAsync(Subscriber subscriber, string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(subscriber.UserName))
        {
            throw new ArgumentException("A user name is required.", nameof(subscriber));
        }
        if (string.IsNullOrWhiteSpace(subscriber.Email))
        {
            throw new ArgumentException("An e-mail address is required to open a billing account.", nameof(subscriber));
        }
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new ArgumentException("A plan handle is required.", nameof(planHandle));
        }

        return WithinBudgetAsync(token => SubscribeCoreAsync(subscriber, planHandle.Trim(), attempt: 0, token), cancellationToken);
    }

    public Task<IReadOnlyList<SubscriptionSummary>> ListSubscriptionsAsync(string userName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new ArgumentException("A user name is required.", nameof(userName));
        }
        return WithinBudgetAsync(token => ListSubscriptionsCoreAsync(userName, token), cancellationToken);
    }

    // ---- plans -------------------------------------------------------------------------------------

    private async Task<SubscriptionPlanCatalog> GetPlanCatalogAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        var cacheKey = "maxio:plans:" + ProductFamilyHandle;
        if (!forceRefresh && _cache.TryGetValue(cacheKey, out SubscriptionPlanCatalog? cached) && cached is not null)
        {
            return cached;
        }

        var plans = new List<SubscriptionPlan>();
        var truncated = false;
        for (var page = 1; page <= MaxPlanPages; page++)
        {
            var products = await _gateway.ListProductsPageAsync(ProductFamilyHandle, page, PlanPageSize, cancellationToken);
            plans.AddRange(products
                .Where(p => p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
                .Select(p => new SubscriptionPlan(
                    p.Handle!,
                    string.IsNullOrWhiteSpace(p.Name) ? p.Handle! : p.Name!,
                    p.Description,
                    p.PriceInCents,
                    p.Interval,
                    p.IntervalUnit?.Value)));

            if (products.Count < PlanPageSize)
            {
                break;
            }
            if (page == MaxPlanPages)
            {
                // A full last page at the cap: there may be more plans than we read.
                truncated = true;
                _logger.LogWarning("Maxio plan list hit the {MaxPages}-page cap; the catalog is truncated.", MaxPlanPages);
            }
        }

        var catalog = new SubscriptionPlanCatalog(plans, truncated);
        _cache.Set(cacheKey, catalog, _settings.PlanCacheDuration);
        return catalog;
    }

    /// <summary>
    /// The plan handle must be one the configured product family offers — the same list
    /// <c>GET /api/subscription-plans</c> returns. A cache miss is refreshed once before refusing.
    /// </summary>
    private async Task<(SubscriptionPlan Plan, SubscriptionPlanCatalog Catalog)> RequireOfferedPlanAsync(
        string planHandle, CancellationToken cancellationToken)
    {
        var catalog = await GetPlanCatalogAsync(forceRefresh: false, cancellationToken);
        var plan = FindPlan(catalog, planHandle);
        if (plan is null)
        {
            catalog = await GetPlanCatalogAsync(forceRefresh: true, cancellationToken);
            plan = FindPlan(catalog, planHandle);
        }
        if (plan is null)
        {
            if (catalog.Truncated)
            {
                throw new BillingProviderException(BillingFailureKind.Unavailable,
                    "The plan could not be validated because the plan catalog is incomplete.");
            }
            throw new SubscriptionPlanNotFoundException(planHandle);
        }
        return (plan, catalog);
    }

    private static SubscriptionPlan? FindPlan(SubscriptionPlanCatalog catalog, string planHandle) =>
        catalog.Plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.Ordinal));

    // ---- subscribe ---------------------------------------------------------------------------------

    private async Task<SubscribeResult> SubscribeCoreAsync(Subscriber subscriber, string planHandle, int attempt,
        CancellationToken cancellationToken)
    {
        var (plan, catalog) = await RequireOfferedPlanAsync(planHandle, cancellationToken);

        // Claim first: the primary key refuses a second claim for this user before anything reaches Maxio.
        var claim = new MaxioSubscriptionClaim(subscriber.UserName, plan.Handle, NewSubscriptionReference(), Now);
        if (await TryInsertClaimAsync(claim, cancellationToken))
        {
            return await EnrollAsync(claim, subscriber, catalog, cancellationToken);
        }

        _logger.LogInformation("Subscription claim already exists for this user; resolving it instead of enrolling again.");
        return await ResolveExistingSubscriptionClaimAsync(subscriber, plan, attempt, cancellationToken);
    }

    private async Task<SubscribeResult> EnrollAsync(MaxioSubscriptionClaim claim, Subscriber subscriber,
        SubscriptionPlanCatalog catalog, CancellationToken cancellationToken)
    {
        int customerId;
        Subscription? live;
        try
        {
            customerId = await EnsureCustomerAsync(subscriber, cancellationToken);

            // Claims are app-local; Maxio is the record. Adopt a live subscription already held in this
            // product family (e.g. created before a restart wiped the claim) instead of creating another.
            var offered = catalog.Plans.Select(p => p.Handle).ToHashSet(StringComparer.Ordinal);
            var existing = await _gateway.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
            live = existing.FirstOrDefault(s => s.Id is not null && IsLive(s)
                && s.Product?.Handle is not null && offered.Contains(s.Product.Handle));
        }
        catch
        {
            // Nothing has been written for this subscription yet: release so the user can retry.
            await ReleaseClaimAsync(claim);
            throw;
        }

        if (live is not null)
        {
            var requestedPlan = claim.PlanHandle;
            claim.MarkCreated(live.Id!.Value, live.Product!.Handle!, live.Reference, Now);
            await _db.SaveChangesAsync(CancellationToken.None);
            _logger.LogInformation("Adopted existing Maxio subscription {SubscriptionId} for customer {CustomerId}.",
                live.Id, customerId);
            return ResultForExisting(requestedPlan, live, claim);
        }

        Subscription created;
        try
        {
            created = await _gateway.CreateSubscriptionAsync(customerId, claim.PlanHandle, claim.Reference,
                PaymentCollectionMethod, cancellationToken);
        }
        catch (BillingProviderException)
        {
            // A definite refusal: nothing was created.
            await ReleaseClaimAsync(claim);
            throw;
        }
        catch (MaxioWriteOutcomeUnknownException ex)
        {
            return await SettleUnknownSubscriptionAsync(claim, customerId, ex);
        }

        // The provider has acted: record it even if the caller has gone away.
        claim.MarkCreated(created.Id!.Value, claim.PlanHandle, created.Reference, Now);
        await _db.SaveChangesAsync(CancellationToken.None);
        _logger.LogInformation("Created Maxio subscription {SubscriptionId} ({State}) on plan {PlanHandle} for customer {CustomerId}.",
            created.Id, created.State?.Value, claim.PlanHandle, customerId);
        return new SubscribeResult(SubscribeOutcome.Created, claim.PlanHandle, ToSummary(created));
    }

    private SubscribeResult ResultForExisting(string requestedPlanHandle, Subscription subscription, MaxioSubscriptionClaim claim)
    {
        var heldPlan = subscription.Product?.Handle ?? claim.PlanHandle;
        if (!string.Equals(heldPlan, requestedPlanHandle, StringComparison.Ordinal))
        {
            throw new DuplicateException(
                $"You already have an active subscription to plan '{heldPlan}'. Changing plans is not supported.");
        }
        return new SubscribeResult(SubscribeOutcome.AlreadySubscribed, heldPlan, ToSummary(subscription));
    }

    /// <summary>
    /// A write that may have landed: re-read Maxio by the reference we sent before answering. Found →
    /// recorded as created. Not found or unreadable → the claim is kept as Unknown (blocking any second
    /// create) and settled on the user's next request.
    /// </summary>
    private async Task<SubscribeResult> SettleUnknownSubscriptionAsync(MaxioSubscriptionClaim claim, int customerId, Exception cause)
    {
        _logger.LogWarning(cause, "Maxio create subscription outcome unknown for reference {Reference}; re-reading.", claim.Reference);
        using var settle = new CancellationTokenSource(SettlementBudget);
        try
        {
            var found = await FindSubscriptionAsync(claim.Reference, customerId, settle.Token);
            if (found?.Id is not null)
            {
                claim.MarkCreated(found.Id.Value, claim.PlanHandle, found.Reference, Now);
                await _db.SaveChangesAsync(CancellationToken.None);
                _logger.LogInformation("Settled unknown create: Maxio subscription {SubscriptionId} exists.", found.Id);
                return new SubscribeResult(SubscribeOutcome.Created, claim.PlanHandle, ToSummary(found));
            }
        }
        catch (Exception ex) when (ex is BillingProviderException or OperationCanceledException)
        {
            _logger.LogWarning(ex, "Re-read after unknown create failed for reference {Reference}.", claim.Reference);
        }

        claim.MarkUnknown(Now);
        await _db.SaveChangesAsync(CancellationToken.None);
        _logger.LogError("Maxio subscription for reference {Reference} is unconfirmed; it will be settled on the next request.", claim.Reference);
        return new SubscribeResult(SubscribeOutcome.Pending, claim.PlanHandle, null);
    }

    private async Task<SubscribeResult> ResolveExistingSubscriptionClaimAsync(Subscriber subscriber, SubscriptionPlan plan,
        int attempt, CancellationToken cancellationToken)
    {
        var existing = await _db.MaxioSubscriptionClaims.SingleOrDefaultAsync(c => c.UserName == subscriber.UserName, cancellationToken);
        if (existing is null)
        {
            // Released between our insert and this read; claim again (once).
            return await RetryClaimAsync(subscriber, plan, attempt, cancellationToken);
        }

        if (existing.State == MaxioClaimState.Pending && !IsStale(existing.UpdatedAt))
        {
            throw new DuplicateException("A subscription request for this account is already in progress.");
        }

        if (existing.State == MaxioClaimState.Created)
        {
            var current = await FindSubscriptionAsync(existing.Reference, await KnownCustomerIdAsync(subscriber.UserName, cancellationToken), cancellationToken);
            if (current is not null && IsLive(current))
            {
                return ResultForExisting(plan.Handle, current, existing);
            }
            // Ended (or gone) at the provider: the claim no longer represents a subscription.
            await ReleaseClaimAsync(existing);
            return await RetryClaimAsync(subscriber, plan, attempt, cancellationToken);
        }

        // Unknown, or Pending left behind by an interrupted request: settle it against Maxio.
        var customerId = await KnownCustomerIdAsync(subscriber.UserName, cancellationToken);
        var found = await FindSubscriptionAsync(existing.Reference, customerId, cancellationToken);
        if (found?.Id is not null)
        {
            existing.MarkCreated(found.Id.Value, found.Product?.Handle ?? existing.PlanHandle, found.Reference, Now);
            await _db.SaveChangesAsync(CancellationToken.None);
            return IsLive(found)
                ? ResultForExisting(plan.Handle, found, existing)
                : await ReleaseAndRetryAsync(existing, subscriber, plan, attempt, cancellationToken);
        }

        if (existing.State == MaxioClaimState.Unknown && !IsStale(existing.UpdatedAt))
        {
            // Too soon to conclude the write never landed.
            return new SubscribeResult(SubscribeOutcome.Pending, existing.PlanHandle, null);
        }

        _logger.LogInformation("Unconfirmed subscription reference {Reference} was never created at Maxio; releasing the claim.", existing.Reference);
        return await ReleaseAndRetryAsync(existing, subscriber, plan, attempt, cancellationToken);
    }

    private async Task<SubscribeResult> ReleaseAndRetryAsync(MaxioSubscriptionClaim claim, Subscriber subscriber,
        SubscriptionPlan plan, int attempt, CancellationToken cancellationToken)
    {
        await ReleaseClaimAsync(claim);
        return await RetryClaimAsync(subscriber, plan, attempt, cancellationToken);
    }

    private Task<SubscribeResult> RetryClaimAsync(Subscriber subscriber, SubscriptionPlan plan, int attempt, CancellationToken cancellationToken)
    {
        if (attempt >= 1)
        {
            throw new DuplicateException("A subscription request for this account is already in progress.");
        }
        return SubscribeCoreAsync(subscriber, plan.Handle, attempt + 1, cancellationToken);
    }

    /// <summary>Find by the reference we sent; fall back to the customer's subscriptions matched on that reference.</summary>
    private async Task<Subscription?> FindSubscriptionAsync(string reference, int? customerId, CancellationToken cancellationToken)
    {
        var found = await _gateway.FindSubscriptionByReferenceAsync(reference, cancellationToken);
        if (found is not null || customerId is null)
        {
            return found;
        }
        var all = await _gateway.ListCustomerSubscriptionsAsync(customerId.Value, cancellationToken);
        return all.FirstOrDefault(s => string.Equals(s.Reference, reference, StringComparison.Ordinal));
    }

    // ---- customer ----------------------------------------------------------------------------------

    /// <summary>
    /// Ensures a Maxio customer exists for the user, keyed by <c>reference</c> = user name. Claimed in the
    /// store first; an existing provider customer is adopted before one is created.
    /// </summary>
    private async Task<int> EnsureCustomerAsync(Subscriber subscriber, CancellationToken cancellationToken)
    {
        var claim = new MaxioCustomerClaim(subscriber.UserName, Now);
        if (!await TryInsertClaimAsync(claim, cancellationToken))
        {
            var existing = await _db.MaxioCustomerClaims.SingleAsync(c => c.UserName == subscriber.UserName, cancellationToken);
            if (existing.State == MaxioClaimState.Created && existing.MaxioCustomerId is not null)
            {
                return existing.MaxioCustomerId.Value;
            }
            if (existing.State == MaxioClaimState.Pending && !IsStale(existing.UpdatedAt))
            {
                throw new DuplicateException("A billing account for this user is already being created.");
            }
            // Unknown or abandoned: take it over. Every path below re-reads Maxio before creating.
            existing.Retake(Now);
            await _db.SaveChangesAsync(cancellationToken);
            claim = existing;
        }

        Customer created;
        try
        {
            var adopted = await _gateway.FindCustomerByReferenceAsync(subscriber.UserName, cancellationToken);
            if (adopted?.Id is not null)
            {
                return await RecordCustomerAsync(claim, adopted.Id.Value);
            }

            var (firstName, lastName) = NamesFor(subscriber);
            created = await _gateway.CreateCustomerAsync(subscriber.UserName, subscriber.Email, firstName, lastName, cancellationToken);
        }
        catch (BillingProviderException ex) when (ex.Kind == BillingFailureKind.Rejected)
        {
            // Possibly the reference is already taken (rejection shape undocumented): re-read before giving up.
            var adopted = await TryFindCustomerAsync(subscriber.UserName);
            if (adopted?.Id is not null)
            {
                return await RecordCustomerAsync(claim, adopted.Id.Value);
            }
            await ReleaseClaimAsync(claim);
            throw;
        }
        catch (BillingProviderException)
        {
            await ReleaseClaimAsync(claim);
            throw;
        }
        catch (MaxioWriteOutcomeUnknownException ex)
        {
            _logger.LogWarning(ex, "Maxio create customer outcome unknown; re-reading by reference.");
            var adopted = await TryFindCustomerAsync(subscriber.UserName);
            if (adopted?.Id is not null)
            {
                return await RecordCustomerAsync(claim, adopted.Id.Value);
            }
            claim.MarkUnknown(Now);
            await _db.SaveChangesAsync(CancellationToken.None);
            throw new BillingProviderException(BillingFailureKind.Unavailable,
                "Your billing account could not be confirmed. Please try again shortly.", innerException: ex);
        }
        catch
        {
            await ReleaseClaimAsync(claim);
            throw;
        }

        _logger.LogInformation("Created Maxio customer {CustomerId}.", created.Id);
        return await RecordCustomerAsync(claim, created.Id!.Value);
    }

    private async Task<int> RecordCustomerAsync(MaxioCustomerClaim claim, int customerId)
    {
        claim.MarkCreated(customerId, Now);
        await _db.SaveChangesAsync(CancellationToken.None);
        return customerId;
    }

    /// <summary>Re-read with its own budget: used after a write whose request token may already be spent.</summary>
    private async Task<Customer?> TryFindCustomerAsync(string reference)
    {
        using var settle = new CancellationTokenSource(SettlementBudget);
        try
        {
            return await _gateway.FindCustomerByReferenceAsync(reference, settle.Token);
        }
        catch (Exception ex) when (ex is BillingProviderException or OperationCanceledException)
        {
            _logger.LogWarning(ex, "Re-read of Maxio customer by reference failed.");
            return null;
        }
    }

    private async Task<int?> KnownCustomerIdAsync(string userName, CancellationToken cancellationToken)
    {
        var claim = await _db.MaxioCustomerClaims.AsNoTracking().SingleOrDefaultAsync(c => c.UserName == userName, cancellationToken);
        if (claim?.State == MaxioClaimState.Created && claim.MaxioCustomerId is not null)
        {
            return claim.MaxioCustomerId;
        }
        return (await _gateway.FindCustomerByReferenceAsync(userName, cancellationToken))?.Id;
    }

    private static (string FirstName, string LastName) NamesFor(Subscriber subscriber)
    {
        // The app stores no personal names; Maxio requires both, so derive non-blank ones from the e-mail.
        var local = subscriber.Email.Split('@')[0];
        var parts = local.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);
        var first = parts.Length > 0 ? parts[0] : local;
        var last = parts.Length > 1 ? string.Join(" ", parts.Skip(1)) : "eShopOnWeb";
        return (first, last);
    }

    // ---- my subscriptions --------------------------------------------------------------------------

    private async Task<IReadOnlyList<SubscriptionSummary>> ListSubscriptionsCoreAsync(string userName, CancellationToken cancellationToken)
    {
        var customerId = await KnownCustomerIdAsync(userName, cancellationToken);

        // Settle an unconfirmed enrollment first, so the answer reflects what Maxio actually holds.
        var claim = await _db.MaxioSubscriptionClaims.SingleOrDefaultAsync(c => c.UserName == userName, cancellationToken);
        if (claim is not null && customerId is not null
            && (claim.State == MaxioClaimState.Unknown || (claim.State == MaxioClaimState.Pending && IsStale(claim.UpdatedAt))))
        {
            var found = await FindSubscriptionAsync(claim.Reference, customerId, cancellationToken);
            if (found?.Id is not null)
            {
                claim.MarkCreated(found.Id.Value, found.Product?.Handle ?? claim.PlanHandle, found.Reference, Now);
                await _db.SaveChangesAsync(CancellationToken.None);
            }
        }

        if (customerId is null)
        {
            return Array.Empty<SubscriptionSummary>();
        }

        var subscriptions = await _gateway.ListCustomerSubscriptionsAsync(customerId.Value, cancellationToken);
        return subscriptions
            .OrderByDescending(s => s.CreatedAt)
            .Select(ToSummary)
            .ToList();
    }

    // ---- helpers -----------------------------------------------------------------------------------

    private DateTimeOffset Now => _clock.GetUtcNow();

    private bool IsStale(DateTimeOffset updatedAt) => Now - updatedAt > _settings.PendingClaimStaleAfter;

    private static string NewSubscriptionReference() => "eshop-sub-" + Guid.NewGuid().ToString("N");

    private static bool IsLive(Subscription subscription)
    {
        var state = subscription.State;
        if (state is null)
        {
            return true; // unknown → assume it counts, never create a second one on a guess
        }
        return state != SubscriptionState.Canceled
            && state != SubscriptionState.Expired
            && state != SubscriptionState.FailedToCreate;
    }

    private static SubscriptionSummary ToSummary(Subscription s) => new(
        s.Id,
        s.Reference,
        s.Product?.Handle,
        s.Product?.Name,
        s.ProductPriceInCents,
        s.Currency,
        s.State?.Value,
        s.NextAssessmentAt,
        s.CurrentPeriodEndsAt,
        s.CreatedAt);

    /// <summary>Insert-or-refuse: the primary key rejects a second claim for the same user.</summary>
    private async Task<bool> TryInsertClaimAsync<TClaim>(TClaim claim, CancellationToken cancellationToken) where TClaim : class
    {
        _db.Add(claim);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception ex) when (IsDuplicateKey(ex))
        {
            _db.Entry(claim).State = EntityState.Detached;
            return false;
        }
    }

    // SQL Server reports a duplicate key as DbUpdateException; the in-memory provider raises
    // ArgumentException ("An item with the same key has already been added") from its key index.
    private bool IsDuplicateKey(Exception ex) =>
        ex is DbUpdateException || (ex is ArgumentException && _db.Database.IsInMemory());

    private async Task ReleaseClaimAsync(object claim)
    {
        _db.Remove(claim);
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<T> WithinBudgetAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken callerToken)
    {
        // One home for the whole-call budget: every provider call made for this request shares it.
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        budget.CancelAfter(_settings.RequestBudget);
        try
        {
            return await work(budget.Token);
        }
        catch (OperationCanceledException ex) when (!callerToken.IsCancellationRequested)
        {
            throw new BillingProviderException(BillingFailureKind.Timeout,
                "The billing provider did not respond in time.", innerException: ex);
        }
    }
}
