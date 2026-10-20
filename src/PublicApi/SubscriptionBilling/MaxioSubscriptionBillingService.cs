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
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionBilling;

/// <summary>
/// The only class that talks to Maxio. It owns the per-request call budget, the duplicate-submission
/// claims, the settlement of writes whose outcome is unknown, and the translation of every SDK failure
/// into <see cref="SubscriptionBillingException"/>.
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    public const int PLAN_PAGE_SIZE = 200;
    public const int MAX_PLAN_PAGES = 5;
    private static readonly TimeSpan ReconcileBudget = TimeSpan.FromSeconds(10);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly IBillingClaimStore _claims;
    private readonly MaxioSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly TimeProvider _time;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;
    private readonly TimeSpan _pendingLease;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        IBillingClaimStore claims,
        IOptions<MaxioSettings> settings,
        IMemoryCache cache,
        TimeProvider time,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _claims = claims;
        _settings = settings.Value;
        _cache = cache;
        _time = time;
        _logger = logger;
        // A Pending claim older than this can no longer belong to a live request, so it may be taken over.
        _pendingLease = _settings.RequestBudget + _settings.RequestBudget;
    }

    private string PlanCacheKey => $"maxio-plans:{_settings.ProductFamilyHandle}";

    public async Task<SubscriptionPlanCatalog> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        using var budget = new CallBudget(_settings.RequestBudget, cancellationToken);
        return await GuardAsync(budget, "list subscription plans", () => GetPlansCoreAsync(budget));
    }

    public async Task<SubscribeResult> SubscribeAsync(BillingUser user, string productHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
            throw new SubscriptionBillingException(HttpStatusCode.BadRequest, "productHandle is required.");

        var handle = productHandle.Trim();
        using var budget = new CallBudget(_settings.RequestBudget, cancellationToken);
        return await GuardAsync(budget, "subscribe", async () =>
        {
            // Invariant: the handle must be one this application offers (the configured family, not archived).
            var catalog = await GetPlansCoreAsync(budget);
            var plan = catalog.Plans.FirstOrDefault(p => string.Equals(p.Handle, handle, StringComparison.Ordinal));
            if (plan is null)
            {
                if (catalog.Truncated)
                    throw new SubscriptionBillingException(HttpStatusCode.ServiceUnavailable,
                        "The subscription plan catalog could not be read completely; try again later.");
                throw new SubscriptionBillingException(HttpStatusCode.BadRequest,
                    $"'{handle}' is not an available subscription plan.");
            }

            var customerReference = BillingReferences.Customer(user.UserName);
            var subscriptionReference = BillingReferences.Subscription(customerReference, plan.Handle);

            var customerId = await EnsureCustomerAsync(user, customerReference, budget);
            if (customerId is null)
                return new SubscribeResult(SubscribeOutcome.Pending, subscriptionReference, null);

            return await EnsureSubscriptionAsync(customerId.Value, plan.Handle, subscriptionReference, budget);
        });
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> GetSubscriptionsAsync(BillingUser user, CancellationToken cancellationToken = default)
    {
        using var budget = new CallBudget(_settings.RequestBudget, cancellationToken);
        return await GuardAsync<IReadOnlyList<SubscriptionDetails>>(budget, "list my subscriptions", async () =>
        {
            var customerReference = BillingReferences.Customer(user.UserName);

            // Invariant: only ever list subscriptions of the customer resolved from the caller's own reference.
            var claim = await _claims.FindAsync(BillingReferences.CustomerClaimKey(customerReference), budget.Token);
            int? customerId = claim is { Status: BillingClaimStatus.Completed } ? claim.ProviderId : null;
            if (customerId is null)
            {
                var customer = await FindCustomerByReferenceAsync(customerReference, budget.Token);
                if (customer is null)
                    return Array.Empty<SubscriptionDetails>();
                customerId = customer.Id ?? throw SubscriptionBillingException.Unavailable(
                    "The billing provider returned a customer without an id.");
            }

            IReadOnlyList<SubscriptionResponse> subscriptions;
            try
            {
                subscriptions = await _client.Customers.ListCustomerSubscriptions(
                    new ListCustomerSubscriptionsRequest { CustomerId = customerId.Value },
                    cancellationToken: budget.Token);
            }
            catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogWarning("Maxio customer {CustomerId} no longer exists; reporting no subscriptions.", customerId);
                return Array.Empty<SubscriptionDetails>();
            }
            catch (ApiException<RawError> ex)
            {
                throw ProviderRefused(ex.StatusCode, "list subscriptions", ex);
            }

            return (subscriptions ?? Array.Empty<SubscriptionResponse>())
                .Select(r => r?.Subscription)
                .Where(s => s?.Id is not null)
                .Select(s => Map(s!))
                .OrderByDescending(s => s.CreatedAt)
                .ToList();
        });
    }

    // ---------------------------------------------------------------- plans

    private async Task<SubscriptionPlanCatalog> GetPlansCoreAsync(CallBudget budget)
    {
        if (_cache.TryGetValue(PlanCacheKey, out SubscriptionPlanCatalog? cached) && cached is not null)
            return cached;

        var catalog = await LoadPlansAsync(budget.Token);
        if (!catalog.Truncated)
            _cache.Set(PlanCacheKey, catalog, _settings.PlanCacheDuration);
        return catalog;
    }

    private async Task<SubscriptionPlanCatalog> LoadPlansAsync(CancellationToken token)
    {
        var plans = new List<SubscriptionPlan>();
        var truncated = false;

        for (var page = 1; ; page++)
        {
            if (page > MAX_PLAN_PAGES)
            {
                truncated = true;
                _logger.LogWarning("Plan list for product family {ProductFamily} stopped at the {MaxPages}-page cap.",
                    _settings.ProductFamilyHandle, MAX_PLAN_PAGES);
                break;
            }

            IReadOnlyList<ProductResponse> batch;
            try
            {
                batch = await _client.ProductFamilies.ListProductsForProductFamily(
                    new ListProductsForProductFamilyRequest
                    {
                        ProductFamilyId = "handle:" + _settings.ProductFamilyHandle,
                        Page = page,
                        PerPage = PLAN_PAGE_SIZE
                    },
                    cancellationToken: token);
            }
            catch (ApiException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out var notFound))
                {
                    _logger.LogError("Maxio product family {ProductFamily} was not found ({Status}): {Detail}",
                        _settings.ProductFamilyHandle, (int)ex.StatusCode, notFound);
                    throw SubscriptionBillingException.Unavailable("Subscription plans are not available.", ex.StatusCode, ex);
                }
                // TryGetRawError (any other status): no typed detail beyond the status.
                throw ProviderRefused(ex.StatusCode, "list plans", ex);
            }

            batch ??= Array.Empty<ProductResponse>();
            foreach (var product in batch.Select(b => b?.Product))
            {
                if (product is null || product.ArchivedAt is not null ||
                    string.IsNullOrWhiteSpace(product.Handle) || product.PriceInCents is null)
                    continue;

                plans.Add(new SubscriptionPlan(
                    product.Handle,
                    product.Name ?? product.Handle,
                    product.Description,
                    product.PriceInCents.Value,
                    product.Interval,
                    product.IntervalUnit?.Value,
                    product.RequireCreditCard));
            }

            if (batch.Count < PLAN_PAGE_SIZE)
                break;
        }

        return new SubscriptionPlanCatalog(plans.OrderBy(p => p.PriceInCents).ToList(), truncated);
    }

    // ---------------------------------------------------------------- customer

    /// <summary>Returns the Maxio customer id, or null when creation is in an unknown state (settled later).</summary>
    private async Task<int?> EnsureCustomerAsync(BillingUser user, string reference, CallBudget budget)
    {
        var acquired = await AcquireClaimAsync(BillingReferences.CustomerClaimKey(reference), BillingClaimKind.Customer, budget.Token);
        if (acquired.CompletedProviderId is int knownId)
            return knownId;
        var claim = acquired.Claim!;

        // State may have been lost (restart) while Maxio kept the customer: the reference is unique there.
        var existing = await FindCustomerByReferenceAsync(reference, budget.Token);
        if (existing?.Id is int existingId)
        {
            await _claims.CompleteAsync(claim, existingId, Now, budget.Token);
            return existingId;
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
                            FirstName = FirstNameOf(user),
                            LastName = "eShopOnWeb",
                            Email = user.Email,
                            Reference = reference
                        }
                    }
                },
                cancellationToken: budget.Token);

            if (response?.Customer?.Id is int createdId)
            {
                await _claims.CompleteAsync(claim, createdId, Now, budget.Token);
                _logger.LogInformation("Created Maxio customer {CustomerId} for reference {Reference}.", createdId, reference);
                return createdId;
            }
            return await SettleCustomerAsync(claim, reference, "the response carried no customer id");
        }
        catch (ApiException<CreateCustomerError> ex) when ((int)ex.StatusCode >= 500)
        {
            return await SettleCustomerAsync(claim, reference, $"provider answered {(int)ex.StatusCode}");
        }
        catch (ApiException<CreateCustomerError> ex)
        {
            // A concurrent creator (another instance, or state lost mid-flight) may have taken the reference.
            var raced = await FindCustomerByReferenceAsync(reference, budget.Token);
            if (raced?.Id is int racedId)
            {
                await _claims.CompleteAsync(claim, racedId, Now, budget.Token);
                return racedId;
            }

            await _claims.ReleaseAsync(claim, budget.Token);
            IReadOnlyList<string> errors = Array.Empty<string>();
            if (ex.Error.TryGetCustomerErrorResponse1(out var body))
                errors = ReadCustomerErrors(body);

            if (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
                throw new SubscriptionBillingException(HttpStatusCode.UnprocessableEntity,
                    "The billing provider rejected the customer record.", ex.StatusCode, errors, ex);
            throw ProviderRefused(ex.StatusCode, "create customer", ex);
        }
        catch (ResponseDeserializationException ex) when (IsSuccessOrServerError(ex.StatusCode))
        {
            return await SettleCustomerAsync(claim, reference, $"unreadable {(int)ex.StatusCode} response");
        }
        catch (ResponseDeserializationException ex)
        {
            await _claims.ReleaseAsync(claim, budget.Token);
            throw ProviderRefused(ex.StatusCode, "create customer", ex);
        }
        catch (SdkConnectionException ex)
        {
            return await SettleCustomerAsync(claim, reference, ex is SdkTimeoutException ? "timeout" : "connection failure");
        }
        catch (OperationCanceledException) when (budget.Expired)
        {
            return await SettleCustomerAsync(claim, reference, "request budget exhausted");
        }
        catch (AuthSchemeException)
        {
            await _claims.ReleaseAsync(claim, budget.Token);
            throw;
        }
    }

    /// <summary>The CreateCustomer outcome is unknown: re-read by the reference we sent.</summary>
    private async Task<int?> SettleCustomerAsync(BillingClaim claim, string reference, string reason)
    {
        _logger.LogWarning("Maxio customer creation for reference {Reference} has an unknown outcome ({Reason}); re-reading.", reference, reason);
        using var settle = new CancellationTokenSource(ReconcileBudget);
        try
        {
            var found = await FindCustomerByReferenceAsync(reference, settle.Token);
            if (found?.Id is int id)
            {
                await _claims.CompleteAsync(claim, id, Now, settle.Token);
                return id;
            }
        }
        catch (Exception ex) when (ex is SdkException or SubscriptionBillingException or OperationCanceledException)
        {
            _logger.LogWarning(ex, "Re-read of Maxio customer {Reference} failed; leaving the claim Unknown.", reference);
        }

        await _claims.MarkUnknownAsync(claim, Now, CancellationToken.None);
        return null;
    }

    /// <summary>Looks a customer up by reference. Null means Maxio answered 404 - nothing else is read as absence.</summary>
    private async Task<Customer?> FindCustomerByReferenceAsync(string reference, CancellationToken token)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = reference },
                cancellationToken: token);
            return response?.Customer;
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (ApiException<RawError> ex)
        {
            throw ProviderRefused(ex.StatusCode, "look up customer", ex);
        }
    }

    // ---------------------------------------------------------------- subscription

    private async Task<SubscribeResult> EnsureSubscriptionAsync(int customerId, string productHandle, string reference, CallBudget budget)
    {
        var acquired = await AcquireClaimAsync(BillingReferences.SubscriptionClaimKey(reference), BillingClaimKind.Subscription, budget.Token);
        if (acquired.CompletedProviderId is not null)
        {
            var current = await FindSubscriptionByReferenceAsync(reference, budget.Token);
            return new SubscribeResult(SubscribeOutcome.AlreadySubscribed, reference, current is null ? null : Map(current));
        }
        var claim = acquired.Claim!;

        // Covers local state lost on restart while Maxio kept the subscription.
        var existing = await FindSubscriptionByReferenceAsync(reference, budget.Token);
        if (existing?.Id is int existingId)
        {
            await _claims.CompleteAsync(claim, existingId, Now, budget.Token);
            return new SubscribeResult(SubscribeOutcome.AlreadySubscribed, reference, Map(existing));
        }

        try
        {
            // Invariant: CustomerId is the id this application resolved for the caller's own reference.
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
                            PaymentCollectionMethod = CollectionMethodSetting
                        }
                    }
                },
                cancellationToken: budget.Token);

            if (response?.Subscription is { Id: int createdId } created)
            {
                await _claims.CompleteAsync(claim, createdId, Now, budget.Token);
                _logger.LogInformation("Created Maxio subscription {SubscriptionId} ({Reference}) on {ProductHandle}.",
                    createdId, reference, productHandle);
                return new SubscribeResult(SubscribeOutcome.Created, reference, Map(created));
            }
            return await SettleSubscriptionAsync(claim, reference, "the response carried no subscription id");
        }
        catch (ApiException<CreateSubscriptionError> ex) when ((int)ex.StatusCode >= 500)
        {
            return await SettleSubscriptionAsync(claim, reference, $"provider answered {(int)ex.StatusCode}");
        }
        catch (ApiException<CreateSubscriptionError> ex)
        {
            await _claims.ReleaseAsync(claim, budget.Token);
            IReadOnlyList<string> errors = Array.Empty<string>();
            if (ex.Error.TryGetErrorListResponse1(out var body))
                errors = body.Errors ?? Array.Empty<string>();

            _logger.LogWarning("Maxio refused subscription {Reference} with {Status}: {Errors}",
                reference, (int)ex.StatusCode, string.Join("; ", errors));
            if (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
                throw new SubscriptionBillingException(HttpStatusCode.UnprocessableEntity,
                    "The billing provider rejected the subscription.", ex.StatusCode, errors, ex);
            throw ProviderRefused(ex.StatusCode, "create subscription", ex);
        }
        catch (ResponseDeserializationException ex) when (IsSuccessOrServerError(ex.StatusCode))
        {
            return await SettleSubscriptionAsync(claim, reference, $"unreadable {(int)ex.StatusCode} response");
        }
        catch (ResponseDeserializationException ex)
        {
            // Rejected, but the error body did not match the declared shape: keep the status.
            await _claims.ReleaseAsync(claim, budget.Token);
            throw ProviderRefused(ex.StatusCode, "create subscription", ex);
        }
        catch (SdkConnectionException ex)
        {
            return await SettleSubscriptionAsync(claim, reference, ex is SdkTimeoutException ? "timeout" : "connection failure");
        }
        catch (OperationCanceledException) when (budget.Expired)
        {
            return await SettleSubscriptionAsync(claim, reference, "request budget exhausted");
        }
        catch (AuthSchemeException)
        {
            await _claims.ReleaseAsync(claim, budget.Token);
            throw;
        }
    }

    /// <summary>The CreateSubscription outcome is unknown: re-read by the reference we sent.</summary>
    private async Task<SubscribeResult> SettleSubscriptionAsync(BillingClaim claim, string reference, string reason)
    {
        _logger.LogWarning("Maxio subscription {Reference} has an unknown outcome ({Reason}); re-reading.", reference, reason);
        using var settle = new CancellationTokenSource(ReconcileBudget);
        try
        {
            var found = await FindSubscriptionByReferenceAsync(reference, settle.Token);
            if (found?.Id is int id)
            {
                await _claims.CompleteAsync(claim, id, Now, settle.Token);
                return new SubscribeResult(SubscribeOutcome.Created, reference, Map(found));
            }
        }
        catch (Exception ex) when (ex is SdkException or SubscriptionBillingException or OperationCanceledException)
        {
            _logger.LogWarning(ex, "Re-read of Maxio subscription {Reference} failed; leaving the claim Unknown.", reference);
        }

        await _claims.MarkUnknownAsync(claim, Now, CancellationToken.None);
        return new SubscribeResult(SubscribeOutcome.Pending, reference, null);
    }

    /// <summary>Null means Maxio answered "no match" (404); nothing else is read as absence.</summary>
    private async Task<Subscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken token)
    {
        try
        {
            var response = await _client.Subscriptions.FindSubscription(
                new FindSubscriptionRequest { Reference = reference },
                cancellationToken: token);
            var subscription = response?.Subscription;
            // Only adopt a subscription that carries the reference we asked for.
            return subscription is not null && subscription.Reference == reference ? subscription : null;
        }
        catch (ApiException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
                return null;
            // TryGetRawError (any other status): no typed detail beyond the status.
            throw ProviderRefused(ex.StatusCode, "look up subscription", ex);
        }
    }

    // ---------------------------------------------------------------- claims

    private readonly record struct ClaimAcquisition(BillingClaim? Claim, int? CompletedProviderId);

    /// <summary>
    /// Claim -> provider call -> record. The second caller for the same key is refused by the store
    /// (primary key on insert, concurrency token on take-over) and gets a 409 while the first is in flight.
    /// </summary>
    private async Task<ClaimAcquisition> AcquireClaimAsync(string key, BillingClaimKind kind, CancellationToken token)
    {
        var claim = await _claims.FindAsync(key, token);
        if (claim is null)
        {
            var fresh = new BillingClaim(key, kind, Now);
            if (await _claims.TryClaimAsync(fresh, token))
                return new ClaimAcquisition(fresh, null);

            _logger.LogInformation("Claim {ClaimKey} is already held by another request.", key);
            claim = await _claims.FindAsync(key, token);
            if (claim is null)
                throw InProgress();
        }

        if (claim.Status == BillingClaimStatus.Completed && claim.ProviderId is int providerId)
            return new ClaimAcquisition(null, providerId);

        if (!claim.IsStale(Now, _pendingLease))
            throw InProgress();

        if (!await _claims.TryTakeOverAsync(claim, Now, token))
        {
            _logger.LogInformation("Claim {ClaimKey} was taken over by another request.", key);
            throw InProgress();
        }

        _logger.LogInformation("Took over stale claim {ClaimKey}; settling against the provider.", key);
        return new ClaimAcquisition(claim, null);
    }

    private static SubscriptionBillingException InProgress() =>
        new(HttpStatusCode.Conflict, "An identical subscription request is already in progress. Retry shortly.");

    // ---------------------------------------------------------------- error boundary

    /// <summary>The one place SDK failures not handled per operation become the caller-facing error type.</summary>
    private async Task<T> GuardAsync<T>(CallBudget budget, string operation, Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (ResponseDeserializationException ex)
        {
            _logger.LogError(ex, "Maxio returned an unreadable response during {Operation} (HTTP {Status}).", operation, (int)ex.StatusCode);
            throw SubscriptionBillingException.Unavailable("The billing provider returned a response that could not be processed.", ex.StatusCode, ex);
        }
        catch (ApiException ex)
        {
            throw ProviderRefused(ex.StatusCode, operation, ex);
        }
        catch (SdkTimeoutException ex)
        {
            _logger.LogWarning(ex, "Maxio did not answer within {Timeout} during {Operation}.", ex.Timeout, operation);
            throw new SubscriptionBillingException(HttpStatusCode.GatewayTimeout, "The billing provider did not answer in time.", null, null, ex);
        }
        catch (SdkConnectionException ex)
        {
            _logger.LogWarning(ex, "Maxio was unreachable during {Operation}.", operation);
            throw SubscriptionBillingException.Unavailable("The billing provider is unreachable.", null, ex);
        }
        catch (AuthSchemeException ex)
        {
            _logger.LogError(ex, "Maxio credentials could not be applied during {Operation}.", operation);
            throw SubscriptionBillingException.Unavailable("The billing provider is unavailable.", null, ex);
        }
        catch (OperationCanceledException ex) when (budget.Expired)
        {
            _logger.LogWarning("Maxio calls for {Operation} exceeded the {Budget} request budget.", operation, _settings.RequestBudget);
            throw new SubscriptionBillingException(HttpStatusCode.GatewayTimeout, "The billing provider did not answer in time.", null, null, ex);
        }
    }

    private SubscriptionBillingException ProviderRefused(HttpStatusCode providerStatus, string operation, Exception inner)
    {
        var status = (int)providerStatus;
        if (status is 401 or 403)
        {
            // Our credentials, not the caller's fault.
            _logger.LogError("Maxio rejected our credentials ({Status}) during {Operation}.", status, operation);
            return SubscriptionBillingException.Unavailable("The billing provider is unavailable.", providerStatus, inner);
        }
        if (status == 429)
        {
            _logger.LogWarning("Maxio rate-limited {Operation}.", operation);
            return new SubscriptionBillingException(HttpStatusCode.ServiceUnavailable, "The billing provider is busy; try again shortly.", providerStatus, null, inner);
        }
        if (status is >= 400 and < 500)
        {
            _logger.LogWarning("Maxio refused {Operation} with {Status}.", operation, status);
            return new SubscriptionBillingException(HttpStatusCode.UnprocessableEntity, $"The billing provider refused to {operation}.", providerStatus, null, inner);
        }

        _logger.LogError("Maxio failed {Operation} with {Status}.", operation, status);
        return SubscriptionBillingException.Unavailable("The billing provider is unavailable.", providerStatus, inner);
    }

    // ---------------------------------------------------------------- helpers

    private DateTimeOffset Now => _time.GetUtcNow();

    // Validated at start-up (MaxioSettingsValidator), so the lookup cannot fail here.
    private MaxioAdvancedBilling.Models.Enums.CollectionMethod CollectionMethodSetting =>
        MaxioAdvancedBilling.Models.Enums.CollectionMethod.TryGetKnownValue(_settings.PaymentCollectionMethod, out var method)
            ? method
            : MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance;

    private static bool IsSuccessOrServerError(HttpStatusCode status) =>
        (int)status is >= 200 and < 300 or >= 500;

    private static string FirstNameOf(BillingUser user)
    {
        var source = string.IsNullOrWhiteSpace(user.Email) ? user.UserName : user.Email;
        var at = source.IndexOf('@');
        var local = at > 0 ? source[..at] : source;
        return string.IsNullOrWhiteSpace(local) ? "Shopper" : local;
    }

    private static IReadOnlyList<string> ReadCustomerErrors(CustomerErrorResponse1 body)
    {
        if (body.Errors is null)
            return Array.Empty<string>();
        if (body.Errors.TryGetListOfString(out var list))
            return list;
        if (body.Errors.TryGetCustomerError(out var customerError) && !string.IsNullOrWhiteSpace(customerError.Customer))
            return new[] { customerError.Customer };
        return Array.Empty<string>();
    }

    internal static SubscriptionDetails Map(Subscription subscription) => new(
        subscription.Id ?? 0,
        subscription.Reference,
        subscription.Product?.Handle,
        subscription.Product?.Name,
        subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents,
        subscription.Currency,
        subscription.Product?.Interval,
        subscription.Product?.IntervalUnit?.Value,
        subscription.State?.Value,
        // "When the next regularly scheduled attempted charge will occur."
        subscription.CurrentPeriodEndsAt,
        subscription.NextAssessmentAt,
        subscription.CreatedAt,
        subscription.ActivatedAt);

    /// <summary>One total budget for every Maxio call made while serving a request, linked to the caller's abort.</summary>
    private sealed class CallBudget : IDisposable
    {
        private readonly CancellationTokenSource _cts;
        private readonly CancellationToken _caller;

        public CallBudget(TimeSpan budget, CancellationToken caller)
        {
            _caller = caller;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(caller);
            _cts.CancelAfter(budget);
        }

        public CancellationToken Token => _cts.Token;

        /// <summary>True when our budget ran out (as opposed to the caller going away).</summary>
        public bool Expired => _cts.IsCancellationRequested && !_caller.IsCancellationRequested;

        public void Dispose() => _cts.Dispose();
    }
}
