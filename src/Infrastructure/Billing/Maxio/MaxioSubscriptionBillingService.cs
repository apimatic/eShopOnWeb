using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Subscription billing with Maxio Advanced Billing as the system of record.
/// <para>
/// Subscribe is idempotent per shopper: a claim row (primary key = shopper) is written before Maxio is called,
/// so a double-click or a concurrent request is refused locally. Customers are found/created by a deterministic
/// reference that Maxio keeps unique; subscriptions carry a per-claim reference so an ambiguous create (timeout,
/// dropped connection, unreadable reply) is settled by looking it up rather than by guessing.
/// </para>
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    public const int PlansPageSize = 200;
    public const int MaxPlanPages = 5;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly SubscriptionEnrollmentStore _store;
    private readonly MaxioSettings _settings;
    private readonly MaxioBillingTimeouts _timeouts;
    private readonly IMemoryCache _cache;
    private readonly TimeProvider _time;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        SubscriptionEnrollmentStore store,
        IOptions<MaxioSettings> settings,
        MaxioBillingTimeouts timeouts,
        IMemoryCache cache,
        TimeProvider time,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _store = store;
        _settings = settings.Value;
        _timeouts = timeouts;
        _cache = cache;
        _time = time;
        _logger = logger;
    }

    private string ProductFamilyHandle => _settings.ProductFamilyHandle!.Trim();

    public Task<SubscriptionPlanCatalog> ListPlansAsync(CancellationToken cancellationToken = default) =>
        WithinBudgetAsync(token => GetPlansAsync(forceRefresh: false, token), cancellationToken);

    public Task<SubscribeResult> SubscribeAsync(string buyerId, string planHandle, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(buyerId, nameof(buyerId));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));

        return WithinBudgetAsync(token => SubscribeCoreAsync(buyerId, planHandle.Trim(), token, cancellationToken), cancellationToken);
    }

    public Task<IReadOnlyList<SubscriptionDetails>> ListSubscriptionsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(buyerId, nameof(buyerId));

        return WithinBudgetAsync<IReadOnlyList<SubscriptionDetails>>(async token =>
        {
            var customer = await TryReadCustomerByReferenceAsync(CustomerReferenceFor(buyerId), token);
            if (customer?.Id is not int customerId)
            {
                return Array.Empty<SubscriptionDetails>();
            }

            var subscriptions = await ListCustomerSubscriptionsAsync(customerId, token);
            return subscriptions
                .OrderByDescending(s => s.CreatedAt)
                .Select(ToDetails)
                .ToList();
        }, cancellationToken);
    }

    /// <summary>
    /// The customer reference used for a shopper. Stable across restarts (unlike the Identity user id when the
    /// in-memory database is used), and unique per Maxio site.
    /// </summary>
    public static string CustomerReferenceFor(string buyerId) => $"eshop:{buyerId.Trim().ToLowerInvariant()}";

    // ---------------------------------------------------------------- subscribe

    private async Task<SubscribeResult> SubscribeCoreAsync(string buyerId, string planHandle, CancellationToken token, CancellationToken callerToken)
    {
        // Cross-operation invariant: only a handle from the configured family's product list may be subscribed to.
        var (plan, catalog) = await ResolvePlanAsync(planHandle, token);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var claim = new SubscriptionEnrollment(buyerId, plan.Handle, NewSubscriptionReference(), _time.GetUtcNow());
            if (await _store.TryClaimAsync(claim, token))
            {
                return await SubscribeUnderClaimAsync(claim, plan, catalog, token, callerToken);
            }

            // The store refused the claim: this shopper already has (or is getting) a subscription.
            var existing = await _store.FindAsync(buyerId, token);
            if (existing is null)
            {
                continue; // released in the meantime; claim again
            }

            var replay = await ResolveExistingClaimAsync(existing, plan, token);
            if (replay is not null)
            {
                return replay;
            }
            // The existing claim was released (stale or ended subscription); claim again.
        }

        throw new SubscriptionConflictException("Another subscription request for this account is in progress. Try again shortly.");
    }

    private async Task<SubscribeResult> SubscribeUnderClaimAsync(SubscriptionEnrollment claim, SubscriptionPlan plan,
        SubscriptionPlanCatalog catalog, CancellationToken token, CancellationToken callerToken)
    {
        var keepClaim = false;
        var createAttempted = false;

        // Release the claim when nothing can have been created; keep it (pending) when the create's outcome is unknown.
        bool ShouldRelease(Exception ex) =>
            !keepClaim && (!createAttempted || ex is BillingRequestRejectedException or BillingProviderException);

        try
        {
            var customer = await EnsureCustomerAsync(claim.BuyerId, token);
            var customerId = customer.Id!.Value;
            await _store.UpdateAsync(claim.BuyerId, claim.SubscriptionReference, e => e.AssignBillingCustomer(customerId), token);

            // State lost locally (e.g. in-memory database restarted) must not lead to a second subscription.
            var offeredHandles = catalog.Plans.Select(p => p.Handle).ToHashSet(StringComparer.Ordinal);
            var live = await FindLiveSubscriptionAsync(customerId, offeredHandles, token);
            if (live is not null)
            {
                var liveHandle = live.Product!.Handle!;
                await _store.UpdateAsync(claim.BuyerId, claim.SubscriptionReference,
                    e => e.MarkCompleted(customerId, live.Id!.Value, liveHandle, _time.GetUtcNow()), CancellationToken.None);
                keepClaim = true;

                _logger.LogInformation("Adopted existing Maxio subscription {SubscriptionId} for customer {CustomerId}", live.Id, customerId);
                if (liveHandle == plan.Handle)
                {
                    return new SubscribeResult(ToDetails(live), Created: false);
                }

                throw new SubscriptionConflictException(
                    $"This account already has an active '{liveHandle}' subscription; changing plans is not supported.");
            }

            createAttempted = true;
            var created = await CreateSubscriptionReconcilingAsync(claim, customerId, plan, token, callerToken);

            await _store.UpdateAsync(claim.BuyerId, claim.SubscriptionReference,
                e => e.MarkCompleted(customerId, created.Id!.Value, plan.Handle, _time.GetUtcNow()), CancellationToken.None);
            keepClaim = true;

            _logger.LogInformation("Created Maxio subscription {SubscriptionId} ({PlanHandle}) for customer {CustomerId}, reference {Reference}",
                created.Id, plan.Handle, customerId, claim.SubscriptionReference);
            return new SubscribeResult(ToDetails(created), Created: true);
        }
        catch (Exception ex) when (ShouldRelease(ex))
        {
            await _store.ReleaseAsync(claim.BuyerId, claim.SubscriptionReference, CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    /// Handles a subscribe request for a shopper whose claim already exists. Returns the existing subscription for
    /// an idempotent repeat, null when the old claim was released and may be taken again, or throws on conflict.
    /// </summary>
    private async Task<SubscribeResult?> ResolveExistingClaimAsync(SubscriptionEnrollment existing, SubscriptionPlan plan, CancellationToken token)
    {
        Subscription? subscription;
        if (existing.Status == SubscriptionEnrollmentStatus.Pending)
        {
            if (_time.GetUtcNow() - existing.ClaimedAt < _timeouts.StaleClaimAge)
            {
                throw new SubscriptionConflictException("A subscription request for this account is already being processed. Try again shortly.");
            }

            (existing, subscription) = await SettleStaleClaimAsync(existing, token);
            if (subscription is null)
            {
                return null;
            }
        }
        else
        {
            subscription = existing.BillingSubscriptionId is int id ? await TryReadSubscriptionAsync(id, token) : null;
            if (subscription is null)
            {
                await _store.ReleaseAsync(existing.BuyerId, existing.SubscriptionReference, token);
                return null;
            }
        }

        if (IsEnded(subscription.State))
        {
            _logger.LogInformation("Subscription {SubscriptionId} has ended ({State}); releasing the claim so the shopper can subscribe again",
                subscription.Id, subscription.State?.Value);
            await _store.ReleaseAsync(existing.BuyerId, existing.SubscriptionReference, token);
            return null;
        }

        if (existing.PlanHandle == plan.Handle)
        {
            return new SubscribeResult(ToDetails(subscription), Created: false);
        }

        throw new SubscriptionConflictException(
            $"This account already has a '{existing.PlanHandle}' subscription; changing plans is not supported.");
    }

    /// <summary>
    /// A pending claim older than any in-flight request: its create either landed at Maxio (find it by the
    /// reference we sent) or never did (release the claim).
    /// </summary>
    private async Task<(SubscriptionEnrollment Enrollment, Subscription? Subscription)> SettleStaleClaimAsync(
        SubscriptionEnrollment stale, CancellationToken token)
    {
        var found = await TryFindSubscriptionByReferenceAsync(stale.SubscriptionReference, token);
        var customerId = found?.Customer?.Id ?? stale.BillingCustomerId;
        if (found?.Id is not int subscriptionId || customerId is null
            || (stale.BillingCustomerId is int expected && found.Customer?.Id is int actual && actual != expected))
        {
            _logger.LogWarning("Releasing stale subscription claim {Reference}: Maxio holds no subscription for it", stale.SubscriptionReference);
            await _store.ReleaseAsync(stale.BuyerId, stale.SubscriptionReference, token);
            return (stale, null);
        }

        _logger.LogWarning("Settled stale subscription claim {Reference}: Maxio subscription {SubscriptionId} exists", stale.SubscriptionReference, subscriptionId);
        var updated = await _store.UpdateAsync(stale.BuyerId, stale.SubscriptionReference,
            e => e.MarkCompleted(customerId.Value, subscriptionId, found.Product?.Handle ?? stale.PlanHandle, _time.GetUtcNow()), token);
        return (updated ?? stale, found);
    }

    private async Task<Subscription> CreateSubscriptionReconcilingAsync(SubscriptionEnrollment claim, int customerId,
        SubscriptionPlan plan, CancellationToken token, CancellationToken callerToken)
    {
        try
        {
            var response = await _client.Subscriptions.CreateSubscription(new CreateSubscriptionOperationRequest
            {
                Body = new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = plan.Handle,
                        CustomerId = customerId,
                        Reference = claim.SubscriptionReference,
                        // No card is captured in this flow: with the default (automatic) collection Maxio refuses the
                        // signup charge for lack of a payment method, so the shopper is billed by invoice instead.
                        PaymentCollectionMethod = CollectionMethod.Remittance
                    }
                }
            }, cancellationToken: token);

            if (response.Subscription is { Id: not null } subscription)
            {
                return subscription;
            }

            return await ReconcileCreateAsync(claim, customerId, cause: null, timedOut: false, callerToken);
        }
        catch (ApiException<CreateSubscriptionError> ex) when ((int)ex.StatusCode >= 500)
        {
            // A server error on a write does not prove nothing was written.
            return await ReconcileCreateAsync(claim, customerId, ex, timedOut: false, callerToken);
        }
        catch (ApiException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var rejection))
            {
                _logger.LogWarning("Maxio rejected subscription {Reference} with HTTP {Status}", claim.SubscriptionReference, (int)ex.StatusCode);
                throw new BillingRequestRejectedException("Maxio rejected the subscription.", rejection.Errors ?? Array.Empty<string>(), ex);
            }

            throw FromStatus(ex.StatusCode, "creating the subscription", ex);
        }
        catch (ResponseDeserializationException ex) when ((int)ex.StatusCode is >= 200 and < 300 or >= 500)
        {
            return await ReconcileCreateAsync(claim, customerId, ex, timedOut: false, callerToken);
        }
        catch (SdkConnectionException ex)
        {
            // Includes SdkTimeoutException: the request may have reached Maxio before the connection failed.
            return await ReconcileCreateAsync(claim, customerId, ex, timedOut: ex is SdkTimeoutException, callerToken);
        }
        catch (OperationCanceledException ex) when (!callerToken.IsCancellationRequested)
        {
            // The request budget ran out while the create was in flight.
            return await ReconcileCreateAsync(claim, customerId, ex, timedOut: true, callerToken);
        }
        catch (SdkException ex)
        {
            throw Translate(ex, "creating the subscription");
        }
    }

    /// <summary>
    /// Settles a create whose outcome is unknown by looking the subscription up by the reference we sent. When that
    /// is not conclusive the claim stays pending and is settled on the shopper's next attempt.
    /// </summary>
    private async Task<Subscription> ReconcileCreateAsync(SubscriptionEnrollment claim, int customerId, Exception? cause,
        bool timedOut, CancellationToken callerToken)
    {
        _logger.LogWarning(cause, "Outcome of Maxio subscription create {Reference} is unknown; reconciling", claim.SubscriptionReference);

        using var reconcile = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        reconcile.CancelAfter(_timeouts.ReconciliationBudget);
        try
        {
            var found = await _client.Subscriptions.FindSubscription(
                new FindSubscriptionRequest { Reference = claim.SubscriptionReference }, cancellationToken: reconcile.Token);
            if (found.Subscription is { Id: not null } subscription
                && (subscription.Customer?.Id is null || subscription.Customer.Id == customerId))
            {
                _logger.LogInformation("Reconciled subscription create {Reference}: Maxio subscription {SubscriptionId} exists",
                    claim.SubscriptionReference, subscription.Id);
                return subscription;
            }
        }
        catch (ApiException<FindSubscriptionError> ex) when (ex.Error.TryGetNoContent(out _))
        {
            // Not found (yet). The create may still be in flight at Maxio, so the claim stays pending.
        }
        catch (Exception ex) when (ex is SdkException || (ex is OperationCanceledException && !callerToken.IsCancellationRequested))
        {
            _logger.LogWarning(ex, "Reconciling subscription create {Reference} failed", claim.SubscriptionReference);
        }

        _logger.LogWarning("Subscription create {Reference} remains unconfirmed; the claim stays pending", claim.SubscriptionReference);
        throw new BillingProviderUnavailableException(
            timedOut
                ? "Maxio did not respond in time, so it is not yet known whether the subscription was created. Check your subscriptions, or retry in a few minutes."
                : "Maxio did not confirm the subscription, so it is not yet known whether it was created. Check your subscriptions, or retry in a few minutes.",
            timedOut: true,
            cause);
    }

    // ---------------------------------------------------------------- customers

    private async Task<Customer> EnsureCustomerAsync(string buyerId, CancellationToken token)
    {
        var reference = CustomerReferenceFor(buyerId);
        var existing = await TryReadCustomerByReferenceAsync(reference, token);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var created = await _client.Customers.CreateCustomer(new CreateCustomerOperationRequest
            {
                Body = new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = FirstNameFor(buyerId),
                        LastName = "Customer",
                        Email = buyerId.Trim(),
                        Reference = reference
                    }
                }
            }, cancellationToken: token);

            _logger.LogInformation("Created Maxio customer {CustomerId}", created.Customer?.Id);
            return RequireId(created.Customer);
        }
        catch (ApiException<CreateCustomerError> ex)
        {
            // Maxio keeps customer references unique: a concurrent create for this shopper may have won.
            var raced = await TryReadCustomerByReferenceAsync(reference, token);
            if (raced is not null)
            {
                return raced;
            }

            if (ex.Error.TryGetCustomerErrorResponse1(out var rejection))
            {
                throw new BillingRequestRejectedException("Maxio rejected the customer record.", ErrorMessages(rejection), ex);
            }

            throw FromStatus(ex.StatusCode, "creating the customer", ex);
        }
        catch (SdkConnectionException ex)
        {
            // The customer may have been created before the connection failed: look it up by its reference.
            var settled = await TryReadCustomerByReferenceAsync(reference, token);
            if (settled is not null)
            {
                return settled;
            }

            throw Translate(ex, "creating the customer");
        }
        catch (SdkException ex)
        {
            throw Translate(ex, "creating the customer");
        }
    }

    private async Task<Customer?> TryReadCustomerByReferenceAsync(string reference, CancellationToken token)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = reference }, cancellationToken: token);
            return RequireId(response.Customer);
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException ex)
        {
            throw Translate(ex, "looking up the customer");
        }
    }

    private static Customer RequireId(Customer? customer) =>
        customer?.Id is not null
            ? customer
            : throw new BillingProviderException("Maxio returned a customer without an id.");

    private static string FirstNameFor(string buyerId)
    {
        var trimmed = buyerId.Trim();
        var at = trimmed.IndexOf('@');
        return at > 0 ? trimmed[..at] : trimmed;
    }

    private static IReadOnlyList<string> ErrorMessages(CustomerErrorResponse1 rejection) =>
        rejection.Errors is not null && rejection.Errors.TryGetListOfString(out var messages)
            ? messages
            : new[] { "The customer record was rejected." };

    // ---------------------------------------------------------------- subscriptions (reads)

    private async Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken token)
    {
        try
        {
            var responses = await _client.Customers.ListCustomerSubscriptions(
                new ListCustomerSubscriptionsRequest { CustomerId = customerId }, cancellationToken: token);
            return responses
                .Select(r => r?.Subscription)
                .Where(s => s?.Id is not null)
                .Select(s => s!)
                .ToList();
        }
        catch (SdkException ex)
        {
            throw Translate(ex, "listing the customer's subscriptions");
        }
    }

    private async Task<Subscription?> FindLiveSubscriptionAsync(int customerId, IReadOnlySet<string> offeredHandles, CancellationToken token)
    {
        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, token);
        return subscriptions
            .Where(s => !IsEnded(s.State) && s.Product?.Handle is string handle && offeredHandles.Contains(handle))
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefault();
    }

    private async Task<Subscription?> TryReadSubscriptionAsync(int subscriptionId, CancellationToken token)
    {
        try
        {
            var response = await _client.Subscriptions.ReadSubscription(
                new ReadSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: token);
            return response.Subscription?.Id is not null ? response.Subscription : null;
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException ex)
        {
            throw Translate(ex, "reading the subscription");
        }
    }

    private async Task<Subscription?> TryFindSubscriptionByReferenceAsync(string reference, CancellationToken token)
    {
        try
        {
            var response = await _client.Subscriptions.FindSubscription(
                new FindSubscriptionRequest { Reference = reference }, cancellationToken: token);
            return response.Subscription;
        }
        catch (ApiException<FindSubscriptionError> ex) when (ex.Error.TryGetNoContent(out _))
        {
            return null;
        }
        catch (SdkException ex)
        {
            throw Translate(ex, "looking up the subscription");
        }
    }

    private static bool IsEnded(SubscriptionState? state) =>
        state == SubscriptionState.Canceled || state == SubscriptionState.Expired || state == SubscriptionState.FailedToCreate;

    private static SubscriptionDetails ToDetails(Subscription subscription) => new(
        SubscriptionId: subscription.Id ?? throw new BillingProviderException("Maxio returned a subscription without an id."),
        PlanHandle: subscription.Product?.Handle,
        PlanName: subscription.Product?.Name,
        PriceInCents: subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents,
        Currency: subscription.Currency,
        Interval: subscription.Product?.Interval,
        IntervalUnit: subscription.Product?.IntervalUnit?.Value,
        State: subscription.State?.Value ?? "unknown",
        // next_assessment_at is when Maxio next bills; it tracks current_period_ends_at except during payment retries.
        NextBillingAt: subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        CurrentPeriodEndsAt: subscription.CurrentPeriodEndsAt,
        CreatedAt: subscription.CreatedAt);

    private static string NewSubscriptionReference() => $"eshop-sub-{Guid.NewGuid():N}";

    // ---------------------------------------------------------------- plans

    private async Task<(SubscriptionPlan Plan, SubscriptionPlanCatalog Catalog)> ResolvePlanAsync(string planHandle, CancellationToken token)
    {
        var catalog = await GetPlansAsync(forceRefresh: false, token);
        var plan = catalog.Plans.FirstOrDefault(p => p.Handle == planHandle);
        if (plan is null)
        {
            // The cached list may predate a newly added plan.
            catalog = await GetPlansAsync(forceRefresh: true, token);
            plan = catalog.Plans.FirstOrDefault(p => p.Handle == planHandle);
        }

        return plan is null ? throw new SubscriptionPlanNotFoundException(planHandle) : (plan, catalog);
    }

    private async Task<SubscriptionPlanCatalog> GetPlansAsync(bool forceRefresh, CancellationToken token)
    {
        var cacheKey = $"maxio:plans:{ProductFamilyHandle}";
        if (!forceRefresh && _cache.TryGetValue(cacheKey, out SubscriptionPlanCatalog? cached) && cached is not null)
        {
            return cached;
        }

        var catalog = await FetchPlansAsync(token);
        _cache.Set(cacheKey, catalog, _timeouts.PlanCacheDuration);
        return catalog;
    }

    /// <summary>
    /// Walks the product family's pages, bounded by <see cref="MaxPlanPages"/>; a full last page marks the result
    /// as <see cref="SubscriptionPlanCatalog.Truncated"/>.
    /// </summary>
    private async Task<SubscriptionPlanCatalog> FetchPlansAsync(CancellationToken token)
    {
        var family = ProductFamilyHandle;
        var plans = new List<SubscriptionPlan>();
        var truncated = false;

        for (var page = 1; ; page++)
        {
            IReadOnlyList<ProductResponse> batch;
            try
            {
                batch = await _client.ProductFamilies.ListProductsForProductFamily(new ListProductsForProductFamilyRequest
                {
                    ProductFamilyId = $"handle:{family}",
                    Page = page,
                    PerPage = PlansPageSize,
                    IncludeArchived = false
                }, cancellationToken: token);
            }
            catch (ApiException<ListProductsForProductFamilyError> ex) when (ex.Error.TryGetString(out _))
            {
                throw new BillingProviderException($"The configured Maxio product family '{family}' was not found.", (int)ex.StatusCode, ex);
            }
            catch (SdkException ex)
            {
                throw Translate(ex, "listing subscription plans");
            }

            foreach (var product in batch.Select(r => r?.Product))
            {
                if (product?.Id is not int id || string.IsNullOrWhiteSpace(product.Handle) || product.ArchivedAt is not null)
                {
                    continue;
                }

                plans.Add(new SubscriptionPlan(
                    id,
                    product.Handle,
                    product.Name ?? product.Handle,
                    product.Description,
                    product.PriceInCents ?? 0,
                    product.Interval,
                    product.IntervalUnit?.Value));
            }

            if (batch.Count < PlansPageSize)
            {
                break;
            }

            if (page >= MaxPlanPages)
            {
                truncated = true;
                _logger.LogWarning("Product family {Family} has more than {Max} products; the plan list is truncated", family, PlansPageSize * MaxPlanPages);
                break;
            }
        }

        _logger.LogInformation("Loaded {Count} subscription plans from Maxio product family {Family}", plans.Count, family);
        return new SubscriptionPlanCatalog(family, plans, truncated);
    }

    // ---------------------------------------------------------------- budget & error boundary

    /// <summary>
    /// Every Maxio call of one API request shares this deadline, so a slow Maxio cannot hold the caller longer than
    /// <see cref="MaxioBillingTimeouts.RequestBudget"/> (+ the reconciliation reserve for an ambiguous create).
    /// </summary>
    private async Task<T> WithinBudgetAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken callerToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        budget.CancelAfter(_timeouts.RequestBudget);
        try
        {
            return await work(budget.Token);
        }
        catch (OperationCanceledException ex) when (budget.IsCancellationRequested && !callerToken.IsCancellationRequested)
        {
            _logger.LogWarning("Maxio did not respond within the {Budget}s request budget", _timeouts.RequestBudget.TotalSeconds);
            throw new BillingProviderUnavailableException(
                $"Maxio did not respond within {_timeouts.RequestBudget.TotalSeconds:0} seconds. Please try again.", timedOut: true, ex);
        }
    }

    private Exception Translate(SdkException ex, string action)
    {
        switch (ex)
        {
            case SdkTimeoutException:
                _logger.LogWarning(ex, "Maxio timed out while {Action}", action);
                return new BillingProviderUnavailableException($"Maxio did not respond in time while {action}.", timedOut: true, ex);
            case SdkConnectionException:
                _logger.LogWarning(ex, "Maxio could not be reached while {Action}", action);
                return new BillingProviderUnavailableException($"Maxio could not be reached while {action}.", timedOut: false, ex);
            case ResponseDeserializationException rde:
                _logger.LogError(ex, "Unreadable Maxio response (HTTP {Status}) while {Action}", (int)rde.StatusCode, action);
                return new BillingProviderException($"Maxio returned a response that could not be processed while {action}.", (int)rde.StatusCode, ex);
            case ApiException api:
                return FromStatus(api.StatusCode, action, ex);
            default:
                // AuthSchemeException and anything else: our side of the integration is misconfigured.
                _logger.LogError(ex, "Maxio call failed while {Action}", action);
                return new BillingProviderException($"The Maxio integration failed while {action}.", null, ex);
        }
    }

    private Exception FromStatus(HttpStatusCode status, string action, Exception ex)
    {
        var code = (int)status;
        if (code is 401 or 403)
        {
            _logger.LogError(ex, "Maxio rejected the configured credentials (HTTP {Status}) while {Action}", code, action);
            return new BillingProviderException("Maxio rejected the configured credentials.", code, ex);
        }

        if (code == 429)
        {
            _logger.LogWarning(ex, "Maxio is rate limiting (HTTP 429) while {Action}", action);
            return new BillingProviderUnavailableException("Maxio is busy; please try again shortly.", timedOut: false, ex);
        }

        if (code is 400 or 422)
        {
            _logger.LogWarning(ex, "Maxio rejected the request (HTTP {Status}) while {Action}", code, action);
            return new BillingRequestRejectedException($"Maxio rejected the request while {action}.", Array.Empty<string>(), ex);
        }

        _logger.LogError(ex, "Maxio returned HTTP {Status} while {Action}", code, action);
        return new BillingProviderException($"Maxio returned HTTP {code} while {action}.", code, ex);
    }
}
