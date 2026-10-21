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
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Subscription billing with Maxio Advanced Billing as the system of record.
/// <list type="bullet">
/// <item>Every provider write is preceded by a durable claim (<see cref="BillingClaimStore"/>), so a
/// double-submit reaches Maxio at most once.</item>
/// <item>A write whose outcome is unknown (timeout, dropped connection, 5xx, unreadable 2xx) is settled by
/// re-reading Maxio by the reference that was sent, before the caller gets an answer.</item>
/// <item>Every public operation runs under one total time budget linked to the caller's cancellation.</item>
/// </list>
/// </summary>
public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const string UnavailableMessage = "The billing service is temporarily unavailable. Please try again shortly.";

    private readonly MaxioAdvancedBillingClient _client;
    private readonly BillingClaimStore _claims;
    private readonly IMemoryCache _cache;
    private readonly MaxioOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        BillingClaimStore claims,
        IMemoryCache cache,
        IOptions<MaxioOptions> options,
        TimeProvider clock,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _claims = claims;
        _cache = cache;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    private string FamilyHandle => _options.ProductFamilyHandle!.Trim();

    // ------------------------------------------------------------------ plans

    public async Task<BillingResult<SubscriptionPlanCatalog>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        using var budget = Budget(cancellationToken, MaxioBillingDefaults.ReadBudget);
        return await GetPlanCatalogAsync(budget.Token, cancellationToken);
    }

    private async Task<BillingResult<SubscriptionPlanCatalog>> GetPlanCatalogAsync(CancellationToken token, CancellationToken callerToken)
    {
        var cacheKey = $"maxio:plans:{FamilyHandle}";
        if (_cache.TryGetValue(cacheKey, out SubscriptionPlanCatalog? cached) && cached is not null)
        {
            return BillingResult<SubscriptionPlanCatalog>.Success(cached);
        }

        var plans = new List<SubscriptionPlan>();
        var complete = false;
        for (var page = 1; page <= MaxioBillingDefaults.MaxProductPages; page++)
        {
            var call = await ListProductsPageAsync(page, token, callerToken);
            if (!call.Ok)
            {
                return BillingResult<SubscriptionPlanCatalog>.Failure(BillingResultStatus.Unavailable, "Subscription plans are temporarily unavailable. Please try again shortly.");
            }

            foreach (var product in call.Value!.Select(r => r?.Product))
            {
                // Archived products are not offered, whether or not the provider filtered them already.
                if (product is null || string.IsNullOrWhiteSpace(product.Handle) || product.ArchivedAt is not null)
                {
                    continue;
                }
                plans.Add(MaxioMapping.ToPlan(product));
            }

            if (call.Value!.Count < MaxioBillingDefaults.ProductsPerPage)
            {
                complete = true;
                break;
            }
        }

        if (!complete)
        {
            _logger.LogWarning("Maxio product family {ProductFamilyHandle} has more than {MaxProducts} products; the plan list is truncated.",
                FamilyHandle, MaxioBillingDefaults.ProductsPerPage * MaxioBillingDefaults.MaxProductPages);
        }

        var catalog = new SubscriptionPlanCatalog(FamilyHandle, plans, IsTruncated: !complete);
        if (complete)
        {
            _cache.Set(cacheKey, catalog, MaxioBillingDefaults.PlanCacheDuration);
        }
        return BillingResult<SubscriptionPlanCatalog>.Success(catalog);
    }

    // ------------------------------------------------------------------ subscribe

    public async Task<BillingResult<SubscribeOutcome>> SubscribeAsync(BillingAccount account, string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            return BillingResult<SubscribeOutcome>.Failure(BillingResultStatus.Invalid, "A planHandle is required.");
        }
        planHandle = planHandle.Trim();

        using var budget = Budget(cancellationToken, MaxioBillingDefaults.WriteBudget);
        var token = budget.Token;

        // The plan must be one this application offers (the configured family's live listing),
        // not merely a product that exists somewhere on the site.
        var catalog = await GetPlanCatalogAsync(token, cancellationToken);
        if (!catalog.IsSuccess)
        {
            return catalog.As<SubscribeOutcome>();
        }
        var plan = catalog.Value!.Plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.Ordinal));
        if (plan is null)
        {
            return catalog.Value.IsTruncated
                ? BillingResult<SubscribeOutcome>.Failure(BillingResultStatus.Unavailable, "The plan catalog is too large to validate this plan right now.")
                : BillingResult<SubscribeOutcome>.Failure(BillingResultStatus.Invalid, $"Unknown plan '{planHandle}'. Choose a handle from GET /api/subscription-plans.");
        }

        var customer = await EnsureCustomerAsync(account, token, cancellationToken);
        if (!customer.IsSuccess)
        {
            return customer.As<SubscribeOutcome>();
        }
        var customerId = customer.Value;

        // Two passes at most: the second handles an enrollment that was vacated, or a claim lost to a concurrent request.
        for (var pass = 0; pass < 2; pass++)
        {
            var existing = await _claims.FindEnrollmentAsync(account.UserId, cancellationToken);
            if (existing is not null)
            {
                var resolved = await ResolveExistingEnrollmentAsync(existing, customerId, plan, token, cancellationToken);
                if (resolved is not null)
                {
                    return resolved;
                }
            }

            var claim = SubscriptionEnrollment.Claim(account.UserId, plan.Handle, NewSubscriptionReference(), _clock.GetUtcNow());
            if (await _claims.TryClaimAsync(claim, cancellationToken))
            {
                return await EnrollAsync(account, claim, customerId, plan, token, cancellationToken);
            }

            _logger.LogInformation("Subscription claim for user {UserId} already held by another request.", account.UserId);
        }

        return InProgress<SubscribeOutcome>();
    }

    /// <summary>Returns the final answer for a user who already holds an enrollment, or null when the
    /// enrollment was vacated (its subscription ended, or a stale claim never reached Maxio).</summary>
    private async Task<BillingResult<SubscribeOutcome>?> ResolveExistingEnrollmentAsync(
        SubscriptionEnrollment existing, int customerId, SubscriptionPlan plan, CancellationToken token, CancellationToken callerToken)
    {
        if (!existing.IsCompleted)
        {
            var found = await FindEnrollmentSubscriptionAsync(existing.SubscriptionReference, customerId, token, callerToken);
            if (found.Ok)
            {
                await CompleteEnrollmentAsync(existing, found.Value!);
                return ExistingSubscriptionOutcome(found.Value!, existing.PlanHandle, plan);
            }
            if (found.Failure != CallFailure.NotFound)
            {
                return BillingResult<SubscribeOutcome>.Failure(BillingResultStatus.Unavailable, UnavailableMessage);
            }
            if (!existing.IsLeaseExpired(_clock.GetUtcNow(), MaxioBillingDefaults.ClaimLease))
            {
                return InProgress<SubscribeOutcome>();
            }

            _logger.LogWarning("Releasing stale subscription claim {SubscriptionReference} for user {UserId}: Maxio has no subscription with that reference.",
                existing.SubscriptionReference, existing.UserId);
            return await _claims.TryReleaseAsync(existing, CancellationToken.None) ? null : InProgress<SubscribeOutcome>();
        }

        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, token, callerToken);
        if (!subscriptions.Ok)
        {
            return BillingResult<SubscribeOutcome>.Failure(BillingResultStatus.Unavailable, UnavailableMessage);
        }
        var current = subscriptions.Value!.FirstOrDefault(s => s.Id == existing.BillingSubscriptionId);
        if (current is not null && MaxioMapping.IsLive(current.State))
        {
            return ExistingSubscriptionOutcome(current, existing.PlanHandle, plan);
        }

        // The recorded subscription ended (or is gone): free the slot so the shopper can subscribe again.
        return await _claims.TryReleaseAsync(existing, CancellationToken.None) ? null : InProgress<SubscribeOutcome>();
    }

    private async Task<BillingResult<SubscribeOutcome>> EnrollAsync(
        BillingAccount account, SubscriptionEnrollment claim, int customerId, SubscriptionPlan plan, CancellationToken token, CancellationToken callerToken)
    {
        // Maxio is the system of record: if it already holds a live subscription for this customer (created
        // before our records existed, or recorded by a lost row), adopt it instead of creating a second one.
        var current = await ListCustomerSubscriptionsAsync(customerId, token, callerToken);
        if (!current.Ok)
        {
            await ReleaseEnrollmentAsync(claim);
            return BillingResult<SubscribeOutcome>.Failure(BillingResultStatus.Unavailable, UnavailableMessage);
        }
        var live = current.Value!
            .Where(s => s.Id is not null && MaxioMapping.IsLive(s.State))
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefault();
        if (live is not null)
        {
            _logger.LogInformation("Maxio customer {MaxioCustomerId} already has live subscription {MaxioSubscriptionId}; adopting it.", customerId, live.Id);
            await CompleteEnrollmentAsync(claim, live);
            return ExistingSubscriptionOutcome(live, claim.PlanHandle, plan);
        }

        var created = await CreateSubscriptionAsync(plan.Handle, customerId, claim.SubscriptionReference, token, callerToken);
        switch (created.Failure)
        {
            case CallFailure.None:
                _logger.LogInformation("Created Maxio subscription {MaxioSubscriptionId} ({SubscriptionReference}) on plan {PlanHandle} for user {UserId}.",
                    created.Value!.Id, claim.SubscriptionReference, plan.Handle, account.UserId);
                await CompleteEnrollmentAsync(claim, created.Value!);
                return BillingResult<SubscribeOutcome>.Success(new SubscribeOutcome(MaxioMapping.ToSummary(created.Value!, plan.Handle), AlreadyExisted: false));

            case CallFailure.Rejected:
                await ReleaseEnrollmentAsync(claim);
                return BillingResult<SubscribeOutcome>.Failure(BillingResultStatus.Rejected, "The billing service declined the subscription.", created.Errors);

            case CallFailure.Unknown:
                return await SettleUnknownSubscriptionAsync(claim, customerId, plan);

            default:
                await ReleaseEnrollmentAsync(claim);
                return BillingResult<SubscribeOutcome>.Failure(BillingResultStatus.Unavailable, UnavailableMessage);
        }
    }

    /// <summary>The create may have landed at Maxio. Re-read by the reference we sent, on a fresh budget
    /// (the request's budget may be what expired), before answering.</summary>
    private async Task<BillingResult<SubscribeOutcome>> SettleUnknownSubscriptionAsync(SubscriptionEnrollment claim, int customerId, SubscriptionPlan plan)
    {
        _logger.LogWarning("Outcome of creating subscription {SubscriptionReference} is unknown; re-reading Maxio.", claim.SubscriptionReference);
        using var settle = new CancellationTokenSource(MaxioBillingDefaults.SettleBudget);
        var found = await FindEnrollmentSubscriptionAsync(claim.SubscriptionReference, customerId, settle.Token, CancellationToken.None);
        if (found.Ok)
        {
            _logger.LogWarning("Settled: subscription {SubscriptionReference} exists at Maxio as {MaxioSubscriptionId}.", claim.SubscriptionReference, found.Value!.Id);
            await CompleteEnrollmentAsync(claim, found.Value!);
            return BillingResult<SubscribeOutcome>.Success(new SubscribeOutcome(MaxioMapping.ToSummary(found.Value!, plan.Handle), AlreadyExisted: false));
        }

        // Not found yet, or the re-read failed too: the claim stays Pending (the "unknown" state). The next
        // subscribe or my-subscriptions call for this user re-reads before anything new is written.
        _logger.LogWarning("Subscription {SubscriptionReference} still unsettled ({Failure}); left pending for the next call.", claim.SubscriptionReference, found.Failure);
        return BillingResult<SubscribeOutcome>.Failure(BillingResultStatus.Pending,
            "Your subscription request was sent but is not confirmed yet. Check GET /api/my-subscriptions shortly; repeating this request is safe.");
    }

    /// <summary>Finds the subscription created for an enrollment by its reference; falls back to the
    /// customer's subscription list. A match must belong to the caller's Maxio customer.</summary>
    private async Task<ProviderCall<Subscription>> FindEnrollmentSubscriptionAsync(string reference, int customerId, CancellationToken token, CancellationToken callerToken)
    {
        var found = await FindSubscriptionByReferenceAsync(reference, token, callerToken);
        if (found.Ok)
        {
            var ownerId = found.Value!.Customer?.Id;
            if (ownerId is not null && ownerId != customerId)
            {
                _logger.LogError("Subscription reference {SubscriptionReference} belongs to Maxio customer {OwnerId}, not {MaxioCustomerId}; not adopting it.",
                    reference, ownerId, customerId);
                return ProviderCall<Subscription>.Failed(CallFailure.Unavailable);
            }
            return found;
        }
        if (found.Failure != CallFailure.NotFound)
        {
            return found;
        }

        var list = await ListCustomerSubscriptionsAsync(customerId, token, callerToken);
        if (!list.Ok)
        {
            return ProviderCall<Subscription>.Failed(list.Failure);
        }
        var match = list.Value!.FirstOrDefault(s => s.Id is not null && string.Equals(s.Reference, reference, StringComparison.Ordinal));
        return match is null ? ProviderCall<Subscription>.Failed(CallFailure.NotFound) : ProviderCall<Subscription>.Success(match);
    }

    private BillingResult<SubscribeOutcome> ExistingSubscriptionOutcome(Subscription subscription, string? recordedPlanHandle, SubscriptionPlan requested)
    {
        var summary = MaxioMapping.ToSummary(subscription, recordedPlanHandle);
        if (MaxioMapping.HasPlan(subscription, requested.Handle, recordedPlanHandle))
        {
            return BillingResult<SubscribeOutcome>.Success(new SubscribeOutcome(summary, AlreadyExisted: true));
        }
        return BillingResult<SubscribeOutcome>.Failure(BillingResultStatus.Conflict,
            $"You already have a live subscription (plan '{summary.PlanHandle ?? summary.PlanName ?? "unknown"}', state '{summary.State}'). Changing plans is not supported here.");
    }

    // ------------------------------------------------------------------ my subscriptions

    public async Task<BillingResult<IReadOnlyList<SubscriptionSummary>>> ListSubscriptionsAsync(BillingAccount account, CancellationToken cancellationToken = default)
    {
        using var budget = Budget(cancellationToken, MaxioBillingDefaults.ReadBudget);
        var token = budget.Token;

        int customerId;
        var link = await _claims.FindCustomerLinkAsync(account.UserId, cancellationToken);
        if (link?.IsCompleted == true)
        {
            customerId = link.BillingCustomerId!.Value;
        }
        else
        {
            // No local record (new shopper, or records lost): Maxio is the system of record.
            var lookup = await ReadCustomerByReferenceAsync(account.UserId, token, cancellationToken);
            if (lookup.Failure == CallFailure.NotFound)
            {
                return BillingResult<IReadOnlyList<SubscriptionSummary>>.Success(Array.Empty<SubscriptionSummary>());
            }
            if (!lookup.Ok)
            {
                return BillingResult<IReadOnlyList<SubscriptionSummary>>.Failure(BillingResultStatus.Unavailable, UnavailableMessage);
            }
            customerId = lookup.Value!.Id!.Value;
        }

        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, token, cancellationToken);
        if (!subscriptions.Ok)
        {
            return BillingResult<IReadOnlyList<SubscriptionSummary>>.Failure(BillingResultStatus.Unavailable, UnavailableMessage);
        }

        // Settle a pending enrollment along the way, from the list we already hold.
        var enrollment = await _claims.FindEnrollmentAsync(account.UserId, cancellationToken);
        if (enrollment is { IsCompleted: false })
        {
            var settled = subscriptions.Value!.FirstOrDefault(s => s.Id is not null && string.Equals(s.Reference, enrollment.SubscriptionReference, StringComparison.Ordinal));
            if (settled is not null)
            {
                await CompleteEnrollmentAsync(enrollment, settled);
            }
        }

        IReadOnlyList<SubscriptionSummary> result = subscriptions.Value!
            .Where(s => s.Id is not null)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => MaxioMapping.ToSummary(s))
            .ToList();
        return BillingResult<IReadOnlyList<SubscriptionSummary>>.Success(result);
    }

    // ------------------------------------------------------------------ customer

    /// <summary>Returns the caller's Maxio customer id, creating the customer at most once.
    /// The customer reference is the eShop user id, which Maxio keeps unique per site.</summary>
    private async Task<BillingResult<int>> EnsureCustomerAsync(BillingAccount account, CancellationToken token, CancellationToken callerToken)
    {
        var link = await _claims.FindCustomerLinkAsync(account.UserId, callerToken);
        if (link?.IsCompleted == true)
        {
            return BillingResult<int>.Success(link.BillingCustomerId!.Value);
        }

        if (link is null)
        {
            var claim = BillingCustomerLink.Claim(account.UserId, _clock.GetUtcNow());
            if (await _claims.TryClaimAsync(claim, callerToken))
            {
                return await CreateOrAdoptCustomerAsync(account, claim, token, callerToken);
            }

            // Another request claimed it first.
            link = await _claims.FindCustomerLinkAsync(account.UserId, callerToken);
            if (link is null)
            {
                return InProgress<int>();
            }
            if (link.IsCompleted)
            {
                return BillingResult<int>.Success(link.BillingCustomerId!.Value);
            }
        }

        // A pending claim: another request is creating the customer, or one died before it settled.
        var lookup = await ReadCustomerByReferenceAsync(account.UserId, token, callerToken);
        if (lookup.Ok)
        {
            await CompleteCustomerLinkAsync(link, lookup.Value!.Id!.Value);
            return BillingResult<int>.Success(lookup.Value!.Id!.Value);
        }
        if (lookup.Failure != CallFailure.NotFound)
        {
            return BillingResult<int>.Failure(BillingResultStatus.Unavailable, UnavailableMessage);
        }
        if (link.IsLeaseExpired(_clock.GetUtcNow(), MaxioBillingDefaults.ClaimLease)
            && await _claims.TryUpdateAsync(link, l => l.Renew(_clock.GetUtcNow()), callerToken))
        {
            _logger.LogWarning("Took over stale customer claim for user {UserId}.", account.UserId);
            return await CreateOrAdoptCustomerAsync(account, link, token, callerToken);
        }
        return InProgress<int>();
    }

    private async Task<BillingResult<int>> CreateOrAdoptCustomerAsync(BillingAccount account, BillingCustomerLink claim, CancellationToken token, CancellationToken callerToken)
    {
        // Adopt an existing Maxio customer with this reference (e.g. our local record was lost).
        var lookup = await ReadCustomerByReferenceAsync(account.UserId, token, callerToken);
        if (lookup.Ok)
        {
            await CompleteCustomerLinkAsync(claim, lookup.Value!.Id!.Value);
            return BillingResult<int>.Success(lookup.Value!.Id!.Value);
        }
        if (lookup.Failure != CallFailure.NotFound)
        {
            await ReleaseCustomerLinkAsync(claim);
            return BillingResult<int>.Failure(BillingResultStatus.Unavailable, UnavailableMessage);
        }

        var created = await CreateCustomerAsync(account, token, callerToken);
        if (created.Ok)
        {
            _logger.LogInformation("Created Maxio customer {MaxioCustomerId} for user {UserId}.", created.Value!.Id, account.UserId);
            await CompleteCustomerLinkAsync(claim, created.Value!.Id!.Value);
            return BillingResult<int>.Success(created.Value!.Id!.Value);
        }

        if (created.Failure is CallFailure.Rejected or CallFailure.Unknown)
        {
            // A rejection may be "reference already taken" (a concurrent or earlier create won), and an unknown
            // outcome may have landed: either way, re-read by reference on a fresh budget before answering.
            using var settle = new CancellationTokenSource(MaxioBillingDefaults.SettleBudget);
            var settled = await ReadCustomerByReferenceAsync(account.UserId, settle.Token, CancellationToken.None);
            if (settled.Ok)
            {
                _logger.LogWarning("Settled customer create for user {UserId}: Maxio customer {MaxioCustomerId} exists.", account.UserId, settled.Value!.Id);
                await CompleteCustomerLinkAsync(claim, settled.Value!.Id!.Value);
                return BillingResult<int>.Success(settled.Value!.Id!.Value);
            }

            if (created.Failure == CallFailure.Rejected)
            {
                await ReleaseCustomerLinkAsync(claim);
                return BillingResult<int>.Failure(BillingResultStatus.Rejected, "The billing service declined to create your billing account.", created.Errors);
            }

            // Still unknown: keep the claim pending; the next call settles it (Maxio refuses a second
            // customer with the same reference, so a later create cannot duplicate it).
            _logger.LogWarning("Customer create for user {UserId} still unsettled ({Failure}); left pending.", account.UserId, settled.Failure);
            return BillingResult<int>.Failure(BillingResultStatus.Unavailable, "Your billing account could not be confirmed yet. Please retry shortly.");
        }

        await ReleaseCustomerLinkAsync(claim);
        return BillingResult<int>.Failure(BillingResultStatus.Unavailable, UnavailableMessage);
    }

    // ------------------------------------------------------------------ claim bookkeeping
    // Recording a provider outcome must not be abandoned because the caller went away, so these use no token.

    private async Task CompleteCustomerLinkAsync(BillingCustomerLink link, int customerId)
    {
        if (!await _claims.TryUpdateAsync(link, l => l.Complete(customerId, _clock.GetUtcNow()), CancellationToken.None))
        {
            _logger.LogInformation("Customer link for user {UserId} was settled by another request.", link.UserId);
        }
    }

    private async Task ReleaseCustomerLinkAsync(BillingCustomerLink link)
    {
        if (!await _claims.TryReleaseAsync(link, CancellationToken.None))
        {
            _logger.LogInformation("Customer claim for user {UserId} was already changed by another request.", link.UserId);
        }
    }

    private async Task CompleteEnrollmentAsync(SubscriptionEnrollment enrollment, Subscription subscription)
    {
        var planHandle = subscription.Product?.Handle ?? enrollment.PlanHandle;
        if (!await _claims.TryUpdateAsync(enrollment, e => e.Complete(subscription.Id!.Value, planHandle, _clock.GetUtcNow()), CancellationToken.None))
        {
            _logger.LogInformation("Enrollment for user {UserId} was settled by another request.", enrollment.UserId);
        }
    }

    private async Task ReleaseEnrollmentAsync(SubscriptionEnrollment enrollment)
    {
        if (!await _claims.TryReleaseAsync(enrollment, CancellationToken.None))
        {
            _logger.LogInformation("Enrollment claim for user {UserId} was already changed by another request.", enrollment.UserId);
        }
    }

    // ------------------------------------------------------------------ Maxio calls (one catch ladder each)

    private async Task<ProviderCall<IReadOnlyList<ProductResponse>>> ListProductsPageAsync(int page, CancellationToken token, CancellationToken callerToken)
    {
        const string operation = "ListProductsForProductFamily";
        try
        {
            var products = await _client.ProductFamilies.ListProductsForProductFamily(
                new ListProductsForProductFamilyRequest
                {
                    ProductFamilyId = "handle:" + FamilyHandle,
                    Page = page,
                    PerPage = MaxioBillingDefaults.ProductsPerPage
                },
                cancellationToken: token);
            return ProviderCall<IReadOnlyList<ProductResponse>>.Success(products ?? Array.Empty<ProductResponse>());
        }
        catch (ApiException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out _))
            {
                // Our configuration names a family the site does not have: an operator problem, not the caller's.
                _logger.LogError("Maxio product family {ProductFamilyHandle} was not found (HTTP {StatusCode}). Check Maxio:ProductFamilyHandle.",
                    FamilyHandle, (int)ex.StatusCode);
                return ProviderCall<IReadOnlyList<ProductResponse>>.Failed(CallFailure.Unavailable);
            }
            return ProviderCall<IReadOnlyList<ProductResponse>>.Failed(Classify(ex, operation, isWrite: false));
        }
        catch (Exception ex) when (IsProviderFailure(ex, callerToken))
        {
            return ProviderCall<IReadOnlyList<ProductResponse>>.Failed(Classify(ex, operation, isWrite: false));
        }
    }

    private async Task<ProviderCall<Customer>> ReadCustomerByReferenceAsync(string reference, CancellationToken token, CancellationToken callerToken)
    {
        const string operation = "ReadCustomerByReference";
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = reference },
                cancellationToken: token);
            if (response?.Customer?.Id is null)
            {
                _logger.LogError("{Operation} returned a customer without an id.", operation);
                return ProviderCall<Customer>.Failed(CallFailure.Unavailable);
            }
            return ProviderCall<Customer>.Success(response.Customer);
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return ProviderCall<Customer>.Failed(CallFailure.NotFound);
        }
        catch (Exception ex) when (IsProviderFailure(ex, callerToken))
        {
            return ProviderCall<Customer>.Failed(Classify(ex, operation, isWrite: false));
        }
    }

    private async Task<ProviderCall<Customer>> CreateCustomerAsync(BillingAccount account, CancellationToken token, CancellationToken callerToken)
    {
        const string operation = "CreateCustomer";
        try
        {
            var response = await _client.Customers.CreateCustomer(
                new CreateCustomerOperationRequest
                {
                    Body = new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = Truncate(FirstNameFor(account), 255),
                            LastName = Truncate(string.IsNullOrWhiteSpace(account.LastName) ? "Customer" : account.LastName.Trim(), 255),
                            Email = account.Email,
                            // Unique per Maxio site: the key that makes the create reconcilable and non-duplicable.
                            Reference = account.UserId
                        }
                    }
                },
                cancellationToken: token);
            if (response?.Customer?.Id is null)
            {
                _logger.LogError("{Operation} succeeded but returned no customer id; treating the outcome as unknown.", operation);
                return ProviderCall<Customer>.Failed(CallFailure.Unknown);
            }
            return ProviderCall<Customer>.Success(response.Customer);
        }
        catch (ApiException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var body))
            {
                var errors = CustomerErrors(body);
                _logger.LogWarning("{Operation} rejected (HTTP {StatusCode}): {Errors}", operation, (int)ex.StatusCode, string.Join("; ", errors));
                return ProviderCall<Customer>.Failed(CallFailure.Rejected, errors);
            }
            return ProviderCall<Customer>.Failed(Classify(ex, operation, isWrite: true));
        }
        catch (Exception ex) when (IsProviderFailure(ex, callerToken))
        {
            return ProviderCall<Customer>.Failed(Classify(ex, operation, isWrite: true));
        }
    }

    private async Task<ProviderCall<Subscription>> CreateSubscriptionAsync(string productHandle, int customerId, string reference, CancellationToken token, CancellationToken callerToken)
    {
        const string operation = "CreateSubscription";
        try
        {
            var response = await _client.Subscriptions.CreateSubscription(
                new CreateSubscriptionOperationRequest
                {
                    Body = new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = productHandle,
                            CustomerId = customerId,
                            Reference = reference,
                            // This flow captures no card, so the subscription is invoiced (remittance) rather than
                            // charged automatically; automatic collection would be refused for want of a payment method.
                            PaymentCollectionMethod = CollectionMethod.Remittance
                        }
                    }
                },
                cancellationToken: token);
            if (response?.Subscription?.Id is null)
            {
                _logger.LogError("{Operation} succeeded but returned no subscription; treating the outcome as unknown.", operation);
                return ProviderCall<Subscription>.Failed(CallFailure.Unknown);
            }
            return ProviderCall<Subscription>.Success(response.Subscription);
        }
        catch (ApiException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var body))
            {
                var errors = body.Errors ?? Array.Empty<string>();
                _logger.LogWarning("{Operation} rejected (HTTP {StatusCode}): {Errors}", operation, (int)ex.StatusCode, string.Join("; ", errors));
                return ProviderCall<Subscription>.Failed(CallFailure.Rejected, errors);
            }
            return ProviderCall<Subscription>.Failed(Classify(ex, operation, isWrite: true));
        }
        catch (Exception ex) when (IsProviderFailure(ex, callerToken))
        {
            return ProviderCall<Subscription>.Failed(Classify(ex, operation, isWrite: true));
        }
    }

    private async Task<ProviderCall<Subscription>> FindSubscriptionByReferenceAsync(string reference, CancellationToken token, CancellationToken callerToken)
    {
        const string operation = "FindSubscription";
        try
        {
            var response = await _client.Subscriptions.FindSubscription(
                new FindSubscriptionRequest { Reference = reference },
                cancellationToken: token);
            if (response?.Subscription?.Id is null)
            {
                _logger.LogError("{Operation} returned no subscription.", operation);
                return ProviderCall<Subscription>.Failed(CallFailure.Unavailable);
            }
            return ProviderCall<Subscription>.Success(response.Subscription);
        }
        catch (ApiException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return ProviderCall<Subscription>.Failed(CallFailure.NotFound);
            }
            return ProviderCall<Subscription>.Failed(Classify(ex, operation, isWrite: false));
        }
        catch (Exception ex) when (IsProviderFailure(ex, callerToken))
        {
            return ProviderCall<Subscription>.Failed(Classify(ex, operation, isWrite: false));
        }
    }

    private async Task<ProviderCall<IReadOnlyList<Subscription>>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken token, CancellationToken callerToken)
    {
        const string operation = "ListCustomerSubscriptions";
        try
        {
            var responses = await _client.Customers.ListCustomerSubscriptions(
                new ListCustomerSubscriptionsRequest { CustomerId = customerId },
                cancellationToken: token);
            IReadOnlyList<Subscription> subscriptions = (responses ?? Array.Empty<SubscriptionResponse>())
                .Select(r => r?.Subscription)
                .Where(s => s is not null)
                .Select(s => s!)
                .ToList();
            return ProviderCall<IReadOnlyList<Subscription>>.Success(subscriptions);
        }
        catch (Exception ex) when (IsProviderFailure(ex, callerToken))
        {
            return ProviderCall<IReadOnlyList<Subscription>>.Failed(Classify(ex, operation, isWrite: false));
        }
    }

    // ------------------------------------------------------------------ failure classification

    /// <summary>SDK failures and our own budget expiring are provider failures; the caller's own
    /// cancellation is not, and propagates untouched.</summary>
    private static bool IsProviderFailure(Exception ex, CancellationToken callerToken) =>
        ex is SdkException || (ex is OperationCanceledException && !callerToken.IsCancellationRequested);

    /// <summary>
    /// The shared tail of every catch ladder: RawError, unreadable bodies, transport failures and timeouts.
    /// For a write, anything that leaves open whether Maxio acted is <see cref="CallFailure.Unknown"/>.
    /// Typed error bodies are read in each operation's own catch, never here.
    /// </summary>
    private CallFailure Classify(Exception ex, string operation, bool isWrite)
    {
        switch (ex)
        {
            case ResponseDeserializationException rde when (int)rde.StatusCode is >= 200 and < 300:
                _logger.LogError("{Operation}: Maxio returned an unreadable success body (HTTP {StatusCode}, target {TargetType}).",
                    operation, (int)rde.StatusCode, rde.TargetType?.Name);
                return isWrite ? CallFailure.Unknown : CallFailure.Unavailable;

            case ApiException api:
                var status = (int)api.StatusCode;
                if (status is 401 or 403)
                {
                    _logger.LogError("{Operation}: Maxio rejected our credentials (HTTP {StatusCode}). Check Maxio:ApiKey and Maxio:Subdomain.", operation, status);
                    return CallFailure.Unavailable;
                }
                if (status == 429)
                {
                    _logger.LogWarning("{Operation}: Maxio rate limit reached (HTTP 429).", operation);
                    return CallFailure.Unavailable;
                }
                if (status == 404)
                {
                    return CallFailure.NotFound;
                }
                if (status >= 500)
                {
                    _logger.LogError("{Operation}: Maxio server error (HTTP {StatusCode}).", operation, status);
                    return isWrite ? CallFailure.Unknown : CallFailure.Unavailable;
                }
                _logger.LogError("{Operation}: Maxio refused the request (HTTP {StatusCode}, {ExceptionType}).", operation, status, ex.GetType().Name);
                return isWrite ? CallFailure.Rejected : CallFailure.Unavailable;

            case SdkTimeoutException timeout:
                _logger.LogError("{Operation}: no answer from Maxio within {Timeout}.", operation, timeout.Timeout);
                return isWrite ? CallFailure.Unknown : CallFailure.Unavailable;

            case SdkConnectionException:
                _logger.LogError(ex, "{Operation}: could not reach Maxio.", operation);
                return isWrite ? CallFailure.Unknown : CallFailure.Unavailable;

            case AuthSchemeException:
                _logger.LogError(ex, "{Operation}: Maxio credentials could not be applied.", operation);
                return CallFailure.Unavailable;

            case OperationCanceledException:
                _logger.LogError("{Operation}: the request's time budget expired before Maxio answered.", operation);
                return isWrite ? CallFailure.Unknown : CallFailure.Unavailable;

            default:
                _logger.LogError(ex, "{Operation}: unexpected Maxio SDK failure.", operation);
                return isWrite ? CallFailure.Unknown : CallFailure.Unavailable;
        }
    }

    // ------------------------------------------------------------------ helpers

    private static CancellationTokenSource Budget(CancellationToken callerToken, TimeSpan budget)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        cts.CancelAfter(budget);
        return cts;
    }

    private static BillingResult<T> InProgress<T>() =>
        BillingResult<T>.Failure(BillingResultStatus.InProgress, "A subscription request for your account is already in progress. Please retry in a moment.");

    private static string NewSubscriptionReference() => $"eshop-sub-{Guid.NewGuid():N}";

    private static string FirstNameFor(BillingAccount account)
    {
        if (!string.IsNullOrWhiteSpace(account.FirstName))
        {
            return account.FirstName.Trim();
        }
        var at = account.Email.IndexOf('@');
        return at > 0 ? account.Email[..at] : account.Email;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private static IReadOnlyList<string> CustomerErrors(CustomerErrorResponse1 body)
    {
        var errors = body.Errors;
        if (errors is not null)
        {
            if (errors.TryGetListOfString(out var list) && list is not null)
            {
                return list;
            }
            if (errors.TryGetCustomerError(out var customerError) && !string.IsNullOrWhiteSpace(customerError?.Customer))
            {
                return new[] { customerError.Customer! };
            }
        }
        return new[] { "The customer could not be created." };
    }

    private enum CallFailure
    {
        None,
        NotFound,
        /// <summary>Maxio definitively refused the request (validation).</summary>
        Rejected,
        /// <summary>No usable answer, and nothing was (or could have been) written.</summary>
        Unavailable,
        /// <summary>A write may or may not have been applied at Maxio.</summary>
        Unknown
    }

    private readonly record struct ProviderCall<T>(T? Value, CallFailure Failure, IReadOnlyList<string> Errors)
    {
        public bool Ok => Failure == CallFailure.None;
        public static ProviderCall<T> Success(T value) => new(value, CallFailure.None, Array.Empty<string>());
        public static ProviderCall<T> Failed(CallFailure failure, IReadOnlyList<string>? errors = null) => new(default, failure, errors ?? Array.Empty<string>());
    }
}
