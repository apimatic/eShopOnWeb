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
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Subscription billing backed by Maxio Advanced Billing.
/// Duplicate prevention: every enrollment write runs under a claim row keyed by the shopper's billing reference
/// (<see cref="ISubscriptionEnrollmentStore"/>), taken before any provider write. Ambiguous writes (the
/// connection failed after the request may have landed) are settled by re-reading Maxio by the deterministic
/// reference the write carried.
/// </summary>
public class MaxioBillingService : ISubscriptionBillingService
{
    public const int PLANS_PER_PAGE = 200;   // Maxio caps per_page at 200
    public const int MAX_PLAN_PAGES = 5;
    private const string PLAN_CACHE_KEY = "maxio:plans";
    private static readonly SemaphoreSlim PlanRefreshLock = new(1, 1);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly ISubscriptionEnrollmentStore _store;
    private readonly MaxioSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly TimeProvider _clock;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(MaxioAdvancedBillingClient client, ISubscriptionEnrollmentStore store,
        IOptions<MaxioSettings> settings, IMemoryCache cache, TimeProvider clock, ILogger<MaxioBillingService> logger)
    {
        _client = client;
        _store = store;
        _settings = settings.Value;
        _cache = cache;
        _clock = clock;
        _logger = logger;
    }

    // ---------------------------------------------------------------- plans

    public async Task<PlanCatalog> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(PLAN_CACHE_KEY, out PlanCatalog? cached) && cached != null)
        {
            return cached;
        }

        // Single-flight refresh: concurrent cold-cache requests share one provider listing.
        await PlanRefreshLock.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGetValue(PLAN_CACHE_KEY, out cached) && cached != null)
            {
                return cached;
            }
            return await FetchPlansAsync(cancellationToken);
        }
        finally
        {
            PlanRefreshLock.Release();
        }
    }

    private async Task<PlanCatalog> FetchPlansAsync(CancellationToken cancellationToken)
    {
        using var budget = StartBudget(cancellationToken);
        var familyId = "handle:" + _settings.ProductFamilyHandle!.Trim();
        var plans = new List<SubscriptionPlan>();
        var isComplete = false;

        for (var page = 1; page <= MAX_PLAN_PAGES; page++)
        {
            IReadOnlyList<ProductResponse> products;
            try
            {
                products = await _client.ProductFamilies.ListProductsForProductFamily(
                    new ListProductsForProductFamilyRequest
                    {
                        ProductFamilyId = familyId,
                        Page = page,
                        PerPage = PLANS_PER_PAGE
                    },
                    cancellationToken: budget.Token);
            }
            catch (ApiException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out _))
                {
                    // 404: the configured family does not exist on this site — our configuration, not the caller's request.
                    _logger.LogError("Maxio product family configured in {Setting} was not found (HTTP {Status}).",
                        $"{MaxioSettings.CONFIG_NAME}:ProductFamilyHandle", (int)ex.StatusCode);
                    throw new SubscriptionBillingException(BillingFailureKind.Unavailable,
                        "Subscription plans are not available.", (int)ex.StatusCode, innerException: ex);
                }
                if (ex.Error.TryGetRawError(out RawError raw))
                {
                    throw FromStatus("ListProductsForProductFamily", raw.StatusCode, ex);
                }
                throw FromStatus("ListProductsForProductFamily", ex.StatusCode, ex);
            }
            catch (Exception ex) when (IsSdkOrBudgetFailure(ex, budget))
            {
                throw Translate("ListProductsForProductFamily", ex, budget);
            }

            foreach (var item in products)
            {
                var product = item?.Product;
                if (product?.Handle is not { Length: > 0 } handle || product.ArchivedAt != null)
                {
                    continue;
                }
                plans.Add(new SubscriptionPlan(
                    product.Id,
                    handle,
                    product.Name ?? handle,
                    product.Description,
                    product.PriceInCents ?? 0,
                    product.Interval,
                    product.IntervalUnit?.Value,
                    product.RequireCreditCard ?? false));
            }

            if (products.Count < PLANS_PER_PAGE)
            {
                isComplete = true;
                break;
            }
        }

        if (!isComplete)
        {
            _logger.LogWarning("Maxio plan listing stopped at the {MaxPages}-page cap; result marked incomplete.", MAX_PLAN_PAGES);
        }

        var catalog = new PlanCatalog(plans, isComplete);
        if (_settings.PlanCacheSeconds > 0)
        {
            _cache.Set(PLAN_CACHE_KEY, catalog, TimeSpan.FromSeconds(_settings.PlanCacheSeconds));
        }
        return catalog;
    }

    // ---------------------------------------------------------------- subscribe

    public async Task<SubscribeResult> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new SubscriptionBillingException(BillingFailureKind.InvalidRequest, "A plan handle is required.");
        }

        // Invariant: only a plan this application offers (the configured family's list) may be subscribed to.
        var plan = await FindOfferedPlanAsync(planHandle.Trim(), cancellationToken);

        var userReference = BillingReferences.ForUser(userName);
        using var budget = StartBudget(cancellationToken);
        var now = _clock.GetUtcNow();

        // Claim first: the store's primary key refuses a second claim for the same shopper.
        var enrollment = new SubscriptionEnrollment(userReference, userName, plan.Handle, now);
        if (!await _store.TryClaimAsync(enrollment, budget.Token))
        {
            var existing = await _store.GetAsync(userReference, budget.Token);
            if (existing == null)
            {
                return new SubscribeResult(SubscribeStatus.InProgress, null, "Another request for this account is being processed. Try again shortly.");
            }

            if (existing.Status == SubscriptionEnrollmentStatus.Active)
            {
                var current = await FindSubscriptionAsync(BillingReferences.ForSubscription(userReference), budget);
                if (current != null)
                {
                    return SameOrConflict(current, plan);
                }
                // Our record says active but Maxio no longer has it — fall through and re-enroll under a takeover.
            }
            else if (existing.Status == SubscriptionEnrollmentStatus.Pending && !existing.IsStale(now, PendingClaimTimeout))
            {
                return new SubscribeResult(SubscribeStatus.InProgress, null, "Another request for this account is being processed. Try again shortly.");
            }

            // Stale pending, unknown outcome, or vanished subscription: take the claim over atomically.
            var expectedToken = existing.ClaimToken;
            existing.TakeOver(plan.Handle, now);
            if (!await _store.TryUpdateAsync(existing, expectedToken, budget.Token))
            {
                return new SubscribeResult(SubscribeStatus.InProgress, null, "Another request for this account is being processed. Try again shortly.");
            }
            enrollment = existing;
        }

        try
        {
            return await EnrollAsync(enrollment, plan, userName, budget);
        }
        catch (SubscriptionBillingException ex) when (ex is not AlreadySubscribedException)
        {
            // The provider definitively refused (or a read failed before any write): nothing is held at Maxio
            // for this attempt, so release the claim and let the shopper retry.
            await _store.ReleaseAsync(enrollment, CancellationToken.None);
            throw;
        }
    }

    private async Task<SubscribeResult> EnrollAsync(SubscriptionEnrollment enrollment, SubscriptionPlan plan, string userName, Budget budget)
    {
        var subscriptionReference = BillingReferences.ForSubscription(enrollment.UserReference);

        // 1. A subscription with our reference may already exist (process restart, earlier unknown outcome): adopt it.
        var existing = await FindSubscriptionAsync(subscriptionReference, budget);
        if (existing != null)
        {
            await CompleteAsync(enrollment, existing, plan.Handle);
            _logger.LogInformation("Adopted existing Maxio subscription {SubscriptionId} for {UserReference}.", existing.Id, enrollment.UserReference);
            return SameOrConflict(existing, plan);
        }

        // 2. Ensure the Maxio customer exists for this shopper.
        var customerId = await EnsureCustomerAsync(enrollment, userName, budget);
        if (customerId == null)
        {
            return await MarkUnknownAsync(enrollment, "CreateCustomer");
        }

        enrollment.RecordCustomer(customerId.Value, _clock.GetUtcNow());
        if (!await _store.TryUpdateAsync(enrollment, enrollment.ClaimToken, budget.Token))
        {
            return new SubscribeResult(SubscribeStatus.InProgress, null, "Another request for this account is being processed. Try again shortly.");
        }

        // 3. Create the subscription.
        return await CreateSubscriptionAsync(enrollment, plan, customerId.Value, subscriptionReference, budget);
    }

    /// <summary>Returns the Maxio customer id, or null when a create's outcome could not be settled.</summary>
    private async Task<int?> EnsureCustomerAsync(SubscriptionEnrollment enrollment, string userName, Budget budget)
    {
        var customerReference = BillingReferences.ForCustomer(enrollment.UserReference);
        var existing = await ReadCustomerByReferenceAsync(customerReference, budget);
        if (existing?.Id is int existingId)
        {
            return existingId;
        }

        var (firstName, lastName, email) = BillingReferences.CustomerNames(userName);
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
                            LastName = lastName,
                            Email = email,
                            Reference = customerReference
                        }
                    }
                },
                cancellationToken: budget.Token);

            if (response.Customer?.Id is int createdId)
            {
                _logger.LogInformation("Created Maxio customer {CustomerId} for {UserReference}.", createdId, enrollment.UserReference);
                return createdId;
            }
            // 2xx without an id: the create landed but we cannot read it — settle by reference.
            return await ReconcileCustomerAsync(customerReference);
        }
        catch (ApiException<CreateCustomerError> ex)
        {
            var errors = new List<string>();
            if (ex.Error.TryGetCustomerErrorResponse1(out var body))
            {
                errors.AddRange(CustomerErrors(body));
            }
            else if (ex.Error.TryGetRawError(out RawError raw))
            {
                throw FromStatus("CreateCustomer", raw.StatusCode, ex);
            }

            // A 422 can mean the reference is already taken (a create that landed earlier): check before reporting.
            var raced = await ReconcileCustomerAsync(customerReference);
            if (raced != null)
            {
                return raced;
            }
            _logger.LogWarning("Maxio rejected customer creation for {UserReference} (HTTP {Status}): {Errors}",
                enrollment.UserReference, (int)ex.StatusCode, string.Join("; ", errors));
            throw new SubscriptionBillingException(BillingFailureKind.InvalidRequest,
                "The billing provider rejected the customer details.", (int)ex.StatusCode, errors, ex);
        }
        catch (ResponseDeserializationException ex) when ((int)ex.StatusCode is >= 200 and < 300)
        {
            _logger.LogWarning(ex, "Unreadable CreateCustomer response for {UserReference}; reconciling by reference.", enrollment.UserReference);
            return await ReconcileCustomerAsync(customerReference);
        }
        catch (SdkConnectionException ex)
        {
            // The create may have landed: settle by re-reading the reference we sent.
            _logger.LogWarning(ex, "Connection failed during CreateCustomer for {UserReference}; reconciling by reference.", enrollment.UserReference);
            return await ReconcileCustomerAsync(customerReference);
        }
        catch (OperationCanceledException) when (budget.Expired)
        {
            _logger.LogWarning("Billing budget expired during CreateCustomer for {UserReference}; reconciling by reference.", enrollment.UserReference);
            return await ReconcileCustomerAsync(customerReference);
        }
        catch (OperationCanceledException)
        {
            await MarkUnknownAsync(enrollment, "CreateCustomer");
            throw;
        }
        catch (Exception ex) when (IsSdkOrBudgetFailure(ex, budget))
        {
            throw Translate("CreateCustomer", ex, budget);
        }
    }

    private async Task<SubscribeResult> CreateSubscriptionAsync(SubscriptionEnrollment enrollment, SubscriptionPlan plan,
        int customerId, string subscriptionReference, Budget budget)
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
                            Reference = subscriptionReference,
                            PaymentCollectionMethod = _settings.CollectionMethod
                        }
                    }
                },
                cancellationToken: budget.Token);

            var created = response.Subscription;
            if (created?.Id is not int)
            {
                return await ReconcileSubscriptionAsync(enrollment, plan, subscriptionReference, "CreateSubscription returned no subscription");
            }

            await CompleteAsync(enrollment, created, plan.Handle);
            _logger.LogInformation("Created Maxio subscription {SubscriptionId} on plan {PlanHandle} for {UserReference}.",
                created.Id, plan.Handle, enrollment.UserReference);
            return new SubscribeResult(SubscribeStatus.Created, Map(created));
        }
        catch (ApiException<CreateSubscriptionError> ex)
        {
            IReadOnlyList<string> errors = Array.Empty<string>();
            if (ex.Error.TryGetErrorListResponse1(out var body))
            {
                errors = body.Errors ?? Array.Empty<string>();
            }
            else if (ex.Error.TryGetRawError(out RawError raw))
            {
                throw FromStatus("CreateSubscription", raw.StatusCode, ex);
            }

            // A 422 can mean our reference is already in use (an earlier create landed): check before reporting.
            var raced = await FindSubscriptionAfterFailureAsync(subscriptionReference);
            if (raced != null)
            {
                await CompleteAsync(enrollment, raced, plan.Handle);
                return SameOrConflict(raced, plan);
            }
            _logger.LogWarning("Maxio rejected subscription creation for {UserReference} on {PlanHandle} (HTTP {Status}): {Errors}",
                enrollment.UserReference, plan.Handle, (int)ex.StatusCode, string.Join("; ", errors));
            throw new SubscriptionBillingException(BillingFailureKind.InvalidRequest,
                "The billing provider rejected the subscription.", (int)ex.StatusCode, errors, ex);
        }
        catch (ResponseDeserializationException ex) when ((int)ex.StatusCode is >= 200 and < 300)
        {
            return await ReconcileSubscriptionAsync(enrollment, plan, subscriptionReference, "unreadable CreateSubscription response");
        }
        catch (SdkConnectionException ex)
        {
            _logger.LogWarning(ex, "Connection failed during CreateSubscription for {UserReference}.", enrollment.UserReference);
            return await ReconcileSubscriptionAsync(enrollment, plan, subscriptionReference, "connection failure");
        }
        catch (OperationCanceledException) when (budget.Expired)
        {
            return await ReconcileSubscriptionAsync(enrollment, plan, subscriptionReference, "billing budget expired");
        }
        catch (OperationCanceledException)
        {
            // The caller went away mid-write: keep the claim as unknown; the next call settles it.
            await MarkUnknownAsync(enrollment, "CreateSubscription");
            throw;
        }
        catch (Exception ex) when (IsSdkOrBudgetFailure(ex, budget))
        {
            throw Translate("CreateSubscription", ex, budget);
        }
    }

    /// <summary>Settles an ambiguous CreateSubscription by looking the subscription up by the reference it carried.</summary>
    private async Task<SubscribeResult> ReconcileSubscriptionAsync(SubscriptionEnrollment enrollment, SubscriptionPlan plan,
        string subscriptionReference, string reason)
    {
        _logger.LogWarning("CreateSubscription outcome unknown for {UserReference} ({Reason}); reconciling by reference.",
            enrollment.UserReference, reason);
        var found = await FindSubscriptionAfterFailureAsync(subscriptionReference);
        if (found != null)
        {
            await CompleteAsync(enrollment, found, plan.Handle);
            _logger.LogInformation("Reconciled Maxio subscription {SubscriptionId} for {UserReference}.", found.Id, enrollment.UserReference);
            return new SubscribeResult(SubscribeStatus.Created, Map(found));
        }
        return await MarkUnknownAsync(enrollment, "CreateSubscription");
    }

    private async Task<int?> ReconcileCustomerAsync(string customerReference)
    {
        using var reconcile = StartReconcileBudget();
        try
        {
            var customer = await ReadCustomerByReferenceAsync(customerReference, reconcile);
            return customer?.Id;
        }
        catch (SubscriptionBillingException ex)
        {
            _logger.LogWarning(ex, "Customer reconciliation read failed; outcome stays unknown.");
            return null;
        }
    }

    private async Task<Subscription?> FindSubscriptionAfterFailureAsync(string subscriptionReference)
    {
        using var reconcile = StartReconcileBudget();
        try
        {
            return await FindSubscriptionAsync(subscriptionReference, reconcile);
        }
        catch (SubscriptionBillingException ex)
        {
            _logger.LogWarning(ex, "Subscription reconciliation read failed; outcome stays unknown.");
            return null;
        }
    }

    private async Task<SubscribeResult> MarkUnknownAsync(SubscriptionEnrollment enrollment, string operation)
    {
        enrollment.MarkUnknown(_clock.GetUtcNow());
        await _store.TryUpdateAsync(enrollment, enrollment.ClaimToken, CancellationToken.None);
        _logger.LogWarning("{Operation} outcome for {UserReference} is unknown; it will be settled on the next request.",
            operation, enrollment.UserReference);
        return new SubscribeResult(SubscribeStatus.OutcomeUnknown, null,
            "The billing provider did not confirm the subscription. Check your subscriptions shortly; retrying is safe.");
    }

    private async Task CompleteAsync(SubscriptionEnrollment enrollment, Subscription subscription, string fallbackPlanHandle)
    {
        enrollment.MarkActive(subscription.Id!.Value, subscription.Customer?.Id,
            subscription.Product?.Handle ?? fallbackPlanHandle, _clock.GetUtcNow());
        await _store.TryUpdateAsync(enrollment, enrollment.ClaimToken, CancellationToken.None);
    }

    private SubscribeResult SameOrConflict(Subscription subscription, SubscriptionPlan requested)
    {
        var heldPlan = subscription.Product?.Handle;
        if (heldPlan == null || string.Equals(heldPlan, requested.Handle, StringComparison.OrdinalIgnoreCase))
        {
            return new SubscribeResult(SubscribeStatus.AlreadySubscribed, Map(subscription));
        }
        throw new AlreadySubscribedException(Map(subscription));
    }

    private async Task<SubscriptionPlan> FindOfferedPlanAsync(string planHandle, CancellationToken cancellationToken)
    {
        var catalog = await GetPlansAsync(cancellationToken);
        var plan = catalog.Plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan == null)
        {
            throw new SubscriptionBillingException(BillingFailureKind.InvalidRequest,
                $"'{planHandle}' is not an available subscription plan.");
        }
        return plan;
    }

    // ---------------------------------------------------------------- my subscriptions

    public async Task<MySubscriptions> GetMySubscriptionsAsync(string userName, CancellationToken cancellationToken = default)
    {
        var userReference = BillingReferences.ForUser(userName);
        using var budget = StartBudget(cancellationToken);

        var enrollment = await _store.GetAsync(userReference, budget.Token);
        var customer = await ReadCustomerByReferenceAsync(BillingReferences.ForCustomer(userReference), budget);
        if (customer?.Id is not int customerId)
        {
            return new MySubscriptions(Array.Empty<BillingSubscription>(), EnrollmentStatusFor(enrollment));
        }

        IReadOnlyList<SubscriptionResponse> responses;
        try
        {
            responses = await _client.Customers.ListCustomerSubscriptions(
                new ListCustomerSubscriptionsRequest { CustomerId = customerId },
                cancellationToken: budget.Token);
        }
        catch (Exception ex) when (IsSdkOrBudgetFailure(ex, budget))
        {
            throw Translate("ListCustomerSubscriptions", ex, budget);
        }

        var subscriptions = responses
            .Select(r => r?.Subscription)
            .Where(s => s?.Id != null)
            .Select(s => s!)
            .ToList();

        // Settle an enrollment whose outcome was unknown: Maxio now shows the subscription we tried to create.
        var subscriptionReference = BillingReferences.ForSubscription(userReference);
        var ours = subscriptions.FirstOrDefault(s => s.Reference == subscriptionReference);
        if (enrollment != null && ours != null && enrollment.Status != SubscriptionEnrollmentStatus.Active
            && (enrollment.Status == SubscriptionEnrollmentStatus.Unknown || enrollment.IsStale(_clock.GetUtcNow(), PendingClaimTimeout)))
        {
            await CompleteAsync(enrollment, ours, enrollment.PlanHandle);
            _logger.LogInformation("Settled unknown enrollment for {UserReference} as subscription {SubscriptionId}.", userReference, ours.Id);
        }

        return new MySubscriptions(subscriptions.Select(Map).ToList(), EnrollmentStatusFor(enrollment));
    }

    private static string? EnrollmentStatusFor(SubscriptionEnrollment? enrollment) => enrollment?.Status switch
    {
        null => null,
        SubscriptionEnrollmentStatus.Active => null,
        SubscriptionEnrollmentStatus.Pending => "pending",
        SubscriptionEnrollmentStatus.Unknown => "unconfirmed",
        _ => null
    };

    // ---------------------------------------------------------------- reads

    private async Task<Customer?> ReadCustomerByReferenceAsync(string customerReference, Budget budget)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = customerReference },
                cancellationToken: budget.Token);
            return response.Customer;
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex) when (IsSdkOrBudgetFailure(ex, budget))
        {
            throw Translate("ReadCustomerByReference", ex, budget);
        }
    }

    private async Task<Subscription?> FindSubscriptionAsync(string subscriptionReference, Budget budget)
    {
        try
        {
            var response = await _client.Subscriptions.FindSubscription(
                new FindSubscriptionRequest { Reference = subscriptionReference },
                cancellationToken: budget.Token);
            return response.Subscription?.Id != null ? response.Subscription : null;
        }
        catch (ApiException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null; // 404: no subscription carries this reference
            }
            if (ex.Error.TryGetRawError(out RawError raw))
            {
                throw FromStatus("FindSubscription", raw.StatusCode, ex);
            }
            throw FromStatus("FindSubscription", ex.StatusCode, ex);
        }
        catch (Exception ex) when (IsSdkOrBudgetFailure(ex, budget))
        {
            throw Translate("FindSubscription", ex, budget);
        }
    }

    // ---------------------------------------------------------------- error translation

    private static bool IsSdkOrBudgetFailure(Exception ex, Budget budget) =>
        ex is SdkException || (ex is OperationCanceledException && budget.Expired);

    /// <summary>One mapping from SDK failure to <see cref="SubscriptionBillingException"/>, shared by every call.</summary>
    private SubscriptionBillingException Translate(string operation, Exception ex, Budget budget)
    {
        switch (ex)
        {
            case ApiException<RawError> api:
                return FromStatus(operation, api.StatusCode, api);
            case ResponseDeserializationException rde:
                _logger.LogError(rde, "Maxio {Operation} returned an unreadable body (HTTP {Status}, expected {Type}).",
                    operation, (int)rde.StatusCode, rde.TargetType.Name);
                return (int)rde.StatusCode is >= 400 and < 500 && rde.StatusCode != HttpStatusCode.Unauthorized
                        && rde.StatusCode != HttpStatusCode.Forbidden && rde.StatusCode != HttpStatusCode.TooManyRequests
                    ? new SubscriptionBillingException(BillingFailureKind.InvalidRequest, "The billing provider rejected the request.", (int)rde.StatusCode, innerException: rde)
                    : new SubscriptionBillingException(BillingFailureKind.Unavailable, "The billing provider returned a response that could not be processed.", (int)rde.StatusCode, innerException: rde);
            case ApiException api:
                return FromStatus(operation, api.StatusCode, api);
            case SdkTimeoutException timeout:
                _logger.LogWarning("Maxio {Operation} timed out after {Timeout}.", operation, timeout.Timeout);
                return new SubscriptionBillingException(BillingFailureKind.Unavailable, "The billing provider did not respond in time.", innerException: timeout);
            case SdkConnectionException connection:
                _logger.LogWarning(connection, "Maxio {Operation} could not be reached.", operation);
                return new SubscriptionBillingException(BillingFailureKind.Unavailable, "The billing provider is unreachable.", innerException: connection);
            case AuthSchemeException auth:
                _logger.LogError(auth, "Maxio credentials could not be applied for {Operation}.", operation);
                return new SubscriptionBillingException(BillingFailureKind.Unavailable, "The billing provider is unavailable.", innerException: auth);
            case OperationCanceledException when budget.Expired:
                _logger.LogWarning("Maxio {Operation} exceeded the {Budget}s billing budget.", operation, _settings.RequestBudgetSeconds);
                return new SubscriptionBillingException(BillingFailureKind.Unavailable, "The billing provider did not respond in time.", innerException: ex);
            default:
                _logger.LogError(ex, "Unexpected failure calling Maxio {Operation}.", operation);
                return new SubscriptionBillingException(BillingFailureKind.Unavailable, "The billing provider is unavailable.", innerException: ex);
        }
    }

    private SubscriptionBillingException FromStatus(string operation, HttpStatusCode status, Exception ex)
    {
        var code = (int)status;
        if (code is 401 or 403)
        {
            // Our credentials — the caller did nothing wrong and cannot fix it.
            _logger.LogError("Maxio rejected our credentials on {Operation} (HTTP {Status}). Check {Setting}.", operation, code, $"{MaxioSettings.CONFIG_NAME}:ApiKey");
            return new SubscriptionBillingException(BillingFailureKind.Unavailable, "The billing provider is unavailable.", code, innerException: ex);
        }
        if (code == 429 || code >= 500)
        {
            _logger.LogWarning("Maxio {Operation} failed with HTTP {Status}.", operation, code);
            return new SubscriptionBillingException(BillingFailureKind.Unavailable, "The billing provider is temporarily unavailable.", code, innerException: ex);
        }
        _logger.LogWarning("Maxio {Operation} rejected the request with HTTP {Status}.", operation, code);
        return new SubscriptionBillingException(BillingFailureKind.InvalidRequest, "The billing provider rejected the request.", code, innerException: ex);
    }

    private static IEnumerable<string> CustomerErrors(CustomerErrorResponse1 body)
    {
        if (body.Errors == null)
        {
            yield break;
        }
        if (body.Errors.TryGetListOfString(out var list))
        {
            foreach (var e in list) yield return e;
        }
        else if (body.Errors.TryGetCustomerError(out var single) && !string.IsNullOrEmpty(single.Customer))
        {
            yield return single.Customer!;
        }
    }

    // ---------------------------------------------------------------- mapping & budgets

    internal static BillingSubscription Map(Subscription s) => new(
        s.Id!.Value,
        s.Reference,
        s.Product?.Handle,
        s.Product?.Name,
        s.State?.Value,
        s.ProductPriceInCents ?? s.Product?.PriceInCents,
        s.Currency,
        s.Product?.Interval,
        s.Product?.IntervalUnit?.Value,
        s.NextAssessmentAt,
        s.CurrentPeriodEndsAt,
        s.ActivatedAt,
        s.CreatedAt);

    private TimeSpan PendingClaimTimeout => TimeSpan.FromSeconds(_settings.PendingClaimTimeoutSeconds);

    private Budget StartBudget(CancellationToken callerToken) =>
        new(callerToken, TimeSpan.FromSeconds(_settings.RequestBudgetSeconds));

    /// <summary>Reconciliation runs after the main budget may be spent; it gets its own short budget and ignores caller aborts.</summary>
    private Budget StartReconcileBudget() =>
        new(CancellationToken.None, TimeSpan.FromSeconds(_settings.AttemptTimeoutSeconds * 2));

    /// <summary>The whole-operation deadline: one linked token for every SDK call an operation makes.</summary>
    private sealed class Budget : IDisposable
    {
        private readonly CancellationTokenSource _cts;
        private readonly CancellationToken _callerToken;

        public Budget(CancellationToken callerToken, TimeSpan limit)
        {
            _callerToken = callerToken;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
            _cts.CancelAfter(limit);
        }

        public CancellationToken Token => _cts.Token;

        /// <summary>True when the deadline (not the caller) cancelled the work.</summary>
        public bool Expired => _cts.IsCancellationRequested && !_callerToken.IsCancellationRequested;

        public void Dispose() => _cts.Dispose();
    }
}

/// <summary>Raised when the shopper already holds a different plan; carries the existing subscription.</summary>
public class AlreadySubscribedException : SubscriptionBillingException
{
    public AlreadySubscribedException(BillingSubscription existing)
        : base(BillingFailureKind.Conflict, $"You are already subscribed to '{existing.PlanName ?? existing.PlanHandle}'.")
    {
        Existing = existing;
    }

    public BillingSubscription Existing { get; }
}
