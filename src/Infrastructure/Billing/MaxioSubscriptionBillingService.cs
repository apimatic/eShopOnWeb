using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.ExceptionServices;
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
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// <see cref="ISubscriptionBillingService"/> backed by Maxio Advanced Billing, the billing system of record.
/// </summary>
/// <remarks>
/// Every provider write follows <b>claim → SDK call → record the result</b>: a row keyed by the write's
/// deterministic reference is inserted into <see cref="CatalogContext"/> first, so a second request for the
/// same user (or user + plan) is refused by the store's primary key before it can reach Maxio. A write whose
/// connection fails after it may have landed is settled by re-reading Maxio by that same reference.
/// </remarks>
public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    /// <summary>Total budget for one service call (all Maxio calls it makes), on top of the caller's own cancellation.</summary>
    internal static readonly TimeSpan TotalCallBudget = TimeSpan.FromSeconds(30);

    /// <summary>Budget for re-reading Maxio after a write whose outcome is unknown.</summary>
    internal static readonly TimeSpan SettlementBudget = TimeSpan.FromSeconds(10);

    /// <summary>A pending claim older than this is treated as abandoned (its request crashed) and may be taken over.</summary>
    internal static readonly TimeSpan StaleClaimAfter = TimeSpan.FromMinutes(2);

    internal const int PlanPageSize = 200;   // the documented maximum per_page
    internal const int MaxPlanPages = 5;
    private static readonly TimeSpan PlanCacheDuration = TimeSpan.FromMinutes(2);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly CatalogContext _db;
    private readonly MaxioSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;
    private readonly TimeProvider _clock;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        CatalogContext db,
        IOptions<MaxioSettings> settings,
        IMemoryCache cache,
        ILogger<MaxioSubscriptionBillingService> logger,
        TimeProvider? clock = null)
    {
        _client = client;
        _db = db;
        _settings = settings.Value;
        _cache = cache;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    private string ProductFamilyHandle => _settings.ProductFamilyHandle!.Trim();

    // ---------------------------------------------------------------- plans

    public async Task<SubscriptionPlanCatalog> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        using var budget = new CallBudget(cancellationToken);
        return await GetPlanCatalogAsync(budget);
    }

    private async Task<SubscriptionPlanCatalog> GetPlanCatalogAsync(CallBudget budget)
    {
        var cacheKey = $"maxio:plans:{ProductFamilyHandle}";
        if (_cache.TryGetValue(cacheKey, out SubscriptionPlanCatalog? cached) && cached is not null)
        {
            return cached;
        }

        var plans = new List<SubscriptionPlan>();
        var truncated = false;
        for (var page = 1; ; page++)
        {
            if (page > MaxPlanPages)
            {
                // Every page so far was full, so there may be more products than we are willing to walk.
                truncated = true;
                _logger.LogWarning("Maxio product family {ProductFamily} has more than {MaxProducts} products; the plan list is truncated.",
                    ProductFamilyHandle, MaxPlanPages * PlanPageSize);
                break;
            }

            var batch = await ReadAsync("list plans", budget, token => ListProductsPageAsync(page, token));
            foreach (var item in batch)
            {
                var plan = MapPlan(item.Product);
                if (plan is not null)
                {
                    plans.Add(plan);
                }
            }

            if (batch.Count < PlanPageSize)
            {
                break;
            }
        }

        var catalog = new SubscriptionPlanCatalog(plans, truncated);
        _cache.Set(cacheKey, catalog, PlanCacheDuration);
        return catalog;
    }

    private async Task<IReadOnlyList<ProductResponse>> ListProductsPageAsync(int page, CancellationToken token)
    {
        try
        {
            return await _client.ProductFamilies.ListProductsForProductFamily(
                new ListProductsForProductFamilyRequest
                {
                    ProductFamilyId = "handle:" + ProductFamilyHandle,
                    Page = page,
                    PerPage = PlanPageSize
                },
                cancellationToken: token);
        }
        catch (ApiException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out _))
            {
                // 404: the configured family does not exist on this site — our configuration, not the caller's request.
                _logger.LogError("Maxio product family {ProductFamily} was not found (HTTP {Status}); check Maxio:ProductFamilyHandle.",
                    ProductFamilyHandle, (int)ex.StatusCode);
                throw new SubscriptionBillingException(SubscriptionBillingFailure.ProviderUnavailable,
                    "Subscription plans are not available right now.", innerException: ex);
            }

            // Any other status (TryGetRawError): not the caller's fault.
            throw ProviderFailure(ex, "list plans");
        }
    }

    private static SubscriptionPlan? MapPlan(Product? product)
    {
        if (product is null || string.IsNullOrWhiteSpace(product.Handle) || product.ArchivedAt is not null)
        {
            return null;
        }

        return new SubscriptionPlan(
            product.Handle,
            string.IsNullOrWhiteSpace(product.Name) ? product.Handle : product.Name,
            product.Description,
            product.PriceInCents,
            product.Interval,
            product.IntervalUnit?.Value);
    }

    // ---------------------------------------------------------------- subscribe

    public async Task<SubscribeResult> SubscribeAsync(string customerReference, string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(customerReference))
        {
            throw new SubscriptionBillingException(SubscriptionBillingFailure.InvalidRequest, "The caller has no identity to subscribe.");
        }

        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new SubscriptionBillingException(SubscriptionBillingFailure.InvalidRequest, "A plan handle is required.");
        }

        customerReference = customerReference.Trim();
        planHandle = planHandle.Trim();

        using var budget = new CallBudget(cancellationToken);

        // Invariant: the plan must be one this application offers (a product of the configured family).
        var catalog = await GetPlanCatalogAsync(budget);
        var plan = catalog.Plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.Ordinal));
        if (plan is null)
        {
            if (catalog.IsTruncated)
            {
                throw new SubscriptionBillingException(SubscriptionBillingFailure.ProviderUnavailable,
                    "The plan could not be verified because the plan list is incomplete.");
            }

            throw new SubscriptionBillingException(SubscriptionBillingFailure.InvalidRequest,
                $"Unknown plan '{planHandle}'.", catalog.Plans.Select(p => $"Available plan: {p.Handle}").ToList());
        }

        var customerId = await EnsureCustomerAsync(customerReference, budget);

        var reference = SubscriptionEnrollment.BuildReference(customerReference, plan.Handle);
        var (enrollment, outcome) = await AcquireClaimAsync(
            new SubscriptionEnrollment(reference, customerReference, plan.Handle, Now), reference, budget);

        if (outcome == ClaimOutcome.AlreadyCompleted)
        {
            var existing = await ReadAsync("read subscription", budget, token =>
                _client.Subscriptions.ReadSubscription(new ReadSubscriptionRequest { SubscriptionId = enrollment.ProviderId!.Value },
                    cancellationToken: token));
            return new SubscribeResult(MapSubscription(existing.Subscription, "read subscription"), Created: false);
        }

        // We own the claim. Maxio may already hold this subscription (e.g. our claim rows were lost on restart,
        // or an earlier attempt's outcome was unknown) — look it up by reference before creating anything.
        Subscription? found;
        try
        {
            found = await ReadAsync("find subscription", budget, token => FindSubscriptionByReferenceAsync(reference, token));
        }
        catch (Exception)
        {
            // Nothing was written to Maxio yet.
            await ReleaseClaimAsync(enrollment, outcome);
            throw;
        }

        if (found?.Id is int foundId)
        {
            await RecordCompletedAsync(enrollment, foundId);
            _logger.LogInformation("Maxio subscription {SubscriptionId} for plan {Plan} already existed; claim recorded.", foundId, plan.Handle);
            return new SubscribeResult(MapSubscription(found, "find subscription"), Created: false);
        }

        SubscriptionResponse response;
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
                            Reference = reference,
                            // This flow captures no payment method, so collect by invoice (remittance) instead of
                            // attempting an automatic charge at signup, which Maxio refuses with no card on file.
                            PaymentCollectionMethod = CollectionMethod.Remittance
                        }
                    }
                },
                cancellationToken: budget.Token);
        }
        catch (ApiException<CreateSubscriptionError> ex) when (IsDefiniteRejection(ex.StatusCode))
        {
            var details = new List<string>();
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                details.AddRange(errorList.Errors ?? Array.Empty<string>());
            }

            // Otherwise (TryGetRawError) there is no typed body; the status alone decides the mapping.
            await DeleteClaimAsync(enrollment);
            throw Rejection(ex, "create subscription", details);
        }
        catch (Exception ex) when (IsAmbiguousWriteFailure(ex))
        {
            return await SettleSubscriptionAsync(enrollment, customerId, ex);
        }
        catch (SdkException ex)
        {
            // Not sent, or definitively refused without a typed body (e.g. a credential that could not be applied).
            await DeleteClaimAsync(enrollment);
            throw ProviderFailure(ex, "create subscription");
        }

        if (response.Subscription?.Id is not int subscriptionId)
        {
            return await SettleSubscriptionAsync(enrollment, customerId,
                new InvalidOperationException("Maxio accepted the subscription but returned no subscription id."));
        }

        await RecordCompletedAsync(enrollment, subscriptionId);
        _logger.LogInformation("Created Maxio subscription {SubscriptionId} (customer {CustomerId}, plan {Plan}, state {State}).",
            subscriptionId, customerId, plan.Handle, response.Subscription.State?.Value);
        return new SubscribeResult(MapSubscription(response.Subscription, "create subscription"), Created: true);
    }

    /// <summary>
    /// Settles a <c>CreateSubscription</c> whose outcome is unknown by re-reading Maxio by the reference it carried.
    /// </summary>
    private async Task<SubscribeResult> SettleSubscriptionAsync(SubscriptionEnrollment enrollment, int customerId, Exception cause)
    {
        _logger.LogWarning(cause, "Outcome of Maxio CreateSubscription (customer {CustomerId}, plan {Plan}) is unknown; settling by reference.",
            customerId, enrollment.PlanHandle);

        using var settle = new CancellationTokenSource(SettlementBudget);
        Subscription? found = null;
        try
        {
            found = await FindSubscriptionByReferenceAsync(enrollment.SubscriptionReference, settle.Token);
        }
        catch (Exception ex) when (ex is SdkException or OperationCanceledException or SubscriptionBillingException)
        {
            _logger.LogWarning(ex, "FindSubscription failed while settling; falling back to the customer's subscription list.");
        }

        if (found?.Id is null)
        {
            try
            {
                var all = await _client.Customers.ListCustomerSubscriptions(
                    new ListCustomerSubscriptionsRequest { CustomerId = customerId }, cancellationToken: settle.Token);
                found = all.Select(r => r.Subscription)
                    .FirstOrDefault(s => s?.Id is not null && string.Equals(s.Reference, enrollment.SubscriptionReference, StringComparison.Ordinal));
            }
            catch (Exception ex) when (ex is SdkException or OperationCanceledException)
            {
                _logger.LogWarning(ex, "ListCustomerSubscriptions failed while settling.");
            }
        }

        if (found?.Id is int id)
        {
            await RecordCompletedAsync(enrollment, id);
            _logger.LogInformation("Settled unknown CreateSubscription outcome: Maxio subscription {SubscriptionId} exists.", id);
            return new SubscribeResult(MapSubscription(found, "settle subscription"), Created: true);
        }

        await RecordUnknownAsync(enrollment);
        _logger.LogError("Could not confirm whether Maxio created the subscription (customer {CustomerId}, plan {Plan}); left for the next request to settle.",
            customerId, enrollment.PlanHandle);
        throw new SubscriptionBillingException(SubscriptionBillingFailure.OutcomeUnknown,
            "The subscription request reached the billing system but its outcome could not be confirmed. Retry the request; it will not create a duplicate subscription.",
            innerException: cause);
    }

    private async Task<Subscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken token)
    {
        try
        {
            var response = await _client.Subscriptions.FindSubscription(new FindSubscriptionRequest { Reference = reference },
                cancellationToken: token);
            return response.Subscription;
        }
        catch (ApiException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out RawError _))
            {
                return null; // 404: no subscription carries this reference
            }

            // Any other status (TryGetRawError): not the caller's fault.
            throw ProviderFailure(ex, "find subscription");
        }
    }

    // ---------------------------------------------------------------- customer

    /// <summary>
    /// Returns the Maxio customer id for <paramref name="customerReference"/>, creating the customer at most once.
    /// </summary>
    private async Task<int> EnsureCustomerAsync(string customerReference, CallBudget budget)
    {
        var (link, outcome) = await AcquireClaimAsync(new BillingCustomer(customerReference, Now), customerReference, budget);
        if (outcome == ClaimOutcome.AlreadyCompleted)
        {
            return link.ProviderId!.Value;
        }

        // We own the claim. Look the customer up first: Maxio is the system of record and may already hold it.
        Customer? existing;
        try
        {
            existing = await ReadAsync("find customer", budget, token => FindCustomerByReferenceAsync(customerReference, token));
        }
        catch (Exception)
        {
            // Nothing was written to Maxio yet.
            await ReleaseClaimAsync(link, outcome);
            throw;
        }

        if (existing?.Id is int existingId)
        {
            await RecordCompletedAsync(link, existingId);
            return existingId;
        }

        var (firstName, lastName, email) = DeriveCustomerIdentity(customerReference);
        CustomerResponse created;
        try
        {
            created = await _client.Customers.CreateCustomer(
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
        }
        catch (ApiException<CreateCustomerError> ex) when (IsDefiniteRejection(ex.StatusCode))
        {
            var details = new List<string>();
            if (ex.Error.TryGetCustomerErrorResponse1(out var errorBody) && errorBody.Errors is not null)
            {
                if (errorBody.Errors.TryGetCustomerError(out var customerError) && !string.IsNullOrWhiteSpace(customerError.Customer))
                {
                    details.Add(customerError.Customer);
                }
                else if (errorBody.Errors.TryGetListOfString(out var messages))
                {
                    details.AddRange(messages);
                }
            }
            // Otherwise (TryGetRawError) there is no typed body; the status alone decides the mapping.

            // Maxio refuses a second customer with the same reference. If that is why, adopt the existing customer.
            var adopted = await TryFindCustomerForSettlementAsync(customerReference);
            if (adopted?.Id is int adoptedId)
            {
                await RecordCompletedAsync(link, adoptedId);
                return adoptedId;
            }

            await DeleteClaimAsync(link);
            throw Rejection(ex, "create customer", details);
        }
        catch (Exception ex) when (IsAmbiguousWriteFailure(ex))
        {
            return await SettleCustomerAsync(link, ex);
        }
        catch (SdkException ex)
        {
            await DeleteClaimAsync(link);
            throw ProviderFailure(ex, "create customer");
        }

        if (created.Customer?.Id is not int customerId)
        {
            return await SettleCustomerAsync(link, new InvalidOperationException("Maxio accepted the customer but returned no customer id."));
        }

        await RecordCompletedAsync(link, customerId);
        _logger.LogInformation("Created Maxio customer {CustomerId}.", customerId);
        return customerId;
    }

    /// <summary>
    /// Settles a <c>CreateCustomer</c> whose outcome is unknown by re-reading Maxio by the reference it carried.
    /// </summary>
    private async Task<int> SettleCustomerAsync(BillingCustomer link, Exception cause)
    {
        _logger.LogWarning(cause, "Outcome of Maxio CreateCustomer is unknown; settling by reference.");

        var found = await TryFindCustomerForSettlementAsync(link.CustomerReference);
        if (found?.Id is int id)
        {
            await RecordCompletedAsync(link, id);
            _logger.LogInformation("Settled unknown CreateCustomer outcome: Maxio customer {CustomerId} exists.", id);
            return id;
        }

        await RecordUnknownAsync(link);
        _logger.LogError("Could not confirm whether Maxio created the customer; left for the next request to settle.");
        throw new SubscriptionBillingException(SubscriptionBillingFailure.OutcomeUnknown,
            "Your billing account request reached the billing system but its outcome could not be confirmed. Retry the request; it will not create a duplicate.",
            innerException: cause);
    }

    private async Task<Customer?> TryFindCustomerForSettlementAsync(string customerReference)
    {
        using var settle = new CancellationTokenSource(SettlementBudget);
        try
        {
            return await FindCustomerByReferenceAsync(customerReference, settle.Token);
        }
        catch (Exception ex) when (ex is SdkException or OperationCanceledException or SubscriptionBillingException)
        {
            _logger.LogWarning(ex, "ReadCustomerByReference failed while settling.");
            return null;
        }
    }

    private async Task<Customer?> FindCustomerByReferenceAsync(string customerReference, CancellationToken token)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = customerReference }, cancellationToken: token);
            return response.Customer;
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private static (string FirstName, string LastName, string Email) DeriveCustomerIdentity(string customerReference)
    {
        // eShop identities are e-mail addresses and carry no name fields; Maxio requires first/last name and e-mail.
        var at = customerReference.IndexOf('@');
        if (at <= 0 || at == customerReference.Length - 1)
        {
            throw new SubscriptionBillingException(SubscriptionBillingFailure.InvalidRequest,
                "Subscribing requires an account identified by an e-mail address.");
        }

        return (customerReference[..at], "eShop Customer", customerReference);
    }

    // ---------------------------------------------------------------- my subscriptions

    public async Task<IReadOnlyList<SubscriptionDetails>> ListSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(customerReference))
        {
            throw new SubscriptionBillingException(SubscriptionBillingFailure.InvalidRequest, "The caller has no identity.");
        }

        customerReference = customerReference.Trim();
        using var budget = new CallBudget(cancellationToken);

        var customerId = await _db.BillingCustomers.AsNoTracking()
            .Where(c => c.CustomerReference == customerReference && c.Status == BillingClaimStatus.Completed)
            .Select(c => c.ProviderId)
            .FirstOrDefaultAsync(budget.Token);

        if (customerId is null)
        {
            // No local link (never subscribed here, or the store was reset): Maxio is the system of record.
            var customer = await ReadAsync("find customer", budget, token => FindCustomerByReferenceAsync(customerReference, token));
            customerId = customer?.Id;
        }

        if (customerId is null)
        {
            return Array.Empty<SubscriptionDetails>();
        }

        var responses = await ReadAsync("list subscriptions", budget, token =>
            _client.Customers.ListCustomerSubscriptions(new ListCustomerSubscriptionsRequest { CustomerId = customerId.Value },
                cancellationToken: token));

        return responses
            .Select(r => r.Subscription)
            .Where(s => s?.Id is not null)
            .Select(s => MapSubscription(s, "list subscriptions"))
            .OrderByDescending(s => s.CreatedAt)
            .ToList();
    }

    // ---------------------------------------------------------------- mapping

    private SubscriptionDetails MapSubscription(Subscription? subscription, string operation)
    {
        if (subscription?.Id is not int id)
        {
            _logger.LogError("Maxio returned no subscription body for {Operation}.", operation);
            throw new SubscriptionBillingException(SubscriptionBillingFailure.ProviderUnavailable,
                "The billing system returned an incomplete response.");
        }

        return new SubscriptionDetails(
            id,
            subscription.Reference,
            subscription.Product?.Handle,
            subscription.Product?.Name,
            subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents,
            subscription.Currency,
            subscription.Product?.Interval,
            subscription.Product?.IntervalUnit?.Value,
            subscription.State?.Value,
            // current_period_ends_at: when the next regularly scheduled charge will be attempted.
            subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
            subscription.ActivatedAt,
            subscription.CreatedAt);
    }

    // ---------------------------------------------------------------- claims

    private enum ClaimOutcome
    {
        /// <summary>A fresh claim row was inserted by this request.</summary>
        Acquired,

        /// <summary>An abandoned or unknown claim was taken over by this request.</summary>
        TakenOver,

        /// <summary>The write already completed; the row carries the provider id.</summary>
        AlreadyCompleted
    }

    /// <summary>
    /// Inserts the claim row; the store's primary key refuses a second claim for the same key.
    /// </summary>
    private async Task<(TClaim Claim, ClaimOutcome Outcome)> AcquireClaimAsync<TClaim>(TClaim candidate, string key, CallBudget budget)
        where TClaim : class, IBillingClaim
    {
        _db.Add(candidate);
        ExceptionDispatchInfo refusal;
        try
        {
            await _db.SaveChangesAsync(budget.Token);
            return (candidate, ClaimOutcome.Acquired);
        }
        catch (Exception ex) when (IsStoreRefusal(ex))
        {
            _db.Entry(candidate).State = EntityState.Detached;
            refusal = ExceptionDispatchInfo.Capture(ex);
        }

        var existing = await _db.Set<TClaim>().FindAsync(new object[] { key }, budget.Token);
        if (existing is null)
        {
            // No conflicting row: this was a store failure, not a duplicate-key refusal.
            refusal.Throw();
        }

        if (existing.Status == BillingClaimStatus.Completed && existing.ProviderId is not null)
        {
            return (existing, ClaimOutcome.AlreadyCompleted);
        }

        if (existing.Status == BillingClaimStatus.Pending && Now - existing.ClaimedAt < StaleClaimAfter)
        {
            throw new SubscriptionBillingException(SubscriptionBillingFailure.InProgress,
                "This request is already being processed. Try again shortly.");
        }

        // Unknown, or pending but abandoned: take it over. The concurrency token lets exactly one request win.
        existing.TakeOver(Now);
        try
        {
            await _db.SaveChangesAsync(budget.Token);
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.Entry(existing).State = EntityState.Detached;
            throw new SubscriptionBillingException(SubscriptionBillingFailure.InProgress,
                "This request is already being processed. Try again shortly.");
        }

        return (existing, ClaimOutcome.TakenOver);
    }

    /// <summary>
    /// Whether a failed claim insert was the store refusing a duplicate key. SQL Server raises
    /// <see cref="DbUpdateException"/>; the EF in-memory provider raises <see cref="ArgumentException"/>.
    /// A refusal is confirmed by then finding the existing row.
    /// </summary>
    private static bool IsStoreRefusal(Exception ex) =>
        ex is DbUpdateException or ArgumentException;

    /// <summary>Gives up a claim when nothing was written to Maxio.</summary>
    private Task ReleaseClaimAsync<TClaim>(TClaim claim, ClaimOutcome outcome) where TClaim : class, IBillingClaim =>
        // A taken-over claim stays Unknown: an earlier attempt may still have landed, and the next request settles it.
        outcome == ClaimOutcome.Acquired ? DeleteClaimAsync(claim) : RecordUnknownAsync(claim);

    private async Task DeleteClaimAsync<TClaim>(TClaim claim) where TClaim : class, IBillingClaim
    {
        _db.Remove(claim);
        await SaveRecordAsync(claim, "release");
    }

    private async Task RecordCompletedAsync<TClaim>(TClaim claim, int providerId) where TClaim : class, IBillingClaim
    {
        claim.Complete(providerId, Now);
        await SaveRecordAsync(claim, "complete");
    }

    private async Task RecordUnknownAsync<TClaim>(TClaim claim) where TClaim : class, IBillingClaim
    {
        claim.MarkUnknown(Now);
        await SaveRecordAsync(claim, "mark unknown");
    }

    /// <summary>
    /// Records a claim transition after Maxio has (or has not) acted. Never cancelled by the request budget;
    /// a store failure is logged rather than masking the provider outcome — the claim then goes stale and the
    /// next request settles it against Maxio.
    /// </summary>
    private async Task SaveRecordAsync<TClaim>(TClaim claim, string transition) where TClaim : class, IBillingClaim
    {
        try
        {
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
        {
            _logger.LogError(ex, "Failed to {Transition} billing claim of type {ClaimType}.", transition, typeof(TClaim).Name);
            _db.Entry(claim).State = EntityState.Detached;
        }
    }

    // ---------------------------------------------------------------- error translation

    /// <summary>
    /// Runs a read under the call budget and converts every SDK failure into a caller-safe
    /// <see cref="SubscriptionBillingException"/>. The caller's own cancellation propagates untouched.
    /// </summary>
    private async Task<T> ReadAsync<T>(string operation, CallBudget budget, Func<CancellationToken, Task<T>> read)
    {
        try
        {
            return await read(budget.Token);
        }
        catch (SdkException ex)
        {
            throw ProviderFailure(ex, operation);
        }
        catch (OperationCanceledException ex) when (!budget.CallerCancelled)
        {
            _logger.LogWarning(ex, "Maxio {Operation} exceeded the {Budget}s call budget.", operation, TotalCallBudget.TotalSeconds);
            throw new SubscriptionBillingException(SubscriptionBillingFailure.ProviderUnavailable,
                "The billing system did not respond in time.", innerException: ex);
        }
    }

    /// <summary>A 4xx other than 408 means Maxio refused the write and did not act on it.</summary>
    private static bool IsDefiniteRejection(HttpStatusCode status) =>
        (int)status is >= 400 and < 500 && status != HttpStatusCode.RequestTimeout;

    /// <summary>Failures after which a write may still have landed at Maxio.</summary>
    private static bool IsAmbiguousWriteFailure(Exception ex) => ex switch
    {
        SdkConnectionException => true,                        // includes SdkTimeoutException
        OperationCanceledException => true,                    // budget or caller cancelled mid-flight
        ResponseDeserializationException rde => !IsDefiniteRejection(rde.StatusCode), // unreadable 2xx/5xx
        ApiException api => !IsDefiniteRejection(api.StatusCode),                      // 5xx, 408
        _ => false
    };

    private SubscriptionBillingException Rejection(ApiException ex, string operation, IReadOnlyList<string> details)
    {
        if (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden || (int)ex.StatusCode == 429)
        {
            // Our credentials or our quota — not something the caller can fix.
            return ProviderFailure(ex, operation);
        }

        _logger.LogWarning("Maxio rejected {Operation} with HTTP {Status}: {Errors}", operation, (int)ex.StatusCode, string.Join("; ", details));
        return new SubscriptionBillingException(SubscriptionBillingFailure.Rejected,
            "The billing system rejected the request.", details, ex);
    }

    private SubscriptionBillingException ProviderFailure(SdkException ex, string operation)
    {
        switch (ex)
        {
            case ApiException api when api.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                _logger.LogError(ex, "Maxio refused this application's credentials during {Operation} (HTTP {Status}); check Maxio:ApiKey and Maxio:Subdomain.",
                    operation, (int)api.StatusCode);
                return new SubscriptionBillingException(SubscriptionBillingFailure.ProviderUnavailable,
                    "The billing system is unavailable.", innerException: ex);

            case ApiException api when (int)api.StatusCode == 429:
                _logger.LogWarning(ex, "Maxio throttled {Operation}.", operation);
                return new SubscriptionBillingException(SubscriptionBillingFailure.ProviderBusy,
                    "The billing system is busy. Try again shortly.", innerException: ex);

            case ResponseDeserializationException rde:
                _logger.LogError(ex, "Maxio returned an unreadable {TargetType} body for {Operation} (HTTP {Status}).",
                    rde.TargetType.Name, operation, (int)rde.StatusCode);
                return new SubscriptionBillingException(SubscriptionBillingFailure.ProviderUnavailable,
                    "The billing system returned a response that could not be processed.", innerException: ex);

            case ApiException api:
                _logger.LogError(ex, "Maxio {Operation} failed with HTTP {Status}.", operation, (int)api.StatusCode);
                return new SubscriptionBillingException(SubscriptionBillingFailure.ProviderUnavailable,
                    "The billing system is unavailable.", innerException: ex);

            case SdkTimeoutException timeout:
                _logger.LogWarning(ex, "Maxio {Operation} received no response within {Timeout}.", operation, timeout.Timeout);
                return new SubscriptionBillingException(SubscriptionBillingFailure.ProviderUnavailable,
                    "The billing system did not respond in time.", innerException: ex);

            case SdkConnectionException:
                _logger.LogWarning(ex, "Maxio {Operation} could not reach the billing system.", operation);
                return new SubscriptionBillingException(SubscriptionBillingFailure.ProviderUnavailable,
                    "The billing system is unreachable.", innerException: ex);

            case AuthSchemeException:
                _logger.LogError(ex, "Maxio credentials could not be applied for {Operation}.", operation);
                return new SubscriptionBillingException(SubscriptionBillingFailure.ProviderUnavailable,
                    "The billing system is unavailable.", innerException: ex);

            default:
                _logger.LogError(ex, "Maxio {Operation} failed.", operation);
                return new SubscriptionBillingException(SubscriptionBillingFailure.ProviderUnavailable,
                    "The billing system is unavailable.", innerException: ex);
        }
    }

    private DateTimeOffset Now => _clock.GetUtcNow();

    /// <summary>
    /// One total budget for everything a service call sends to Maxio, linked to the caller's cancellation.
    /// </summary>
    private sealed class CallBudget : IDisposable
    {
        private readonly CancellationToken _caller;
        private readonly CancellationTokenSource _source;

        public CallBudget(CancellationToken caller)
        {
            _caller = caller;
            _source = CancellationTokenSource.CreateLinkedTokenSource(caller);
            _source.CancelAfter(TotalCallBudget);
        }

        public CancellationToken Token => _source.Token;

        public bool CallerCancelled => _caller.IsCancellationRequested;

        public void Dispose() => _source.Dispose();
    }
}
