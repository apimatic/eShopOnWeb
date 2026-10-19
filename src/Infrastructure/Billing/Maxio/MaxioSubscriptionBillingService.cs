using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MaxioProduct = MaxioAdvancedBilling.Models.Product;
using MaxioSubscription = MaxioAdvancedBilling.Models.Subscription;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Subscription billing backed by Maxio Advanced Billing (system of record).
/// Writes follow claim → SDK call → record: a claim row in <see cref="CatalogContext"/> is inserted before
/// each Maxio create, so a double-click is refused by the primary key before it reaches Maxio. A write whose
/// outcome is unknown (timeout, dropped connection, 5xx, unreadable reply) is settled by re-reading Maxio.
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    public const int PlanPageSize = 200;
    public const int MaxPlanPages = 5;

    /// <summary>Total time one inbound API request may spend on Maxio calls.</summary>
    internal static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    /// <summary>Separate budget for settling an unknown write, so it still runs when the caller's budget is spent.</summary>
    internal static readonly TimeSpan ReconcileBudget = TimeSpan.FromSeconds(15);
    /// <summary>A Pending claim older than this belongs to a request that died; it may be taken over.</summary>
    internal static readonly TimeSpan StaleClaimAge = TimeSpan.FromMinutes(2);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly CatalogContext _db;
    private readonly MaxioSettings _settings;
    private readonly TimeProvider _clock;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        CatalogContext db,
        IOptions<MaxioSettings> settings,
        TimeProvider clock,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _db = db;
        _settings = settings.Value;
        _clock = clock;
        _logger = logger;
    }

    private string FamilyHandle => _settings.ProductFamilyHandle!;

    // ------------------------------------------------------------------ public surface

    public async Task<SubscriptionPlanList> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        using var budget = Budget(cancellationToken);
        try
        {
            return await ListPlansCoreAsync(budget.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw TimedOut();
        }
    }

    public async Task<SubscribeResult> SubscribeAsync(Subscriber subscriber, string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new BillingException(BillingFailureKind.InvalidRequest, "planHandle is required. Use GET /api/subscription-plans to list the available plans.");
        }

        using var budget = Budget(cancellationToken);
        try
        {
            // Cross-operation invariant: the plan must be one this application offers (a live product of the family).
            var plans = await ListPlansCoreAsync(budget.Token);
            var plan = plans.Plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle.Trim(), StringComparison.Ordinal));
            if (plan is null)
            {
                throw new BillingException(BillingFailureKind.InvalidRequest,
                    $"Unknown plan '{planHandle}'. Use GET /api/subscription-plans to list the available plans.");
            }

            var customerId = await EnsureCustomerAsync(subscriber, budget.Token);
            return await EnsureSubscriptionAsync(subscriber, customerId, plan, plans, budget.Token);
        }
        catch (OutcomeUnknownException)
        {
            return new SubscribeResult(SubscribeOutcome.PendingConfirmation, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw TimedOut();
        }
    }

    public async Task<SubscriptionDetailsList> ListSubscriptionsAsync(Subscriber subscriber, CancellationToken cancellationToken = default)
    {
        using var budget = Budget(cancellationToken);
        try
        {
            var pending = await SettleOpenSubscriptionClaimAsync(subscriber.UserName, budget.Token);

            var customerId = await FindKnownCustomerIdAsync(subscriber.UserName, budget.Token);
            if (customerId is null)
            {
                return new SubscriptionDetailsList(Array.Empty<SubscriptionDetails>(), pending);
            }

            var subscriptions = await ListFamilySubscriptionsAsync(customerId.Value, budget.Token);
            var details = subscriptions
                .Select(ToDetails)
                .OrderByDescending(s => s.CreatedAt)
                .ToList();
            return new SubscriptionDetailsList(details, pending);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw TimedOut();
        }
    }

    // ------------------------------------------------------------------ plans

    private async Task<SubscriptionPlanList> ListPlansCoreAsync(CancellationToken ct)
    {
        var plans = new List<SubscriptionPlan>();
        var truncated = false;

        for (var page = 1; ; page++)
        {
            IReadOnlyList<ProductResponse> batch;
            try
            {
                batch = await _client.ProductFamilies.ListProductsForProductFamily(
                    new ListProductsForProductFamilyRequest
                    {
                        ProductFamilyId = "handle:" + FamilyHandle,
                        Page = page,
                        PerPage = PlanPageSize
                    },
                    cancellationToken: ct);
            }
            catch (ApiException<ListProductsForProductFamilyError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                // Our configuration names a family the site does not have — an operator fault, not the caller's.
                _logger.LogError("Maxio product family configured in Maxio:ProductFamilyHandle was not found (HTTP 404).");
                throw new BillingException(BillingFailureKind.Unavailable, "Subscription plans are not available right now.", ex);
            }
            catch (SdkException ex)
            {
                throw ProviderFailure("ListProductsForProductFamily", ex);
            }

            foreach (var item in batch)
            {
                var product = item.Product;
                if (product?.Handle is null || product.ArchivedAt is not null)
                {
                    continue;
                }

                plans.Add(new SubscriptionPlan(
                    product.Handle,
                    product.Name ?? product.Handle,
                    product.Description,
                    CentsToAmount(product.PriceInCents) ?? 0m,
                    product.Interval,
                    product.IntervalUnit?.Value));
            }

            if (batch.Count < PlanPageSize)
            {
                break;
            }

            if (page >= MaxPlanPages)
            {
                truncated = true;
                _logger.LogWarning("Maxio plan listing stopped at the {MaxPages}-page cap; result marked truncated.", MaxPlanPages);
                break;
            }
        }

        return new SubscriptionPlanList(plans, truncated);
    }

    // ------------------------------------------------------------------ customer

    /// <summary>Ensures a Maxio customer exists for the subscriber and returns its id. Idempotent per user.</summary>
    private async Task<int> EnsureCustomerAsync(Subscriber subscriber, CancellationToken ct)
    {
        var reference = subscriber.UserName;

        var claim = new MaxioCustomerClaim(reference, Now);
        var tookOver = false;
        if (!await TryInsertClaimAsync(claim, () => _db.MaxioCustomerClaims.AnyAsync(c => c.Reference == reference)))
        {
            // Another request claimed this customer first.
            claim = await _db.MaxioCustomerClaims.SingleAsync(c => c.Reference == reference, CancellationToken.None);
            if (claim.Status == MaxioClaimStatus.Created && claim.MaxioCustomerId is int knownId)
            {
                return knownId;
            }

            if (claim.Status == MaxioClaimStatus.Pending && !IsStale(claim.UpdatedAt))
            {
                throw new BillingException(BillingFailureKind.Conflict, "Your billing account is being set up by another request. Please retry shortly.");
            }

            // Stale or unknown: take the claim over (optimistic concurrency stops two requests doing this at once).
            claim.TakeOver(Now);
            await SaveClaimOrConflictAsync();
            tookOver = true;
        }

        // We hold the claim. Maxio may already have this customer (an earlier unknown outcome, or local state lost on restart).
        int? existingId;
        try
        {
            existingId = await FindCustomerIdByReferenceAsync(reference, ct);
        }
        catch (Exception ex) when (ex is BillingException or OperationCanceledException)
        {
            // Nothing was written by this request. A claim taken over from an unsettled request stays unknown.
            if (tookOver)
            {
                claim.MarkUnknown(Now);
                await SaveAsync();
            }
            else
            {
                await ReleaseClaimAsync(claim);
            }

            throw;
        }

        if (existingId is int foundId)
        {
            claim.MarkCreated(foundId, Now);
            await SaveAsync();
            return foundId;
        }

        try
        {
            var response = await _client.Customers.CreateCustomer(
                new CreateCustomerOperationRequest
                {
                    Body = new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = FirstNameFor(subscriber),
                            LastName = "eShopOnWeb",
                            Email = subscriber.Email,
                            Reference = reference
                        }
                    }
                },
                cancellationToken: ct);

            if (response.Customer?.Id is int createdId)
            {
                claim.MarkCreated(createdId, Now);
                await SaveAsync();
                _logger.LogInformation("Created Maxio customer {MaxioCustomerId}.", createdId);
                return createdId;
            }

            _logger.LogError("Maxio CreateCustomer succeeded without a customer id; settling by reference.");
            return await SettleCustomerAsync(claim);
        }
        catch (ApiException<CreateCustomerError> ex) when (IsRefusal(ex))
        {
            // Maxio refused: nothing was created. A concurrent create with the same reference elsewhere also lands
            // here, so look once more before giving the claim back.
            int? racedId = null;
            try
            {
                racedId = await FindCustomerIdByReferenceAsync(reference, ct);
            }
            catch (Exception lookupFailure) when (lookupFailure is BillingException or OperationCanceledException)
            {
                // The refusal stands on its own; the lookup was only a courtesy.
            }

            if (racedId is not null)
            {
                claim.MarkCreated(racedId.Value, Now);
                await SaveAsync();
                return racedId.Value;
            }

            await ReleaseClaimAsync(claim);
            throw WriteRefused("CreateCustomer", ex, DescribeCustomerErrors(ex.Error));
        }
        catch (ApiException ex) when (IsRefusal(ex))
        {
            // Includes a refusal whose error body did not match the documented shape (ResponseDeserializationException).
            await ReleaseClaimAsync(claim);
            throw WriteRefused("CreateCustomer", ex, null);
        }
        catch (AuthSchemeException ex)
        {
            // The credential could not be applied, so nothing was sent.
            await ReleaseClaimAsync(claim);
            throw ProviderFailure("CreateCustomer", ex);
        }
        catch (Exception ex) when (IsUnknownOutcome(ex))
        {
            _logger.LogWarning("Maxio CreateCustomer outcome unknown ({Failure}); settling by reference.", Describe(ex));
            return await SettleCustomerAsync(claim);
        }
    }

    /// <summary>Settles an unknown CreateCustomer by re-reading Maxio by the reference that was sent.</summary>
    private async Task<int> SettleCustomerAsync(MaxioCustomerClaim claim)
    {
        using var reconcile = new CancellationTokenSource(ReconcileBudget);
        try
        {
            if (await FindCustomerIdByReferenceAsync(claim.Reference, reconcile.Token) is int id)
            {
                claim.MarkCreated(id, Now);
                await SaveAsync();
                _logger.LogInformation("Settled unknown CreateCustomer: Maxio customer {MaxioCustomerId} exists.", id);
                return id;
            }
        }
        catch (Exception ex) when (ex is BillingException or OperationCanceledException)
        {
            _logger.LogWarning("Re-reading Maxio customer after an unknown create failed ({Failure}).", Describe(ex));
        }

        // Not found, or the re-read failed too: still unknown. Keep the claim so no second create is sent
        // until a later request settles it.
        claim.MarkUnknown(Now);
        await SaveAsync();
        _logger.LogError("Maxio CreateCustomer outcome remains unknown; claim kept for settlement on the next request.");
        throw new OutcomeUnknownException();
    }

    /// <summary>Looks a customer up by reference. Returns null only on a definite "not found".</summary>
    private async Task<int?> FindCustomerIdByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = reference },
                cancellationToken: ct);

            if (response.Customer?.Id is int id)
            {
                return id;
            }

            throw new BillingException(BillingFailureKind.Unavailable, "The billing provider returned an incomplete customer record.");
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException ex)
        {
            throw ProviderFailure("ReadCustomerByReference", ex);
        }
    }

    /// <summary>The caller's Maxio customer id from the local claim, else from Maxio by reference (survives lost local state).</summary>
    private async Task<int?> FindKnownCustomerIdAsync(string userName, CancellationToken ct)
    {
        var claim = await _db.MaxioCustomerClaims.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Reference == userName, ct);
        if (claim is { Status: MaxioClaimStatus.Created, MaxioCustomerId: int id })
        {
            return id;
        }

        return await FindCustomerIdByReferenceAsync(userName, ct);
    }

    // ------------------------------------------------------------------ subscription

    private async Task<SubscribeResult> EnsureSubscriptionAsync(
        Subscriber subscriber, int customerId, SubscriptionPlan plan, SubscriptionPlanList plans, CancellationToken ct)
    {
        var userName = subscriber.UserName;
        var claim = new MaxioSubscriptionClaim(userName, plan.Handle, customerId, Now);

        if (!await TryInsertClaimAsync(claim, () => _db.MaxioSubscriptionClaims.AnyAsync(c => c.UserName == userName)))
        {
            claim = await _db.MaxioSubscriptionClaims.SingleAsync(c => c.UserName == userName, CancellationToken.None);
            var existing = await ResolveExistingClaimAsync(claim, customerId, plan, ct);
            if (existing is not null)
            {
                return existing;
            }
            // ResolveExistingClaimAsync has re-armed the claim (state Pending, owned by this request): go on to create.
        }

        // Guard against lost local state (e.g. the in-memory store restarted): adopt a live subscription Maxio already has.
        MaxioSubscription? live;
        try
        {
            live = (await ListFamilySubscriptionsAsync(customerId, ct, plans)).FirstOrDefault(IsLive);
        }
        catch (Exception ex) when (ex is BillingException or OperationCanceledException)
        {
            // Nothing was written by this request; any earlier attempt on this claim is already settled.
            await ReleaseClaimAsync(claim);
            throw;
        }

        if (live?.Id is int liveId)
        {
            var liveHandle = live.Product?.Handle ?? plan.Handle;
            claim.MarkCreated(liveId, liveHandle, Now);
            await SaveAsync();
            return SameOrConflict(liveHandle, plan.Handle, live);
        }

        return await CreateSubscriptionAsync(claim, customerId, plan, ct);
    }

    /// <summary>
    /// Decides what to do with a claim another request already holds. Returns a result to hand back, or null when
    /// this request now owns the (re-armed) claim and should create the subscription.
    /// </summary>
    private async Task<SubscribeResult?> ResolveExistingClaimAsync(
        MaxioSubscriptionClaim claim, int customerId, SubscriptionPlan plan, CancellationToken ct)
    {
        switch (claim.Status)
        {
            case MaxioClaimStatus.Created when claim.MaxioSubscriptionId is int subscriptionId:
            {
                var current = await ReadSubscriptionAsync(subscriptionId, ct);
                if (current is not null && IsLive(current))
                {
                    return SameOrConflict(current.Product?.Handle ?? claim.ProductHandle, plan.Handle, current);
                }

                // The earlier subscription has ended (canceled/expired): allow a new one on this claim.
                claim.Begin(plan.Handle, customerId, Now);
                await SaveClaimOrConflictAsync();
                return null;
            }

            case MaxioClaimStatus.Pending when !IsStale(claim.UpdatedAt):
                throw new BillingException(BillingFailureKind.Conflict, "A subscription request for your account is already in progress. Please retry shortly.");

            default:
            {
                // Unknown, or a Pending claim whose request died: settle it by the reference it sent.
                MaxioSubscription? found;
                try
                {
                    found = await FindSubscriptionByReferenceAsync(claim.SubscriptionReference, claim.MaxioCustomerId, ct);
                }
                catch (BillingException ex)
                {
                    _logger.LogWarning("Could not settle subscription claim {SubscriptionReference}: {Failure}", claim.SubscriptionReference, ex.Message);
                    throw new OutcomeUnknownException();
                }

                if (found?.Id is int foundId && IsLive(found))
                {
                    var handle = found.Product?.Handle ?? claim.ProductHandle;
                    claim.MarkCreated(foundId, handle, Now);
                    await SaveClaimOrConflictAsync();
                    _logger.LogInformation("Settled subscription claim {SubscriptionReference}: Maxio subscription {MaxioSubscriptionId} exists.",
                        claim.SubscriptionReference, foundId);
                    return SameOrConflict(handle, plan.Handle, found);
                }

                // Maxio has no subscription with that reference: the earlier send never landed. Retry on this claim.
                claim.Begin(plan.Handle, customerId, Now);
                await SaveClaimOrConflictAsync();
                return null;
            }
        }
    }

    private async Task<SubscribeResult> CreateSubscriptionAsync(
        MaxioSubscriptionClaim claim, int customerId, SubscriptionPlan plan, CancellationToken ct)
    {
        try
        {
            var response = await _client.Subscriptions.CreateSubscription(
                new CreateSubscriptionOperationRequest
                {
                    Body = new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = plan.Handle,
                            CustomerId = customerId,
                            Reference = claim.SubscriptionReference,
                            // No card is captured in this flow, so charges are invoiced to the customer
                            // (remittance) rather than collected automatically from a payment profile.
                            PaymentCollectionMethod = CollectionMethod.Remittance
                        }
                    }
                },
                cancellationToken: ct);

            if (response.Subscription?.Id is int subscriptionId)
            {
                claim.MarkCreated(subscriptionId, plan.Handle, Now);
                await SaveAsync();
                _logger.LogInformation("Created Maxio subscription {MaxioSubscriptionId} ({ProductHandle}) for customer {MaxioCustomerId}, reference {SubscriptionReference}.",
                    subscriptionId, plan.Handle, customerId, claim.SubscriptionReference);
                return new SubscribeResult(SubscribeOutcome.Created, ToDetails(response.Subscription));
            }

            _logger.LogError("Maxio CreateSubscription succeeded without a subscription; settling reference {SubscriptionReference}.", claim.SubscriptionReference);
            return await SettleSubscriptionAsync(claim);
        }
        catch (ApiException<CreateSubscriptionError> ex) when (IsRefusal(ex))
        {
            await ReleaseClaimAsync(claim);
            throw WriteRefused("CreateSubscription", ex, DescribeSubscriptionErrors(ex.Error));
        }
        catch (ApiException ex) when (IsRefusal(ex))
        {
            await ReleaseClaimAsync(claim);
            throw WriteRefused("CreateSubscription", ex, null);
        }
        catch (AuthSchemeException ex)
        {
            await ReleaseClaimAsync(claim);
            throw ProviderFailure("CreateSubscription", ex);
        }
        catch (Exception ex) when (IsUnknownOutcome(ex))
        {
            _logger.LogWarning("Maxio CreateSubscription outcome unknown for reference {SubscriptionReference} ({Failure}); settling.",
                claim.SubscriptionReference, Describe(ex));
            return await SettleSubscriptionAsync(claim);
        }
    }

    /// <summary>Settles an unknown CreateSubscription by re-reading Maxio by the reference that was sent.</summary>
    private async Task<SubscribeResult> SettleSubscriptionAsync(MaxioSubscriptionClaim claim)
    {
        using var reconcile = new CancellationTokenSource(ReconcileBudget);
        try
        {
            var found = await FindSubscriptionByReferenceAsync(claim.SubscriptionReference, claim.MaxioCustomerId, reconcile.Token);
            if (found?.Id is int id)
            {
                claim.MarkCreated(id, found.Product?.Handle ?? claim.ProductHandle, Now);
                await SaveAsync();
                _logger.LogInformation("Settled unknown CreateSubscription {SubscriptionReference}: Maxio subscription {MaxioSubscriptionId} exists.",
                    claim.SubscriptionReference, id);
                return new SubscribeResult(SubscribeOutcome.Created, ToDetails(found));
            }
        }
        catch (Exception ex) when (ex is BillingException or OperationCanceledException)
        {
            _logger.LogWarning("Re-reading Maxio subscription {SubscriptionReference} failed ({Failure}).", claim.SubscriptionReference, Describe(ex));
        }

        // Not visible yet (or the re-read failed): never report "failed" — the write may have landed.
        claim.MarkUnknown(Now);
        await SaveAsync();
        _logger.LogError("Maxio CreateSubscription {SubscriptionReference} outcome remains unknown; claim kept for settlement on the next request.",
            claim.SubscriptionReference);
        return new SubscribeResult(SubscribeOutcome.PendingConfirmation, null);
    }

    /// <summary>
    /// Settles the caller's open subscription claim (Unknown, or a dead Pending one) before listing.
    /// Returns true while a subscribe for this user is still unconfirmed.
    /// </summary>
    private async Task<bool> SettleOpenSubscriptionClaimAsync(string userName, CancellationToken ct)
    {
        var claim = await _db.MaxioSubscriptionClaims.SingleOrDefaultAsync(c => c.UserName == userName, ct);
        if (claim is null || claim.Status == MaxioClaimStatus.Created)
        {
            return false;
        }

        if (claim.Status == MaxioClaimStatus.Pending && !IsStale(claim.UpdatedAt))
        {
            return true;
        }

        try
        {
            var found = await FindSubscriptionByReferenceAsync(claim.SubscriptionReference, claim.MaxioCustomerId, ct);
            if (found?.Id is int id)
            {
                claim.MarkCreated(id, found.Product?.Handle ?? claim.ProductHandle, Now);
                await SaveAsync();
                return false;
            }

            // Definitely not at Maxio: the earlier send never landed. Free the claim so the user can subscribe again.
            await ReleaseClaimAsync(claim);
            return false;
        }
        catch (Exception ex) when (ex is BillingException or DbUpdateConcurrencyException)
        {
            _logger.LogWarning("Could not settle subscription claim {SubscriptionReference} ({Failure}).", claim.SubscriptionReference, Describe(ex));
            return true;
        }
    }

    /// <summary>
    /// Finds a subscription by the app-generated reference: <c>FindSubscription</c> first, then the customer's
    /// subscription list. Returns null only when both say it does not exist.
    /// </summary>
    private async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, int customerId, CancellationToken ct)
    {
        try
        {
            var response = await _client.Subscriptions.FindSubscription(
                new FindSubscriptionRequest { Reference = reference },
                cancellationToken: ct);
            if (response.Subscription is not null)
            {
                return response.Subscription;
            }
        }
        catch (ApiException<FindSubscriptionError> ex) when (ex.Error.TryGetNoContent(out _))
        {
            // 404: no subscription with that reference — confirm against the customer's list below.
        }
        catch (SdkException ex)
        {
            throw ProviderFailure("FindSubscription", ex);
        }

        var all = await ListCustomerSubscriptionsAsync(customerId, ct);
        return all.FirstOrDefault(s => string.Equals(s.Reference, reference, StringComparison.Ordinal));
    }

    private async Task<MaxioSubscription?> ReadSubscriptionAsync(int subscriptionId, CancellationToken ct)
    {
        try
        {
            var response = await _client.Subscriptions.ReadSubscription(
                new ReadSubscriptionRequest { SubscriptionId = subscriptionId },
                cancellationToken: ct);
            return response.Subscription;
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException ex)
        {
            throw ProviderFailure("ReadSubscription", ex);
        }
    }

    private async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ListCustomerSubscriptions(
                new ListCustomerSubscriptionsRequest { CustomerId = customerId },
                cancellationToken: ct);
            return response
                .Select(r => r.Subscription)
                .Where(s => s?.Id is not null)
                .Select(s => s!)
                .ToList();
        }
        catch (SdkException ex)
        {
            throw ProviderFailure("ListCustomerSubscriptions", ex);
        }
    }

    /// <summary>The customer's subscriptions whose product belongs to the configured family.</summary>
    private async Task<IReadOnlyList<MaxioSubscription>> ListFamilySubscriptionsAsync(
        int customerId, CancellationToken ct, SubscriptionPlanList? knownPlans = null)
    {
        var all = await ListCustomerSubscriptionsAsync(customerId, ct);
        HashSet<string>? planHandles = knownPlans is null ? null : knownPlans.Plans.Select(p => p.Handle).ToHashSet(StringComparer.Ordinal);

        var result = new List<MaxioSubscription>();
        foreach (var subscription in all)
        {
            var product = subscription.Product;
            if (product?.ProductFamily?.Handle is string familyHandle)
            {
                if (string.Equals(familyHandle, FamilyHandle, StringComparison.Ordinal))
                {
                    result.Add(subscription);
                }
                continue;
            }

            // The product carries no family: fall back to matching the handle against the family's plans.
            planHandles ??= (await ListPlansCoreAsync(ct)).Plans.Select(p => p.Handle).ToHashSet(StringComparer.Ordinal);
            if (product?.Handle is string handle && planHandles.Contains(handle))
            {
                result.Add(subscription);
            }
        }

        return result;
    }

    // ------------------------------------------------------------------ claims (local store)

    /// <summary>
    /// Inserts a claim. Returns false when the store refuses it because the key is already claimed.
    /// </summary>
    private async Task<bool> TryInsertClaimAsync<TClaim>(TClaim claim, Func<Task<bool>> keyExists) where TClaim : class
    {
        // Fast path for repeats. Not the guard: two racing requests can both pass it, and then the insert decides.
        if (await keyExists())
        {
            return false;
        }

        _db.Add(claim);
        try
        {
            await _db.SaveChangesAsync(CancellationToken.None);
            return true;
        }
        catch (Exception ex) when (ex is DbUpdateException or ArgumentException)
        {
            // SQL Server raises DbUpdateException on a duplicate primary key; the in-memory provider raises
            // ArgumentException. Either way confirm it really was the key before treating it as "already claimed".
            _db.Entry(claim).State = EntityState.Detached;
            if (await keyExists())
            {
                return false;
            }

            throw;
        }
    }

    private async Task SaveClaimOrConflictAsync()
    {
        try
        {
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new BillingException(BillingFailureKind.Conflict, "Your subscription request is being processed by another request. Please retry shortly.", ex);
        }
    }

    private Task SaveAsync() => _db.SaveChangesAsync(CancellationToken.None);

    private async Task ReleaseClaimAsync<TClaim>(TClaim claim) where TClaim : class
    {
        _db.Remove(claim);
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    // ------------------------------------------------------------------ error translation

    private static bool IsRefusal(ApiException ex)
    {
        var status = (int)ex.StatusCode;
        return status is >= 400 and < 500;
    }

    /// <summary>
    /// True when a write may have reached Maxio even though we got no usable answer: no response, a timeout,
    /// a 5xx, an unreadable 2xx, or our own budget/caller cancellation after the send.
    /// </summary>
    private static bool IsUnknownOutcome(Exception ex) => ex switch
    {
        SdkConnectionException => true, // includes SdkTimeoutException
        ApiException api => (int)api.StatusCode >= 500 || (int)api.StatusCode < 400,
        OperationCanceledException => true,
        _ => false
    };

    private BillingException WriteRefused(string operation, ApiException ex, string? providerDetail)
    {
        var status = (int)ex.StatusCode;
        if (status is 401 or 403 or 429)
        {
            return ProviderFailure(operation, ex);
        }

        _logger.LogWarning("Maxio {Operation} refused with HTTP {Status}: {Detail}", operation, status, providerDetail ?? "(no detail)");
        var message = string.IsNullOrWhiteSpace(providerDetail)
            ? "The billing provider rejected the request."
            : $"The billing provider rejected the request: {providerDetail}";
        return new BillingException(BillingFailureKind.Rejected, message, ex);
    }

    /// <summary>One shared mapping from SDK failures to caller-safe <see cref="BillingException"/>s.</summary>
    private BillingException ProviderFailure(string operation, SdkException ex)
    {
        switch (ex)
        {
            case ApiException api when api.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                _logger.LogError("Maxio {Operation} rejected our credentials (HTTP {Status}). Check Maxio:ApiKey / Maxio:Subdomain.", operation, (int)api.StatusCode);
                return new BillingException(BillingFailureKind.Unavailable, "The billing provider is unavailable right now.", ex);
            case ApiException api when api.StatusCode == HttpStatusCode.TooManyRequests:
                _logger.LogWarning("Maxio {Operation} rate limited (HTTP 429).", operation);
                return new BillingException(BillingFailureKind.Unavailable, "The billing provider is busy. Please retry shortly.", ex);
            case ResponseDeserializationException rde:
                _logger.LogError("Maxio {Operation} returned HTTP {Status} with a body that did not match {TargetType}.", operation, (int)rde.StatusCode, rde.TargetType.Name);
                return new BillingException(BillingFailureKind.Unavailable, "The billing provider returned a response that could not be processed.", ex);
            case ApiException api when (int)api.StatusCode is >= 400 and < 500:
                _logger.LogWarning("Maxio {Operation} refused with HTTP {Status}.", operation, (int)api.StatusCode);
                return new BillingException(BillingFailureKind.Rejected, "The billing provider rejected the request.", ex);
            case ApiException api:
                _logger.LogError("Maxio {Operation} failed with HTTP {Status}.", operation, (int)api.StatusCode);
                return new BillingException(BillingFailureKind.Unavailable, "The billing provider is unavailable right now.", ex);
            case SdkTimeoutException timeout:
                _logger.LogError("Maxio {Operation} did not answer within {Timeout}.", operation, timeout.Timeout);
                return new BillingException(BillingFailureKind.Unavailable, "The billing provider did not respond in time.", ex);
            case SdkConnectionException:
                _logger.LogError("Maxio {Operation} could not be reached: {Cause}", operation, ex.InnerException?.GetType().Name);
                return new BillingException(BillingFailureKind.Unavailable, "The billing provider could not be reached.", ex);
            default:
                _logger.LogError("Maxio {Operation} failed: {Failure}", operation, ex.GetType().Name);
                return new BillingException(BillingFailureKind.Unavailable, "The billing provider is unavailable right now.", ex);
        }
    }

    private static string? DescribeCustomerErrors(CreateCustomerError error)
    {
        if (error.TryGetCustomerErrorResponse1(out var body) && body.Errors is not null)
        {
            if (body.Errors.TryGetListOfString(out var messages))
            {
                return string.Join("; ", messages);
            }

            if (body.Errors.TryGetCustomerError(out var customerError))
            {
                return customerError.Customer;
            }
        }

        return null;
    }

    private static string? DescribeSubscriptionErrors(CreateSubscriptionError error)
    {
        if (error.TryGetErrorListResponse1(out var body))
        {
            return string.Join("; ", body.Errors);
        }

        return null;
    }

    private static string Describe(Exception ex) => ex switch
    {
        ApiException api => $"{ex.GetType().Name} HTTP {(int)api.StatusCode}",
        _ => ex.GetType().Name
    };

    private static BillingException TimedOut() =>
        new(BillingFailureKind.Unavailable, "The billing provider did not respond in time.");

    // ------------------------------------------------------------------ mapping & helpers

    private static readonly SubscriptionState[] EndedStates =
    [
        SubscriptionState.Canceled,
        SubscriptionState.Expired,
        SubscriptionState.FailedToCreate
    ];

    private static bool IsLive(MaxioSubscription subscription) =>
        subscription.State is null || !EndedStates.Contains(subscription.State);

    private static SubscribeResult SameOrConflict(string existingHandle, string requestedHandle, MaxioSubscription existing)
    {
        if (string.Equals(existingHandle, requestedHandle, StringComparison.Ordinal))
        {
            return new SubscribeResult(SubscribeOutcome.AlreadySubscribed, ToDetails(existing));
        }

        throw new BillingException(BillingFailureKind.Conflict,
            $"You already have an active subscription to '{existingHandle}'. Changing plans is not supported here.");
    }

    internal static SubscriptionDetails ToDetails(MaxioSubscription s)
    {
        MaxioProduct? product = s.Product;
        return new SubscriptionDetails(
            s.Id ?? 0,
            product?.Handle,
            product?.Name,
            s.State?.Value ?? "unknown",
            CentsToAmount(s.ProductPriceInCents ?? product?.PriceInCents),
            s.Currency,
            product?.Interval,
            product?.IntervalUnit?.Value,
            s.CurrentPeriodEndsAt ?? s.NextAssessmentAt,
            s.CreatedAt);
    }

    private static decimal? CentsToAmount(long? cents) => cents is long c ? c / 100m : null;

    private static string FirstNameFor(Subscriber subscriber)
    {
        var source = string.IsNullOrWhiteSpace(subscriber.Email) ? subscriber.UserName : subscriber.Email;
        var at = source.IndexOf('@');
        var local = at > 0 ? source[..at] : source;
        return string.IsNullOrWhiteSpace(local) ? "eShop" : local;
    }

    private DateTimeOffset Now => _clock.GetUtcNow();

    private bool IsStale(DateTimeOffset updatedAt) => Now - updatedAt > StaleClaimAge;

    private static CancellationTokenSource Budget(CancellationToken callerToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        cts.CancelAfter(CallBudget);
        return cts;
    }

    /// <summary>Signals that a write's outcome could not be confirmed; the claim is kept as Unknown.</summary>
    private sealed class OutcomeUnknownException : Exception
    {
    }
}
