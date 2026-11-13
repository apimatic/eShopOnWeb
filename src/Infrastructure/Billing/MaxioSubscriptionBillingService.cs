using System;
using System.Collections.Generic;
using System.Globalization;
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
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Subscription billing with Maxio Advanced Billing as the system of record.
/// <para>
/// Duplicate prevention: every write to Maxio is preceded by a claim row in <see cref="CatalogContext"/>
/// whose primary key refuses a concurrent second request (claim → SDK call → record result).
/// Unknown outcomes (timeout, dropped connection, 5xx, unreadable 2xx) are settled by re-reading Maxio by the
/// reference that was sent, never reported as plain failures.
/// </para>
/// </summary>
public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    /// <summary>A pending claim older than this is considered abandoned and may be taken over.</summary>
    internal static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(2);

    /// <summary>Time allowed to settle an unknown outcome, independent of the caller's (possibly spent) budget.</summary>
    private static readonly TimeSpan SettleBudget = TimeSpan.FromSeconds(10);

    internal const int PlanPageSize = 200; // Maxio's per_page maximum
    internal const int MaxPlanPages = 10;
    private const int MaxClaimAttempts = 3;

    private readonly MaxioAdvancedBillingClient _maxio;
    private readonly CatalogContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IMemoryCache _cache;
    private readonly MaxioSettings _settings;
    private readonly TimeProvider _clock;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient maxio,
        CatalogContext db,
        UserManager<ApplicationUser> users,
        IMemoryCache cache,
        IOptions<MaxioSettings> settings,
        TimeProvider clock,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _maxio = maxio;
        _db = db;
        _users = users;
        _cache = cache;
        _settings = settings.Value;
        _clock = clock;
        _logger = logger;
    }

    private DateTimeOffset Now => _clock.GetUtcNow();

    // ------------------------------------------------------------------ plans

    public async Task<SubscriptionPlanCatalog> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        using var budget = CreateBudget(cancellationToken);
        try
        {
            return await GetPlanCatalogAsync(budget.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BillingUnavailableException("The billing system did not respond in time.");
        }
    }

    private async Task<SubscriptionPlanCatalog> GetPlanCatalogAsync(CancellationToken ct)
    {
        var cacheKey = $"maxio:plans:{_settings.ProductFamilyHandle}";
        if (_settings.PlanCacheSeconds > 0 && _cache.TryGetValue(cacheKey, out SubscriptionPlanCatalog? cached) && cached is not null)
        {
            return cached;
        }

        var catalog = await ReadPlanCatalogAsync(ct);
        if (_settings.PlanCacheSeconds > 0)
        {
            _cache.Set(cacheKey, catalog, TimeSpan.FromSeconds(_settings.PlanCacheSeconds));
        }
        return catalog;
    }

    /// <summary>
    /// Walks the family's products page by page (the SDK does not paginate this operation), bounded by
    /// <see cref="MaxPlanPages"/>. A walk stopped by the cap is reported via <see cref="SubscriptionPlanCatalog.IsComplete"/>.
    /// </summary>
    private async Task<SubscriptionPlanCatalog> ReadPlanCatalogAsync(CancellationToken ct)
    {
        var plans = new List<SubscriptionPlan>();
        var familyId = "handle:" + _settings.ProductFamilyHandle!.Trim();

        for (var page = 1; page <= MaxPlanPages; page++)
        {
            IReadOnlyList<ProductResponse> batch;
            try
            {
                batch = await _maxio.ProductFamilies.ListProductsForProductFamily(
                    new ListProductsForProductFamilyRequest
                    {
                        ProductFamilyId = familyId,
                        Page = page,
                        PerPage = PlanPageSize
                    },
                    cancellationToken: ct);
            }
            catch (ApiException<ListProductsForProductFamilyError> ex)
            {
                throw ex.Error.Match(
                    onString: _ =>
                    {
                        _logger.LogError("Maxio product family configured in Maxio:ProductFamilyHandle was not found (HTTP {Status}).", (int)ex.StatusCode);
                        return (Exception)new BillingIntegrationException("The subscription catalog is not available.", ex);
                    },
                    otherwise: _ => TranslateStatus(ex.StatusCode, "list subscription plans", ex));
            }
            catch (SdkException ex)
            {
                throw TranslateReadFailure(ex, "list subscription plans");
            }

            foreach (var product in batch.Select(r => r.Product))
            {
                if (product is null
                    || product.ArchivedAt is not null
                    || product.RequireCreditCard == true // this flow captures no payment method
                    || string.IsNullOrWhiteSpace(product.Handle)
                    || product.PriceInCents is null)
                {
                    continue;
                }

                plans.Add(new SubscriptionPlan(
                    product.Handle,
                    product.Name ?? product.Handle,
                    product.Description,
                    product.PriceInCents.Value,
                    product.Interval,
                    product.IntervalUnit?.Value));
            }

            if (batch.Count < PlanPageSize)
            {
                return new SubscriptionPlanCatalog(plans, IsComplete: true);
            }
        }

        _logger.LogWarning("Maxio plan list was cut short after {Pages} pages of {PageSize}.", MaxPlanPages, PlanPageSize);
        return new SubscriptionPlanCatalog(plans, IsComplete: false);
    }

    /// <summary>Cross-operation invariant: the plan handle must be one the plan list offers.</summary>
    private async Task<SubscriptionPlan> RequireOfferedPlanAsync(string planHandle, CancellationToken ct)
    {
        var catalog = await GetPlanCatalogAsync(ct);
        var plan = catalog.Plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.Ordinal));
        if (plan is not null)
        {
            return plan;
        }

        if (!catalog.IsComplete)
        {
            // Cannot tell "not offered" from "beyond the page cap".
            throw new BillingUnavailableException("The plan list could not be read completely; please retry later.");
        }

        throw new SubscriptionPlanNotOfferedException(planHandle);
    }

    // ------------------------------------------------------------------ subscribe

    public async Task<SubscribeResult> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken = default)
    {
        var planHandle = command.PlanHandle?.Trim();
        if (string.IsNullOrEmpty(planHandle))
        {
            throw new SubscriptionPlanNotOfferedException(string.Empty);
        }

        using var budget = CreateBudget(cancellationToken);
        try
        {
            var plan = await RequireOfferedPlanAsync(planHandle, budget.Token);
            var identity = await ResolveIdentityAsync(command.UserName);
            var customerId = await EnsureCustomerAsync(identity, command.FirstName, command.LastName, budget.Token, cancellationToken);

            var (claim, existing) = await ClaimSubscriptionAsync(identity.Reference, plan, budget.Token);
            if (existing is not null)
            {
                return existing;
            }

            Subscription? live;
            try
            {
                // Provider-side guard that survives the loss of local claims (e.g. an in-memory store restart):
                // never create a second live subscription to the same plan for this customer.
                live = await FindLiveSubscriptionForPlanAsync(customerId, plan.Handle, budget.Token);
            }
            catch
            {
                await ReleaseClaimAsync(claim!);
                throw;
            }

            if (live?.Id is int liveId)
            {
                claim!.MarkCreated(liveId, Now, live.Reference);
                await SaveStateAsync("adopt existing subscription");
                _logger.LogInformation("Maxio subscription {SubscriptionId} already exists for plan {PlanHandle}; returned unchanged.", liveId, plan.Handle);
                return new SubscribeResult(SubscribeOutcome.AlreadySubscribed, plan.Handle, claim.SubscriptionReference, ToDetails(live, plan));
            }

            return await CreateSubscriptionAsync(claim!, customerId, plan, budget.Token, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BillingUnavailableException("The billing system did not respond in time.");
        }
    }

    /// <summary>
    /// Claim → (re-read) → result. Inserts the (user, plan) claim row; the store's primary key refuses a second
    /// concurrent insert, which is caught here. Returns either the claim to proceed with, or the existing result.
    /// </summary>
    private async Task<(SubscriptionClaim? Claim, SubscribeResult? Existing)> ClaimSubscriptionAsync(
        string customerReference, SubscriptionPlan plan, CancellationToken ct)
    {
        for (var attempt = 0; attempt < MaxClaimAttempts; attempt++)
        {
            var existing = await _db.SubscriptionClaims
                .FirstOrDefaultAsync(c => c.CustomerReference == customerReference && c.PlanHandle == plan.Handle, ct);

            if (existing is null)
            {
                var claim = new SubscriptionClaim(customerReference, plan.Handle, NewSubscriptionReference(), Now);
                _db.SubscriptionClaims.Add(claim);
                try
                {
                    await _db.SaveChangesAsync(ct);
                    return (claim, null);
                }
                catch (Exception ex) when (StoreErrors.IsDuplicateKey(ex))
                {
                    // Another request for the same user + plan claimed first.
                    _db.ChangeTracker.Clear();
                    continue;
                }
            }

            switch (existing.Status)
            {
                case SubscriptionClaimStatus.Pending when existing.IsInFlight(Now, ClaimLease):
                    throw new SubscriptionRequestInProgressException(
                        "A subscription request for this plan is already being processed. Please retry shortly.");

                case SubscriptionClaimStatus.Created:
                {
                    var current = await FindSubscriptionByReferenceAsync(existing.SubscriptionReference, ct);
                    if (current is not null && IsLive(current))
                    {
                        return (null, new SubscribeResult(SubscribeOutcome.AlreadySubscribed, plan.Handle, existing.SubscriptionReference, ToDetails(current, plan)));
                    }

                    // The earlier subscription ended (or is not found by reference): a new one may be created.
                    existing.Reclaim(NewSubscriptionReference(), Now);
                    break;
                }

                default:
                {
                    // Stale pending or unknown: settle the earlier attempt before anything else is sent.
                    var earlier = await FindSubscriptionByReferenceAsync(existing.SubscriptionReference, ct);
                    if (earlier?.Id is int earlierId)
                    {
                        existing.MarkCreated(earlierId, Now);
                        if (IsLive(earlier))
                        {
                            await SaveStateAsync("settle earlier subscription attempt");
                            _logger.LogWarning("Settled earlier subscription attempt {SubscriptionReference}: Maxio subscription {SubscriptionId} exists.", existing.SubscriptionReference, earlierId);
                            return (null, new SubscribeResult(SubscribeOutcome.AlreadySubscribed, plan.Handle, existing.SubscriptionReference, ToDetails(earlier, plan)));
                        }
                        existing.Reclaim(NewSubscriptionReference(), Now);
                    }
                    else
                    {
                        // Not created: retry with the SAME reference so a late-landing earlier attempt stays findable.
                        existing.Reclaim(existing.SubscriptionReference, Now);
                    }
                    break;
                }
            }

            try
            {
                await _db.SaveChangesAsync(ct);
                return (existing, null);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Another request took the claim over between our read and our write.
                _db.ChangeTracker.Clear();
            }
        }

        throw new SubscriptionRequestInProgressException(
            "A subscription request for this plan is already being processed. Please retry shortly.");
    }

    private async Task<SubscribeResult> CreateSubscriptionAsync(
        SubscriptionClaim claim, int customerId, SubscriptionPlan plan, CancellationToken budgetToken, CancellationToken callerToken)
    {
        Exception unknownCause;
        try
        {
            var response = await _maxio.Subscriptions.CreateSubscription(
                new CreateSubscriptionOperationRequest
                {
                    Body = new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = plan.Handle,
                            CustomerId = customerId,
                            Reference = claim.SubscriptionReference,
                            PaymentCollectionMethod = CollectionMethodSetting()
                        }
                    }
                },
                cancellationToken: budgetToken);

            if (response.Subscription?.Id is int subscriptionId)
            {
                claim.MarkCreated(subscriptionId, Now);
                await SaveStateAsync("record created subscription");
                _logger.LogInformation("Created Maxio subscription {SubscriptionId} ({SubscriptionReference}) for plan {PlanHandle}, state {State}.",
                    subscriptionId, claim.SubscriptionReference, plan.Handle, response.Subscription.State?.Value);
                return new SubscribeResult(SubscribeOutcome.Created, plan.Handle, claim.SubscriptionReference, ToDetails(response.Subscription, plan));
            }

            unknownCause = new InvalidOperationException("Maxio returned a success response without a subscription id.");
        }
        catch (ApiException<CreateSubscriptionError> ex) when (IsDefinitiveRejection(ex.StatusCode))
        {
            await ReleaseClaimAsync(claim);
            if (IsOurCredentialProblem(ex.StatusCode) || ex.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throw TranslateStatus(ex.StatusCode, "create subscription", ex);
            }

            var errors = ex.Error.Match(
                onErrorListResponse1: body => body.Errors,
                otherwise: _ => (IReadOnlyList<string>)Array.Empty<string>());
            _logger.LogWarning("Maxio rejected subscription to plan {PlanHandle} (HTTP {Status}): {Errors}", plan.Handle, (int)ex.StatusCode, string.Join("; ", errors));
            throw new BillingRequestRejectedException(errors, ex);
        }
        catch (ApiException<CreateSubscriptionError> ex)
        {
            unknownCause = ex; // 408 / 5xx: Maxio may have acted
        }
        catch (ResponseDeserializationException ex) when (IsDefinitiveRejection(ex.StatusCode))
        {
            await ReleaseClaimAsync(claim);
            if (IsOurCredentialProblem(ex.StatusCode) || ex.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throw TranslateStatus(ex.StatusCode, "create subscription", ex);
            }
            _logger.LogWarning("Maxio rejected subscription to plan {PlanHandle} (HTTP {Status}) with an unreadable error body.", plan.Handle, (int)ex.StatusCode);
            throw new BillingRequestRejectedException(Array.Empty<string>(), ex);
        }
        catch (ResponseDeserializationException ex)
        {
            unknownCause = ex; // unreadable 2xx/5xx: outcome unknown
        }
        catch (SdkConnectionException ex)
        {
            unknownCause = ex; // includes SdkTimeoutException: the request may have reached Maxio
        }
        catch (OperationCanceledException ex)
        {
            unknownCause = ex; // budget expired or caller went away mid-write
        }
        catch (AuthSchemeException ex)
        {
            await ReleaseClaimAsync(claim);
            _logger.LogError(ex, "Maxio credentials are not configured; nothing was sent.");
            throw new BillingIntegrationException("The billing integration is not configured correctly.", ex);
        }

        var settled = await SettleUnknownSubscriptionAsync(claim, customerId, plan, unknownCause);
        if (settled is null && callerToken.IsCancellationRequested)
        {
            callerToken.ThrowIfCancellationRequested();
        }
        return settled ?? throw new BillingUnavailableException(
            "The billing system did not confirm the subscription. No subscription was found; please retry.");
    }

    /// <summary>
    /// Settles a create whose outcome is unknown by re-reading Maxio by the reference that was sent.
    /// Found → recorded as created. Read fails → recorded as unknown and returned as pending confirmation
    /// (settled by the next subscribe / my-subscriptions request). Not found → recorded as unknown with the same
    /// reference (so the retry reuses it) and <c>null</c> is returned.
    /// </summary>
    private async Task<SubscribeResult?> SettleUnknownSubscriptionAsync(SubscriptionClaim claim, int customerId, SubscriptionPlan plan, Exception cause)
    {
        _logger.LogWarning("Outcome of Maxio create-subscription {SubscriptionReference} is unknown ({Cause}); re-reading.", claim.SubscriptionReference, cause.GetType().Name);

        using var settle = new CancellationTokenSource(SettleBudget);
        Subscription? found;
        try
        {
            found = await FindSubscriptionByReferenceAsync(claim.SubscriptionReference, settle.Token)
                    ?? await FindSubscriptionInCustomerListAsync(customerId, claim.SubscriptionReference, settle.Token);
        }
        catch (Exception ex) when (ex is BillingException or OperationCanceledException)
        {
            claim.MarkUnknown(Now);
            await SaveStateAsync("record unknown subscription outcome");
            _logger.LogWarning("Could not settle Maxio subscription {SubscriptionReference}; recorded as unknown for later settlement.", claim.SubscriptionReference);
            return new SubscribeResult(SubscribeOutcome.PendingConfirmation, plan.Handle, claim.SubscriptionReference, null);
        }

        if (found?.Id is int id)
        {
            claim.MarkCreated(id, Now);
            await SaveStateAsync("record settled subscription");
            _logger.LogInformation("Settled Maxio subscription {SubscriptionReference}: created as {SubscriptionId}.", claim.SubscriptionReference, id);
            return new SubscribeResult(SubscribeOutcome.Created, plan.Handle, claim.SubscriptionReference, ToDetails(found, plan));
        }

        claim.MarkUnknown(Now);
        await SaveStateAsync("record unconfirmed subscription");
        return null;
    }

    // ------------------------------------------------------------------ my subscriptions

    public async Task<CustomerSubscriptions> GetSubscriptionsAsync(string userName, CancellationToken cancellationToken = default)
    {
        using var budget = CreateBudget(cancellationToken);
        try
        {
            var identity = await ResolveIdentityAsync(userName);
            var ct = budget.Token;

            var link = await _db.BillingCustomerLinks.AsNoTracking()
                .FirstOrDefaultAsync(l => l.CustomerReference == identity.Reference, ct);
            var customerId = link is { IsLinked: true } ? link.BillingCustomerId : null;

            if (customerId is null)
            {
                // Not linked locally (never subscribed here, or local state was lost): ask Maxio. Never create on read.
                var customer = await ReadCustomerByReferenceAsync(identity.Reference, ct);
                customerId = customer?.Id;
                if (customerId is int id && link is null)
                {
                    await RecordLinkAsync(identity.Reference, id);
                }
            }

            var pending = await SettleOpenClaimsAsync(identity.Reference, customerId, ct);

            if (customerId is not int billingCustomerId)
            {
                return new CustomerSubscriptions(null, Array.Empty<SubscriptionDetails>(), pending);
            }

            var subscriptions = await ListCustomerSubscriptionsAsync(billingCustomerId, ct);
            var details = subscriptions
                .Where(s => s.Id is not null)
                .OrderByDescending(s => s.CreatedAt)
                .Select(s => ToDetails(s, plan: null))
                .ToList();

            return new CustomerSubscriptions(billingCustomerId, details, pending);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BillingUnavailableException("The billing system did not respond in time.");
        }
    }

    /// <summary>Settles unknown / abandoned subscribe attempts for this user; returns those still open.</summary>
    private async Task<IReadOnlyList<PendingSubscription>> SettleOpenClaimsAsync(string customerReference, int? customerId, CancellationToken ct)
    {
        var open = await _db.SubscriptionClaims
            .Where(c => c.CustomerReference == customerReference && c.Status != SubscriptionClaimStatus.Created)
            .ToListAsync(ct);

        var stillOpen = new List<PendingSubscription>();
        foreach (var claim in open)
        {
            if (claim.IsInFlight(Now, ClaimLease))
            {
                stillOpen.Add(ToPending(claim));
                continue;
            }

            Subscription? found;
            try
            {
                found = await FindSubscriptionByReferenceAsync(claim.SubscriptionReference, ct);
                if (found is null && customerId is int id)
                {
                    found = await FindSubscriptionInCustomerListAsync(id, claim.SubscriptionReference, ct);
                }
            }
            catch (BillingException)
            {
                stillOpen.Add(ToPending(claim)); // cannot settle now; try again next time
                continue;
            }

            if (found?.Id is int subscriptionId)
            {
                claim.MarkCreated(subscriptionId, Now);
                _logger.LogInformation("Settled Maxio subscription {SubscriptionReference}: created as {SubscriptionId}.", claim.SubscriptionReference, subscriptionId);
            }
            else if (Now - claim.UpdatedAt <= ClaimLease)
            {
                // Recently failed write not visible yet: keep it open; a late-landing create is still findable.
                stillOpen.Add(ToPending(claim));
            }
            else
            {
                // Abandoned past its lease and not in Maxio: the attempt did not happen.
                _db.SubscriptionClaims.Remove(claim);
                _logger.LogWarning("Released Maxio subscription attempt {SubscriptionReference}: not found in Maxio.", claim.SubscriptionReference);
            }
        }

        await SaveStateAsync("settle open subscription claims");
        return stillOpen;
    }

    // ------------------------------------------------------------------ customer

    /// <summary>
    /// Ensures a Maxio customer exists for the user: claim (local primary key) → read by reference →
    /// create with the same reference → record the link. The customer <c>reference</c> is also unique in Maxio,
    /// so even a lost local claim cannot produce a second customer.
    /// </summary>
    private async Task<int> EnsureCustomerAsync(UserIdentity identity, string? firstName, string? lastName, CancellationToken budgetToken, CancellationToken callerToken)
    {
        var claim = await ClaimCustomerAsync(identity.Reference, budgetToken);
        if (claim.IsLinked)
        {
            return claim.BillingCustomerId!.Value;
        }

        int customerId;
        try
        {
            var existing = await ReadCustomerByReferenceAsync(identity.Reference, budgetToken);
            customerId = existing?.Id
                         ?? await CreateCustomerAsync(identity, firstName, lastName, budgetToken, callerToken);
        }
        catch
        {
            await ReleaseClaimAsync(claim);
            throw;
        }

        claim.Link(customerId, Now);
        await SaveStateAsync("record customer link");
        return customerId;
    }

    private async Task<BillingCustomerLink> ClaimCustomerAsync(string customerReference, CancellationToken ct)
    {
        for (var attempt = 0; attempt < MaxClaimAttempts; attempt++)
        {
            var existing = await _db.BillingCustomerLinks.FirstOrDefaultAsync(l => l.CustomerReference == customerReference, ct);
            if (existing is null)
            {
                var claim = new BillingCustomerLink(customerReference, Now);
                _db.BillingCustomerLinks.Add(claim);
                try
                {
                    await _db.SaveChangesAsync(ct);
                    return claim;
                }
                catch (Exception ex) when (StoreErrors.IsDuplicateKey(ex))
                {
                    _db.ChangeTracker.Clear();
                    continue;
                }
            }

            if (existing.IsLinked)
            {
                return existing;
            }

            if (!existing.IsStale(Now, ClaimLease))
            {
                throw new SubscriptionRequestInProgressException(
                    "Your billing account is being set up by another request. Please retry shortly.");
            }

            existing.Reclaim(Now);
            try
            {
                await _db.SaveChangesAsync(ct);
                return existing;
            }
            catch (DbUpdateConcurrencyException)
            {
                _db.ChangeTracker.Clear();
            }
        }

        throw new SubscriptionRequestInProgressException(
            "Your billing account is being set up by another request. Please retry shortly.");
    }

    private async Task<int> CreateCustomerAsync(UserIdentity identity, string? firstName, string? lastName, CancellationToken budgetToken, CancellationToken callerToken)
    {
        var (first, last) = CustomerNames.Derive(identity.Email, firstName, lastName);
        Exception unknownCause;
        try
        {
            var response = await _maxio.Customers.CreateCustomer(
                new CreateCustomerOperationRequest
                {
                    Body = new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = first,
                            LastName = last,
                            Email = identity.Email,
                            Reference = identity.Reference
                        }
                    }
                },
                cancellationToken: budgetToken);

            if (response.Customer?.Id is int id)
            {
                _logger.LogInformation("Created Maxio customer {CustomerId}.", id);
                return id;
            }

            unknownCause = new InvalidOperationException("Maxio returned a success response without a customer id.");
        }
        catch (ApiException<CreateCustomerError> ex) when (IsDefinitiveRejection(ex.StatusCode))
        {
            if (IsOurCredentialProblem(ex.StatusCode) || ex.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throw TranslateStatus(ex.StatusCode, "create customer", ex);
            }

            // A duplicate-reference rejection means the customer already exists (a concurrent creator, or an earlier
            // attempt that landed): re-read by reference before reporting a rejection.
            var existing = await TrySettleCustomerAsync(identity.Reference);
            if (existing is int existingId)
            {
                return existingId;
            }

            var errors = ex.Error.Match(
                onCustomerErrorResponse1: body => CustomerErrorMessages(body),
                otherwise: _ => (IReadOnlyList<string>)Array.Empty<string>());
            _logger.LogWarning("Maxio rejected customer creation (HTTP {Status}): {Errors}", (int)ex.StatusCode, string.Join("; ", errors));
            throw new BillingRequestRejectedException(errors, ex);
        }
        catch (ApiException<CreateCustomerError> ex)
        {
            unknownCause = ex;
        }
        catch (ResponseDeserializationException ex)
        {
            unknownCause = ex;
        }
        catch (SdkConnectionException ex)
        {
            unknownCause = ex;
        }
        catch (OperationCanceledException ex)
        {
            unknownCause = ex;
        }
        catch (AuthSchemeException ex)
        {
            _logger.LogError(ex, "Maxio credentials are not configured; nothing was sent.");
            throw new BillingIntegrationException("The billing integration is not configured correctly.", ex);
        }

        // Unknown outcome: settle by reference (the reference is unique in Maxio, so a retry can never duplicate).
        _logger.LogWarning("Outcome of Maxio create-customer is unknown ({Cause}); re-reading by reference.", unknownCause.GetType().Name);
        var settled = await TrySettleCustomerAsync(identity.Reference);
        if (settled is int settledId)
        {
            return settledId;
        }

        callerToken.ThrowIfCancellationRequested();
        throw unknownCause is ResponseDeserializationException rde && IsDefinitiveRejection(rde.StatusCode) && !IsOurCredentialProblem(rde.StatusCode)
            ? new BillingRequestRejectedException(Array.Empty<string>(), unknownCause)
            : new BillingUnavailableException("The billing system did not confirm the customer account; please retry.", unknownCause);
    }

    private async Task<int?> TrySettleCustomerAsync(string customerReference)
    {
        using var settle = new CancellationTokenSource(SettleBudget);
        try
        {
            return (await ReadCustomerByReferenceAsync(customerReference, settle.Token))?.Id;
        }
        catch (Exception ex) when (ex is BillingException or OperationCanceledException)
        {
            return null;
        }
    }

    private async Task RecordLinkAsync(string customerReference, int customerId)
    {
        var link = new BillingCustomerLink(customerReference, Now);
        link.Link(customerId, Now);
        _db.BillingCustomerLinks.Add(link);
        try
        {
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex) when (StoreErrors.IsDuplicateKey(ex))
        {
            _db.ChangeTracker.Clear(); // someone else recorded it first
        }
    }

    // ------------------------------------------------------------------ Maxio reads

    private async Task<Customer?> ReadCustomerByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _maxio.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = reference },
                cancellationToken: ct);
            return response.Customer;
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null; // no customer with this reference
        }
        catch (SdkException ex)
        {
            throw TranslateReadFailure(ex, "read customer");
        }
    }

    private async Task<Subscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _maxio.Subscriptions.FindSubscription(
                new FindSubscriptionRequest { Reference = reference },
                cancellationToken: ct);
            return response.Subscription;
        }
        catch (ApiException<FindSubscriptionError> ex)
        {
            var notFound = ex.Error.Match(onNoContent: _ => true, otherwise: _ => false);
            if (notFound)
            {
                return null;
            }
            throw TranslateStatus(ex.StatusCode, "find subscription", ex);
        }
        catch (SdkException ex)
        {
            throw TranslateReadFailure(ex, "find subscription");
        }
    }

    private async Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct)
    {
        try
        {
            var response = await _maxio.Customers.ListCustomerSubscriptions(
                new ListCustomerSubscriptionsRequest { CustomerId = customerId },
                cancellationToken: ct);
            return response
                .Select(r => r.Subscription)
                .Where(s => s is not null)
                .Select(s => s!)
                .ToList();
        }
        catch (SdkException ex)
        {
            throw TranslateReadFailure(ex, "list customer subscriptions");
        }
    }

    private async Task<Subscription?> FindLiveSubscriptionForPlanAsync(int customerId, string planHandle, CancellationToken ct)
    {
        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, ct);
        return subscriptions
            .Where(s => string.Equals(s.Product?.Handle, planHandle, StringComparison.Ordinal) && IsLive(s))
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefault();
    }

    private async Task<Subscription?> FindSubscriptionInCustomerListAsync(int customerId, string reference, CancellationToken ct)
    {
        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, ct);
        return subscriptions.FirstOrDefault(s => string.Equals(s.Reference, reference, StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ helpers

    private CancellationTokenSource CreateBudget(CancellationToken callerToken)
    {
        var budget = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        budget.CancelAfter(TimeSpan.FromSeconds(_settings.RequestBudgetSeconds));
        return budget;
    }

    private async Task<UserIdentity> ResolveIdentityAsync(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new UserNotFoundException(userName ?? string.Empty);
        }

        var user = await _users.FindByNameAsync(userName) ?? throw new UserNotFoundException(userName);
        var email = !string.IsNullOrWhiteSpace(user.Email) ? user.Email.Trim()
            : userName.Contains('@') ? userName.Trim()
            : throw new BillingRequestRejectedException(new[] { "Your account has no e-mail address; one is required for billing." });

        // The user name is immutable in this application and survives database re-seeding (unlike the Identity
        // row id), so it is the stable reference Maxio knows the customer by.
        return new UserIdentity((user.UserName ?? userName).Trim().ToLowerInvariant(), email);
    }

    private async Task ReleaseClaimAsync(object claim)
    {
        try
        {
            _db.Remove(claim);
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Could not release billing claim; it expires after {Lease}.", ClaimLease);
            _db.ChangeTracker.Clear();
        }
    }

    /// <summary>Records a state change after a Maxio call; never cancelled, so a spent budget cannot lose the result.</summary>
    private async Task SaveStateAsync(string what)
    {
        try
        {
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        catch (DbUpdateException ex)
        {
            // Another request changed the claim meanwhile; Maxio remains the source of truth and the next
            // request re-reads it.
            _logger.LogWarning(ex, "Could not {What}; the next request will reconcile with Maxio.", what);
            _db.ChangeTracker.Clear();
        }
    }

    private static bool IsLive(Subscription subscription) =>
        subscription.State is null
        || !(subscription.State == SubscriptionState.Canceled
             || subscription.State == SubscriptionState.Expired
             || subscription.State == SubscriptionState.FailedToCreate);

    /// <summary>A 4xx that means Maxio refused the request without acting on it (408 is a timeout: unknown).</summary>
    private static bool IsDefinitiveRejection(HttpStatusCode status) =>
        (int)status is >= 400 and < 500 && status != HttpStatusCode.RequestTimeout;

    private static bool IsOurCredentialProblem(HttpStatusCode status) =>
        status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;

    private Exception TranslateStatus(HttpStatusCode status, string operation, Exception ex)
    {
        if (IsOurCredentialProblem(status))
        {
            _logger.LogError("Maxio refused our credentials during {Operation} (HTTP {Status}). Check Maxio:ApiKey / Maxio:Subdomain.", operation, (int)status);
            return new BillingIntegrationException("The billing integration is not configured correctly.", ex);
        }

        if (status is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout || (int)status >= 500)
        {
            _logger.LogWarning("Maxio unavailable during {Operation} (HTTP {Status}).", operation, (int)status);
            return new BillingUnavailableException("The billing system is temporarily unavailable; please retry.", ex);
        }

        _logger.LogError("Maxio returned an unexpected HTTP {Status} during {Operation}.", (int)status, operation);
        return new BillingIntegrationException("The billing system returned an unexpected response.", ex);
    }

    /// <summary>Shared fallback for read failures (Case B raw errors, unreadable bodies, transport, auth).</summary>
    private Exception TranslateReadFailure(SdkException ex, string operation)
    {
        switch (ex)
        {
            case ResponseDeserializationException rde:
                _logger.LogError(rde, "Maxio response for {Operation} could not be read (HTTP {Status}).", operation, (int)rde.StatusCode);
                return new BillingIntegrationException("The billing system returned a response that could not be processed.", ex);
            case ApiException api:
                return TranslateStatus(api.StatusCode, operation, ex);
            case SdkTimeoutException timeout:
                _logger.LogWarning("Maxio did not answer {Operation} within {Timeout}.", operation, timeout.Timeout);
                return new BillingUnavailableException("The billing system did not respond in time.", ex);
            case SdkConnectionException:
                _logger.LogWarning(ex, "Maxio unreachable during {Operation}.", operation);
                return new BillingUnavailableException("The billing system is unreachable; please retry.", ex);
            case AuthSchemeException:
                _logger.LogError(ex, "Maxio credentials are not configured.");
                return new BillingIntegrationException("The billing integration is not configured correctly.", ex);
            default:
                _logger.LogError(ex, "Unexpected Maxio failure during {Operation}.", operation);
                return new BillingIntegrationException("The billing system call failed.", ex);
        }
    }

    private static IReadOnlyList<string> CustomerErrorMessages(CustomerErrorResponse1 body) =>
        body.Errors is null
            ? Array.Empty<string>()
            : body.Errors.Match(
                onCustomerError: e => e.Customer is null ? Array.Empty<string>() : new[] { e.Customer },
                onListOfString: list => list,
                otherwise: _ => (IReadOnlyList<string>)Array.Empty<string>());

    /// <summary>Maps Maxio:PaymentCollectionMethod (validated at startup) onto the SDK's open enum.</summary>
    private CollectionMethod CollectionMethodSetting() =>
        CollectionMethod.TryGetKnownValue(_settings.PaymentCollectionMethod.Trim().ToLowerInvariant(), out var known)
            ? known
            : CollectionMethod.Remittance;

    private static string NewSubscriptionReference() => "eshop-sub-" + Guid.NewGuid().ToString("N");

    private static PendingSubscription ToPending(SubscriptionClaim claim) =>
        new(claim.PlanHandle, claim.SubscriptionReference,
            claim.Status == SubscriptionClaimStatus.Unknown ? "pending_confirmation" : "processing",
            claim.ClaimedAt);

    internal static SubscriptionDetails ToDetails(Subscription subscription, SubscriptionPlan? plan) =>
        new(
            subscription.Id ?? 0,
            subscription.Reference,
            subscription.Product?.Handle ?? plan?.Handle,
            subscription.Product?.Name ?? plan?.Name,
            subscription.State?.Value ?? "unknown",
            subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? plan?.PriceInCents,
            subscription.Currency,
            subscription.CurrentPeriodEndsAt,
            // Next charge attempt; falls back to the end of the current period.
            subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            subscription.CreatedAt,
            subscription.ActivatedAt,
            subscription.CanceledAt);

    private sealed record UserIdentity(string Reference, string Email);
}

/// <summary>Derives the first/last name Maxio requires from what the caller supplied or the e-mail address.</summary>
internal static class CustomerNames
{
    public static (string First, string Last) Derive(string email, string? firstName, string? lastName)
    {
        var local = email.Split('@')[0];
        var parts = local.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);

        var first = !string.IsNullOrWhiteSpace(firstName) ? firstName.Trim()
            : parts.Length > 0 ? Capitalize(parts[0]) : "eShop";
        var last = !string.IsNullOrWhiteSpace(lastName) ? lastName.Trim()
            : parts.Length > 1 ? string.Join(" ", parts.Skip(1).Select(Capitalize)) : "Customer";
        return (first, last);
    }

    private static string Capitalize(string value) =>
        value.Length == 0 ? value : char.ToUpper(value[0], CultureInfo.InvariantCulture) + value[1..];
}
