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
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MaxioSubscription = MaxioAdvancedBilling.Models.Subscription;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Subscription billing with Maxio Advanced Billing as the system of record.
/// Every write follows claim → provider call → record: a local primary-key claim is saved before the
/// provider is called, so a double-submit is refused locally and never reaches Maxio twice.
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    /// <summary>The whole budget one inbound request may spend on Maxio, across every call it makes.</summary>
    internal static readonly TimeSpan RequestBudget = TimeSpan.FromSeconds(25);

    /// <summary>Budget for re-reading Maxio after a write whose outcome is unknown (independent of the expired request budget).</summary>
    internal static readonly TimeSpan ReconcileBudget = TimeSpan.FromSeconds(10);

    /// <summary>A pending claim older than this is treated as abandoned and may be taken over.</summary>
    internal static readonly TimeSpan StaleClaimAge = TimeSpan.FromMinutes(2);

    internal const int PlanPageSize = 200;
    internal const int MaxPlanPages = 5;
    private static readonly TimeSpan PlanCacheDuration = TimeSpan.FromSeconds(60);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly CatalogContext _db;
    private readonly MaxioSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly TimeProvider _time;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        CatalogContext db,
        IOptions<MaxioSettings> settings,
        IMemoryCache cache,
        ILogger<MaxioSubscriptionBillingService> logger,
        TimeProvider? time = null)
    {
        _client = client;
        _db = db;
        _settings = settings.Value;
        _cache = cache;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    private string ProductFamilyHandle => _settings.ProductFamilyHandle!.Trim();
    private string PlanCacheKey => $"maxio-plans:{ProductFamilyHandle}";

    // ---------------------------------------------------------------- plans

    public async Task<SubscriptionPlanCatalog> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        using var budget = StartBudget(cancellationToken);
        return await GetPlansCachedAsync(budget.Token, cancellationToken);
    }

    private async Task<SubscriptionPlanCatalog> GetPlansCachedAsync(CancellationToken budgetToken, CancellationToken callerToken)
    {
        if (_cache.TryGetValue(PlanCacheKey, out SubscriptionPlanCatalog? cached) && cached is not null)
        {
            return cached;
        }

        var catalog = await LoadPlansAsync(budgetToken, callerToken);
        _cache.Set(PlanCacheKey, catalog, PlanCacheDuration);
        return catalog;
    }

    private async Task<SubscriptionPlanCatalog> LoadPlansAsync(CancellationToken budgetToken, CancellationToken callerToken)
    {
        var plans = new List<SubscriptionPlan>();
        var truncated = false;

        for (var page = 1; ; page++)
        {
            IReadOnlyList<ProductResponse> items;
            try
            {
                items = await _client.ProductFamilies.ListProductsForProductFamily(
                    new ListProductsForProductFamilyRequest
                    {
                        ProductFamilyId = "handle:" + ProductFamilyHandle,
                        Page = page,
                        PerPage = PlanPageSize
                    },
                    cancellationToken: budgetToken);
            }
            catch (ApiException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out var message))
                {
                    // 404: the configured product family does not exist on this site — a deployment fault.
                    _logger.LogError("Maxio product family {ProductFamilyHandle} could not be read ({StatusCode}): {ProviderMessage}",
                        ProductFamilyHandle, (int)ex.StatusCode, message);
                    throw new SubscriptionBillingException(StatusCodes.ServiceUnavailable, "Subscription plans are not available right now.", ex);
                }

                throw TranslateFailure(ex, "ListProductsForProductFamily");
            }
            catch (SdkException ex)
            {
                throw TranslateFailure(ex, "ListProductsForProductFamily");
            }
            catch (OperationCanceledException ex) when (!callerToken.IsCancellationRequested)
            {
                throw BudgetExpired(ex, "ListProductsForProductFamily");
            }

            foreach (var item in items)
            {
                var plan = ToPlan(item.Product);
                if (plan is not null)
                {
                    plans.Add(plan);
                }
            }

            if (items.Count < PlanPageSize)
            {
                break;
            }

            if (page >= MaxPlanPages)
            {
                // The provider may have more: say so in the result, not only in the log.
                truncated = true;
                _logger.LogWarning("Maxio plan catalog for {ProductFamilyHandle} was cut at {MaxPages} pages of {PageSize}",
                    ProductFamilyHandle, MaxPlanPages, PlanPageSize);
                break;
            }
        }

        return new SubscriptionPlanCatalog(plans, truncated);
    }

    private static SubscriptionPlan? ToPlan(Product? product)
    {
        // Offer only live products that can be bought without card capture (no payment flow in scope).
        if (product is null ||
            string.IsNullOrWhiteSpace(product.Handle) ||
            product.ArchivedAt is not null ||
            product.RequireCreditCard == true)
        {
            return null;
        }

        return new SubscriptionPlan(
            product.Handle,
            product.Name ?? product.Handle,
            product.Description,
            (product.PriceInCents ?? 0) / 100m,
            product.Interval,
            product.IntervalUnit?.Value);
    }

    // ---------------------------------------------------------------- subscribe

    public async Task<SubscribeResult> SubscribeAsync(BillingUser user, string? planHandle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        var handle = string.IsNullOrWhiteSpace(planHandle) ? _settings.DefaultPlanHandle?.Trim() : planHandle.Trim();
        if (string.IsNullOrEmpty(handle))
        {
            throw new SubscriptionBillingException(StatusCodes.BadRequest, "A planHandle is required.");
        }

        using var budget = StartBudget(cancellationToken);

        // The handle must be one this application offers — the same set GET /api/subscription-plans returns.
        var catalog = await GetPlansCachedAsync(budget.Token, cancellationToken);
        var plan = catalog.Plans.FirstOrDefault(p => string.Equals(p.Handle, handle, StringComparison.Ordinal));
        if (plan is null)
        {
            if (catalog.IsTruncated)
            {
                throw new SubscriptionBillingException(StatusCodes.ServiceUnavailable,
                    "The plan catalog could not be read in full; please try again later.");
            }

            throw new SubscriptionBillingException(StatusCodes.BadRequest, $"'{handle}' is not an available subscription plan.");
        }

        (SubscriptionClaim? Claim, SubscribeResult? ExistingResult) acquired;
        try
        {
            acquired = await AcquireSubscriptionClaimAsync(user, plan, budget.Token, cancellationToken);
        }
        catch (ClaimRetainedException ex)
        {
            return ex.Pending;
        }

        if (acquired.ExistingResult is not null)
        {
            return acquired.ExistingResult;
        }

        var claim = acquired.Claim!;
        try
        {
            var customerId = await EnsureCustomerAsync(user, budget.Token, cancellationToken);

            // Adopt-check: a live subscription to this plan may already exist at Maxio (an earlier create
            // whose outcome was unknown, or a local store that was reset). Adopt it instead of duplicating it.
            var existing = (await ListSubscriptionsAsync(customerId, budget.Token, cancellationToken))
                .FirstOrDefault(s => (s.Reference == claim.Reference ||
                                      string.Equals(s.Product?.Handle, plan.Handle, StringComparison.Ordinal)) &&
                                     IsLive(s));
            if (existing?.Id is int existingId)
            {
                claim.Complete(existingId);
                await _db.SaveChangesAsync(CancellationToken.None);
                _logger.LogInformation("User {UserId} already subscribed to {PlanHandle}: adopted Maxio subscription {SubscriptionId}",
                    user.UserId, plan.Handle, existingId);
                return new SubscribeResult(SubscribeOutcome.AlreadySubscribed, plan.Handle, existing.Reference ?? claim.Reference, ToDetails(existing, plan));
            }

            return await CreateSubscriptionAsync(user, claim, customerId, plan, budget.Token);
        }
        catch (Exception ex) when (ex is not ClaimRetainedException)
        {
            // A definite failure: nothing was created at Maxio, so free the claim for the next attempt.
            await ReleaseAsync(claim);
            throw;
        }
        catch (ClaimRetainedException ex)
        {
            // Sent, but not confirmable yet: the claim stays, and the caller is told it is pending.
            return ex.Pending;
        }
    }

    private async Task<(SubscriptionClaim? Claim, SubscribeResult? ExistingResult)> AcquireSubscriptionClaimAsync(
        BillingUser user, SubscriptionPlan plan, CancellationToken budgetToken, CancellationToken callerToken)
    {
        var key = SubscriptionClaim.KeyFor(user.UserId, plan.Handle);

        // Two passes: a completed claim whose subscription has since ended is released and claimed again.
        for (var pass = 0; pass < 2; pass++)
        {
            var existing = await _db.SubscriptionClaims.FirstOrDefaultAsync(c => c.Id == key, budgetToken);
            if (existing is null)
            {
                var claim = new SubscriptionClaim(user.UserId, plan.Handle, _time.GetUtcNow());
                _db.SubscriptionClaims.Add(claim);
                if (await TrySaveClaimAsync(claim))
                {
                    _logger.LogInformation("User {UserId} claimed subscribe to {PlanHandle} with reference {Reference}",
                        user.UserId, plan.Handle, claim.Reference);
                    return (claim, null);
                }

                // Another request inserted the same key first; re-read what it holds.
                continue;
            }

            if (existing.Status == ClaimStatus.Completed)
            {
                var customerId = await EnsureCustomerAsync(user, budgetToken, callerToken);
                var subscription = (await ListSubscriptionsAsync(customerId, budgetToken, callerToken))
                    .FirstOrDefault(s => s.Id == existing.ProviderSubscriptionId);
                if (subscription is not null && IsLive(subscription))
                {
                    return (null, new SubscribeResult(SubscribeOutcome.AlreadySubscribed, plan.Handle, existing.Reference, ToDetails(subscription, plan)));
                }

                // The recorded subscription ended (or is gone): the user may subscribe again.
                _db.SubscriptionClaims.Remove(existing);
                if (!await TrySaveClaimAsync(existing))
                {
                    throw InProgress();
                }

                continue;
            }

            // Pending: a request is in flight, unless the claim was abandoned or its outcome is unknown.
            if (!existing.OutcomeUnknown && !existing.IsStale(_time.GetUtcNow(), StaleClaimAge))
            {
                throw InProgress();
            }

            existing.Retake(_time.GetUtcNow());
            if (!await TrySaveClaimAsync(existing))
            {
                throw InProgress();
            }

            var settled = await TrySettleUnknownSubscriptionAsync(existing, plan);
            if (settled is not null)
            {
                return (null, settled);
            }

            // The earlier reference is known not to exist at Maxio: start a fresh attempt under this claim.
            existing.RenewReference();
            await _db.SaveChangesAsync(CancellationToken.None);
            _logger.LogInformation("User {UserId} took over subscribe claim for {PlanHandle} with reference {Reference}",
                user.UserId, plan.Handle, existing.Reference);
            return (existing, null);
        }

        throw InProgress();
    }

    private async Task<SubscribeResult> CreateSubscriptionAsync(
        BillingUser user, SubscriptionClaim claim, int customerId, SubscriptionPlan plan, CancellationToken budgetToken)
    {
        SubscriptionResponse? response = null;
        Exception? ambiguousFailure = null;
        try
        {
            response = await _client.Subscriptions.CreateSubscription(
                new CreateSubscriptionOperationRequest
                {
                    Body = new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = plan.Handle,
                            CustomerId = customerId,
                            Reference = claim.Reference,
                            // No payment method is captured in this flow, so the subscription is invoiced
                            // (remittance) rather than auto-charged against a card on file.
                            PaymentCollectionMethod = MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance
                        }
                    }
                },
                cancellationToken: budgetToken);
        }
        catch (ApiException<CreateSubscriptionError> ex) when ((int)ex.StatusCode < 500)
        {
            if (ex.Error.TryGetErrorListResponse1(out var body))
            {
                var reasons = string.Join("; ", body.Errors ?? Array.Empty<string>());
                _logger.LogWarning("Maxio declined subscription for user {UserId} to {PlanHandle} ({StatusCode}): {ProviderErrors}",
                    user.UserId, plan.Handle, (int)ex.StatusCode, reasons);
                throw new SubscriptionBillingException(StatusCodes.UnprocessableEntity,
                    string.IsNullOrWhiteSpace(reasons) ? "The billing provider declined the subscription." : $"The billing provider declined the subscription: {reasons}", ex);
            }

            if (ex.Error.TryGetRawError(out RawError _))
            {
                throw TranslateFailure(ex, "CreateSubscription");
            }

            throw TranslateFailure(ex, "CreateSubscription");
        }
        catch (ApiException<CreateSubscriptionError> ex)
        {
            ambiguousFailure = ex; // 5xx: Maxio may have acted before failing
        }
        catch (ResponseDeserializationException ex) when (IsSuccessOrServerError(ex.StatusCode))
        {
            ambiguousFailure = ex; // the create may have succeeded; we could not read the answer
        }
        catch (ResponseDeserializationException ex)
        {
            throw TranslateFailure(ex, "CreateSubscription"); // a 4xx rejection whose detail was unreadable
        }
        catch (AuthSchemeException ex)
        {
            throw TranslateFailure(ex, "CreateSubscription"); // never sent
        }
        catch (SdkConnectionException ex)
        {
            ambiguousFailure = ex; // includes SdkTimeoutException: the request may have landed
        }
        catch (OperationCanceledException ex)
        {
            ambiguousFailure = ex; // budget or client disconnect after the request may have been sent
        }

        if (ambiguousFailure is null && response?.Subscription is { Id: int id } created)
        {
            claim.Complete(id);
            await _db.SaveChangesAsync(CancellationToken.None);
            _logger.LogInformation("Created Maxio subscription {SubscriptionId} ({State}) for user {UserId} on {PlanHandle}",
                id, created.State?.Value, user.UserId, plan.Handle);
            return new SubscribeResult(SubscribeOutcome.Created, plan.Handle, claim.Reference, ToDetails(created, plan));
        }

        _logger.LogWarning(ambiguousFailure, "CreateSubscription outcome unknown for user {UserId}, reference {Reference}; re-reading Maxio",
            user.UserId, claim.Reference);

        var settled = await TrySettleUnknownSubscriptionAsync(claim, plan);
        if (settled is not null)
        {
            return settled;
        }

        // Re-read proved the create did not land: the outer catch releases the claim.
        throw new SubscriptionBillingException(StatusCodes.ServiceUnavailable,
            "The billing provider did not complete the subscription. Please try again.", ambiguousFailure);
    }

    /// <summary>
    /// Settles a create whose outcome is unknown by finding the subscription by the claim's reference.
    /// Returns the result when found; null when Maxio reports no such reference (the create did not land).
    /// When Maxio cannot be read either, the claim is kept as outcome-unknown and a pending result is returned,
    /// so the next request for this user settles it.
    /// </summary>
    private async Task<SubscribeResult?> TrySettleUnknownSubscriptionAsync(SubscriptionClaim claim, SubscriptionPlan plan)
    {
        using var reconcile = new CancellationTokenSource(ReconcileBudget);
        try
        {
            var found = await _client.Subscriptions.FindSubscription(
                new FindSubscriptionRequest { Reference = claim.Reference },
                cancellationToken: reconcile.Token);

            if (found.Subscription is { Id: int id } subscription &&
                string.Equals(subscription.Reference, claim.Reference, StringComparison.Ordinal))
            {
                claim.Complete(id);
                await _db.SaveChangesAsync(CancellationToken.None);
                _logger.LogInformation("Reconciled reference {Reference}: Maxio subscription {SubscriptionId} exists", claim.Reference, id);
                return new SubscribeResult(SubscribeOutcome.Created, plan.Handle, claim.Reference, ToDetails(subscription, plan));
            }

            _logger.LogWarning("Reconciled reference {Reference}: Maxio returned no matching subscription", claim.Reference);
            return null;
        }
        catch (ApiException<FindSubscriptionError> ex) when (ex.Error.TryGetNoContent(out RawError _))
        {
            _logger.LogInformation("Reconciled reference {Reference}: not found at Maxio", claim.Reference);
            return null;
        }
        catch (Exception ex) when (ex is SdkException or OperationCanceledException)
        {
            // Still unknown. Keep the claim so no second create can race it; a later request settles it.
            claim.MarkOutcomeUnknown();
            await _db.SaveChangesAsync(CancellationToken.None);
            _logger.LogError(ex, "Subscription reference {Reference} for user {UserId} could not be confirmed; left pending",
                claim.Reference, claim.UserId);
            throw new ClaimRetainedException(new SubscriptionBillingException(StatusCodes.Accepted,
                "The subscription request was sent but could not be confirmed yet.", ex), PendingResult(claim, plan));
        }
    }

    private static SubscribeResult PendingResult(SubscriptionClaim claim, SubscriptionPlan plan) =>
        new(SubscribeOutcome.PendingConfirmation, plan.Handle, claim.Reference, null);

    // ---------------------------------------------------------------- customer

    /// <summary>Returns the Maxio customer id for <paramref name="user"/>, creating the customer at most once.</summary>
    private async Task<int> EnsureCustomerAsync(BillingUser user, CancellationToken budgetToken, CancellationToken callerToken)
    {
        var link = await _db.BillingCustomerLinks.FirstOrDefaultAsync(l => l.UserId == user.UserId, budgetToken);
        if (link is { Status: ClaimStatus.Completed, ProviderCustomerId: int knownId })
        {
            return knownId;
        }

        if (link is null)
        {
            link = new BillingCustomerLink(user.UserId, _time.GetUtcNow());
            _db.BillingCustomerLinks.Add(link);
            if (!await TrySaveClaimAsync(link))
            {
                var winner = await _db.BillingCustomerLinks.AsNoTracking().FirstOrDefaultAsync(l => l.UserId == user.UserId, budgetToken);
                if (winner is { Status: ClaimStatus.Completed, ProviderCustomerId: int winnerId })
                {
                    return winnerId;
                }

                throw InProgress();
            }
        }
        else
        {
            if (!link.IsStale(_time.GetUtcNow(), StaleClaimAge))
            {
                throw InProgress();
            }

            link.Retake(_time.GetUtcNow());
            if (!await TrySaveClaimAsync(link))
            {
                throw InProgress();
            }
        }

        // The claim is held. The reference lookup comes first, so a customer created by an earlier attempt
        // (or before a restart) is reused rather than duplicated.
        try
        {
            var customerId = await FindCustomerIdByReferenceAsync(user.CustomerReference, budgetToken, callerToken)
                             ?? await CreateCustomerAsync(user, budgetToken, callerToken);

            link.Complete(customerId);
            await _db.SaveChangesAsync(CancellationToken.None);
            return customerId;
        }
        catch (CustomerOutcomeUnknownException ex)
        {
            // Keep the claim pending; once stale, the next attempt re-reads by reference and settles it.
            throw ex.Failure;
        }
        catch
        {
            await ReleaseAsync(link);
            throw;
        }
    }

    /// <summary>Looks up the customer by reference; null only when Maxio answers 404.</summary>
    private async Task<int?> FindCustomerIdByReferenceAsync(string reference, CancellationToken budgetToken, CancellationToken callerToken)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = reference },
                cancellationToken: budgetToken);
            return response.Customer?.Id ?? throw new SubscriptionBillingException(StatusCodes.BadGateway,
                "The billing provider returned an unexpected customer record.");
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException ex)
        {
            throw TranslateFailure(ex, "ReadCustomerByReference");
        }
        catch (OperationCanceledException ex) when (!callerToken.IsCancellationRequested)
        {
            throw BudgetExpired(ex, "ReadCustomerByReference");
        }
    }

    private async Task<int> CreateCustomerAsync(BillingUser user, CancellationToken budgetToken, CancellationToken callerToken)
    {
        var email = user.Email.Trim();
        var at = email.IndexOf('@');
        var firstName = at > 0 ? email[..at] : email;

        Exception? ambiguousFailure;
        try
        {
            var response = await _client.Customers.CreateCustomer(
                new CreateCustomerOperationRequest
                {
                    Body = new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = firstName,
                            LastName = "eShop",
                            Email = email,
                            // Maxio allows one customer per reference: the provider-side duplicate guard.
                            Reference = user.CustomerReference
                        }
                    }
                },
                cancellationToken: budgetToken);

            if (response.Customer?.Id is int id)
            {
                _logger.LogInformation("Created Maxio customer {CustomerId} for user {UserId}", id, user.UserId);
                return id;
            }

            ambiguousFailure = null;
        }
        catch (ApiException<CreateCustomerError> ex) when ((int)ex.StatusCode < 500)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var body))
            {
                // A concurrent create elsewhere may have won the reference: prefer the existing customer.
                var adopted = await FindCustomerIdByReferenceAsync(user.CustomerReference, budgetToken, callerToken);
                if (adopted is int adoptedId)
                {
                    return adoptedId;
                }

                var reasons = DescribeCustomerErrors(body);
                _logger.LogWarning("Maxio declined customer for user {UserId} ({StatusCode}): {ProviderErrors}",
                    user.UserId, (int)ex.StatusCode, reasons);
                throw new SubscriptionBillingException(StatusCodes.UnprocessableEntity,
                    $"The billing provider declined the customer record: {reasons}", ex);
            }

            if (ex.Error.TryGetRawError(out RawError _))
            {
                throw TranslateFailure(ex, "CreateCustomer");
            }

            throw TranslateFailure(ex, "CreateCustomer");
        }
        catch (ApiException<CreateCustomerError> ex)
        {
            ambiguousFailure = ex;
        }
        catch (ResponseDeserializationException ex) when (IsSuccessOrServerError(ex.StatusCode))
        {
            ambiguousFailure = ex;
        }
        catch (ResponseDeserializationException ex)
        {
            throw TranslateFailure(ex, "CreateCustomer");
        }
        catch (AuthSchemeException ex)
        {
            throw TranslateFailure(ex, "CreateCustomer");
        }
        catch (SdkConnectionException ex)
        {
            ambiguousFailure = ex;
        }
        catch (OperationCanceledException ex)
        {
            ambiguousFailure = ex;
        }

        // Unknown outcome: settle by re-reading the customer by the reference we sent.
        _logger.LogWarning(ambiguousFailure, "CreateCustomer outcome unknown for user {UserId}; re-reading Maxio by reference", user.UserId);
        using var reconcile = new CancellationTokenSource(ReconcileBudget);
        int? found;
        try
        {
            found = await FindCustomerIdByReferenceAsync(user.CustomerReference, reconcile.Token, CancellationToken.None);
        }
        catch (SubscriptionBillingException ex)
        {
            throw new CustomerOutcomeUnknownException(new SubscriptionBillingException(StatusCodes.ServiceUnavailable,
                "The billing provider is not responding. Please try again shortly.", ex));
        }

        if (found is int foundId)
        {
            _logger.LogInformation("Reconciled Maxio customer {CustomerId} for user {UserId}", foundId, user.UserId);
            return foundId;
        }

        throw new SubscriptionBillingException(StatusCodes.ServiceUnavailable,
            "The billing provider did not create the customer record. Please try again.", ambiguousFailure);
    }

    private static string DescribeCustomerErrors(CustomerErrorResponse1 body)
    {
        if (body.Errors is null)
        {
            return "no details were given";
        }

        if (body.Errors.TryGetCustomerError(out var customerError) && !string.IsNullOrWhiteSpace(customerError.Customer))
        {
            return customerError.Customer;
        }

        if (body.Errors.TryGetListOfString(out var messages) && messages.Count > 0)
        {
            return string.Join("; ", messages);
        }

        return "no details were given";
    }

    // ---------------------------------------------------------------- my subscriptions

    public async Task<IReadOnlyList<SubscriptionDetails>> GetSubscriptionsAsync(BillingUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        using var budget = StartBudget(cancellationToken);

        var link = await _db.BillingCustomerLinks.AsNoTracking().FirstOrDefaultAsync(l => l.UserId == user.UserId, budget.Token);
        var customerId = link is { Status: ClaimStatus.Completed, ProviderCustomerId: int id }
            ? id
            : await FindCustomerIdByReferenceAsync(user.CustomerReference, budget.Token, cancellationToken);
        if (customerId is null)
        {
            return Array.Empty<SubscriptionDetails>();
        }

        await SettleUnknownClaimsAsync(user);

        IReadOnlyList<SubscriptionPlan> plans;
        try
        {
            plans = (await GetPlansCachedAsync(budget.Token, cancellationToken)).Plans;
        }
        catch (SubscriptionBillingException)
        {
            // Plan names are only a fallback for subscriptions whose product is not embedded.
            plans = Array.Empty<SubscriptionPlan>();
        }

        var subscriptions = await ListSubscriptionsAsync(customerId.Value, budget.Token, cancellationToken);
        return subscriptions
            .Select(s => ToDetails(s, plans.FirstOrDefault(p => p.Handle == s.Product?.Handle)))
            .OrderByDescending(s => s.CreatedAt)
            .ToList();
    }

    /// <summary>Best-effort: settles this user's creates whose outcome was left unknown.</summary>
    private async Task SettleUnknownClaimsAsync(BillingUser user)
    {
        var unknown = await _db.SubscriptionClaims
            .Where(c => c.UserId == user.UserId && c.Status == ClaimStatus.Pending && c.OutcomeUnknown)
            .ToListAsync();

        foreach (var claim in unknown)
        {
            try
            {
                claim.Retake(_time.GetUtcNow());
                if (!await TrySaveClaimAsync(claim))
                {
                    continue;
                }

                var plan = new SubscriptionPlan(claim.PlanHandle, claim.PlanHandle, null, 0, null, null);
                if (await TrySettleUnknownSubscriptionAsync(claim, plan) is null)
                {
                    await ReleaseAsync(claim);
                }
            }
            catch (ClaimRetainedException)
            {
                // Still unknown; left for a later request.
            }
        }
    }

    private async Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsAsync(int customerId, CancellationToken budgetToken, CancellationToken callerToken)
    {
        try
        {
            var responses = await _client.Customers.ListCustomerSubscriptions(
                new ListCustomerSubscriptionsRequest { CustomerId = customerId },
                cancellationToken: budgetToken);
            return responses
                .Select(r => r.Subscription)
                .Where(s => s?.Id is not null)
                .Select(s => s!)
                .ToList();
        }
        catch (SdkException ex)
        {
            throw TranslateFailure(ex, "ListCustomerSubscriptions");
        }
        catch (OperationCanceledException ex) when (!callerToken.IsCancellationRequested)
        {
            throw BudgetExpired(ex, "ListCustomerSubscriptions");
        }
    }

    // ---------------------------------------------------------------- helpers

    private static bool IsLive(MaxioSubscription subscription)
    {
        var state = subscription.State?.Value;
        return state is not ("canceled" or "expired" or "failed_to_create");
    }

    private static SubscriptionDetails ToDetails(MaxioSubscription subscription, SubscriptionPlan? plan) =>
        new(
            subscription.Id ?? 0,
            subscription.Reference,
            subscription.Product?.Handle ?? plan?.Handle,
            subscription.Product?.Name ?? plan?.Name,
            subscription.ProductPriceInCents is long cents ? cents / 100m : plan?.Price,
            subscription.Currency,
            subscription.State?.Value,
            subscription.CurrentPeriodEndsAt,
            subscription.CreatedAt);

    private CancellationTokenSource StartBudget(CancellationToken callerToken)
    {
        var budget = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        budget.CancelAfter(RequestBudget);
        return budget;
    }

    /// <summary>Saves a claim write; false when the store refuses it (duplicate key or a concurrent change).</summary>
    private async Task<bool> TrySaveClaimAsync(object claim)
    {
        try
        {
            await _db.SaveChangesAsync(CancellationToken.None);
            return true;
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException or ArgumentException)
        {
            // DbUpdateConcurrencyException (a DbUpdateException) on a stale token; the in-memory provider raises
            // ArgumentException / InvalidOperationException for a duplicate key, relational providers DbUpdateException.
            _logger.LogInformation("Billing claim refused by the store: {ClaimType}", claim.GetType().Name);
            _db.Entry(claim).State = EntityState.Detached;
            return false;
        }
    }

    private async Task ReleaseAsync(object claim)
    {
        try
        {
            var entry = _db.Entry(claim);
            if (entry.State == EntityState.Detached)
            {
                return;
            }

            entry.State = EntityState.Deleted;
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
        {
            // Left in place, the claim expires as stale and is taken over by a later request.
            _logger.LogWarning(ex, "Could not release billing claim {ClaimType}", claim.GetType().Name);
            _db.Entry(claim).State = EntityState.Detached;
        }
    }

    private static bool IsSuccessOrServerError(HttpStatusCode status) =>
        (int)status is >= 200 and < 300 or >= 500;

    private static SubscriptionBillingException InProgress() =>
        new(StatusCodes.Conflict, "A subscription request for this account is already in progress. Please retry shortly.");

    private SubscriptionBillingException BudgetExpired(OperationCanceledException ex, string operation)
    {
        _logger.LogError("Maxio {Operation} exceeded the {Budget}s request budget", operation, RequestBudget.TotalSeconds);
        return new SubscriptionBillingException(StatusCodes.ServiceUnavailable, "The billing provider did not respond in time.", ex);
    }

    /// <summary>
    /// One mapping for every SDK failure: provider rejections of our own credentials or quota are not the
    /// caller's fault (502/503); transport failures are 503; the SDK's own exception text never reaches the caller.
    /// </summary>
    private SubscriptionBillingException TranslateFailure(SdkException ex, string operation)
    {
        switch (ex)
        {
            case ResponseDeserializationException rde:
                _logger.LogError(ex, "Maxio {Operation} returned an unreadable {StatusCode} body (expected {TargetType})",
                    operation, (int)rde.StatusCode, rde.TargetType.Name);
                return new SubscriptionBillingException(StatusCodes.BadGateway, "The billing provider returned a response that could not be processed.", ex);

            case ApiException api:
                var status = (int)api.StatusCode;
                _logger.LogError(ex, "Maxio {Operation} failed with HTTP {StatusCode}", operation, status);
                return status switch
                {
                    401 or 403 => new SubscriptionBillingException(StatusCodes.BadGateway, "The billing provider rejected this application's credentials.", ex),
                    429 => new SubscriptionBillingException(StatusCodes.ServiceUnavailable, "The billing provider is busy. Please try again shortly.", ex),
                    >= 500 => new SubscriptionBillingException(StatusCodes.ServiceUnavailable, "The billing provider is unavailable. Please try again shortly.", ex),
                    _ => new SubscriptionBillingException(StatusCodes.BadGateway, "The billing provider rejected the request.", ex)
                };

            case SdkTimeoutException timeout:
                _logger.LogError(ex, "Maxio {Operation} timed out after {Timeout}", operation, timeout.Timeout);
                return new SubscriptionBillingException(StatusCodes.ServiceUnavailable, "The billing provider did not respond in time.", ex);

            case SdkConnectionException:
                _logger.LogError(ex, "Maxio {Operation} could not reach the provider", operation);
                return new SubscriptionBillingException(StatusCodes.ServiceUnavailable, "The billing provider is unreachable. Please try again shortly.", ex);

            case AuthSchemeException:
                _logger.LogError(ex, "Maxio {Operation} could not apply credentials", operation);
                return new SubscriptionBillingException(StatusCodes.BadGateway, "The billing provider credentials are not usable.", ex);

            default:
                _logger.LogError(ex, "Maxio {Operation} failed", operation);
                return new SubscriptionBillingException(StatusCodes.BadGateway, "The billing provider request failed.", ex);
        }
    }

    private static class StatusCodes
    {
        public const int Accepted = 202;
        public const int BadRequest = 400;
        public const int Conflict = 409;
        public const int UnprocessableEntity = 422;
        public const int BadGateway = 502;
        public const int ServiceUnavailable = 503;
    }

    /// <summary>A failure after which the subscription claim must be kept (its outcome is unknown), carrying the pending result.</summary>
    private sealed class ClaimRetainedException : Exception
    {
        public ClaimRetainedException(SubscriptionBillingException failure, SubscribeResult pending) : base(failure.Message, failure)
        {
            Failure = failure;
            Pending = pending;
        }

        public SubscriptionBillingException Failure { get; }
        public SubscribeResult Pending { get; }
    }

    /// <summary>A customer create whose outcome could not be settled: the customer claim must be kept.</summary>
    private sealed class CustomerOutcomeUnknownException : Exception
    {
        public CustomerOutcomeUnknownException(SubscriptionBillingException failure) : base(failure.Message, failure)
        {
            Failure = failure;
        }

        public SubscriptionBillingException Failure { get; }
    }
}
