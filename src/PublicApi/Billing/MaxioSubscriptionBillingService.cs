using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Maxio = MaxioAdvancedBilling.Models;
using RawError = MaxioAdvancedBilling.Core.ErrorResponse.RawError;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Subscription billing with Maxio Advanced Billing as the system of record.
/// <para>
/// Every write follows claim → provider call → record: a claim keyed by a deterministic reference is inserted
/// in this application's store first, so a second request for the same thing is refused before it reaches
/// Maxio. A write whose outcome is unknown (lost connection, timeout, unreadable success) is settled by
/// re-reading Maxio by that reference, never reported as a plain failure.
/// </para>
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    /// <summary>Whole-call budget for the read endpoints.</summary>
    public static readonly TimeSpan ReadBudget = TimeSpan.FromSeconds(20);

    /// <summary>Whole-call budget for subscribe, shared by every provider call it makes.</summary>
    public static readonly TimeSpan SubscribeBudget = TimeSpan.FromSeconds(30);

    /// <summary>Budget for re-reading the provider after an unknown write outcome; not tied to the caller.</summary>
    public static readonly TimeSpan SettleBudget = TimeSpan.FromSeconds(10);

    /// <summary>An unfinished claim untouched for this long belongs to a request that is gone.</summary>
    public static readonly TimeSpan ClaimStaleAfter = TimeSpan.FromMinutes(2);

    public const int PlanPageSize = 200;
    public const int MaxPlanPages = 5;

    private static readonly TimeSpan CatalogCacheDuration = TimeSpan.FromSeconds(60);
    private const string CustomerLastNamePlaceholder = "Shopper";

    private readonly MaxioAdvancedBillingClient _client;
    private readonly IBillingClaimStore _claims;
    private readonly IMemoryCache _cache;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;
    private readonly string _productFamilyHandle;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        IBillingClaimStore claims,
        IOptions<MaxioSettings> settings,
        IMemoryCache cache,
        TimeProvider timeProvider,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _claims = claims;
        _cache = cache;
        _timeProvider = timeProvider;
        _logger = logger;
        _productFamilyHandle = settings.Value.ProductFamilyHandle!.Trim();
    }

    private DateTimeOffset Now => _timeProvider.GetUtcNow();

    public async Task<SubscriptionPlanCatalog> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        using var budget = new CallBudget(ReadBudget, cancellationToken);
        try
        {
            return await GetPlanCatalogAsync(budget);
        }
        catch (OperationCanceledException ex) when (budget.Expired)
        {
            throw BudgetExpired(ex);
        }
    }

    public async Task<SubscribeResult> SubscribeAsync(BillingBuyer buyer, string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new BillingException(BillingErrorKind.InvalidRequest, "A plan handle is required.");
        }

        using var budget = new CallBudget(SubscribeBudget, cancellationToken);
        try
        {
            // The plan must be one this shop offers: a product of the configured family, not merely one on the site.
            var catalog = await GetPlanCatalogAsync(budget);
            var plan = catalog.Plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle.Trim(), StringComparison.OrdinalIgnoreCase));
            if (plan is null)
            {
                throw catalog.IsComplete
                    ? new BillingException(BillingErrorKind.InvalidRequest, $"'{planHandle.Trim()}' is not an available subscription plan.")
                    : new BillingException(BillingErrorKind.CatalogIncomplete, "The plan catalogue could not be read completely. Try again shortly.");
            }

            var reference = BillingReferences.ForSubscription(buyer.UserName, plan.Handle);

            // A second pass only happens when an abandoned claim was released by the first.
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var claim = new SubscriptionClaim(reference, buyer.UserName, plan.Handle, Now);
                if (await _claims.TryAddAsync(claim, budget.Token))
                {
                    return await SubscribeWithClaimAsync(claim, buyer, plan, budget);
                }

                var existing = await ResolveExistingSubscriptionClaimAsync(reference, budget);
                if (existing is not null)
                {
                    return existing;
                }
            }

            throw new BillingException(BillingErrorKind.InProgress, "A subscription to this plan is already being processed.");
        }
        catch (OperationCanceledException ex) when (budget.Expired)
        {
            throw BudgetExpired(ex);
        }
    }

    public async Task<BuyerSubscriptions> GetSubscriptionsAsync(BillingBuyer buyer, CancellationToken cancellationToken = default)
    {
        using var budget = new CallBudget(ReadBudget, cancellationToken);
        try
        {
            var pendingPlanHandles = await SettleUnsettledSubscriptionClaimsAsync(buyer, budget);

            var customerId = await FindCustomerIdAsync(buyer, budget);
            if (customerId is null)
            {
                return new BuyerSubscriptions(Array.Empty<SubscriptionDetails>(), pendingPlanHandles);
            }

            var responses = await ListCustomerSubscriptionsAsync(customerId.Value, budget);
            var subscriptions = responses
                .Select(r => r?.Subscription)
                .OfType<Maxio.Subscription>()
                .Where(s => s.Id is not null && IsInConfiguredFamily(s))
                .Select(MapSubscription)
                .OrderByDescending(s => s.ActivatedAt)
                .ToList();

            var stillPending = pendingPlanHandles
                .Where(handle => !subscriptions.Any(s => string.Equals(s.PlanHandle, handle, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            return new BuyerSubscriptions(subscriptions, stillPending);
        }
        catch (OperationCanceledException ex) when (budget.Expired)
        {
            throw BudgetExpired(ex);
        }
    }

    // ---------------------------------------------------------------- plan catalogue

    private async Task<SubscriptionPlanCatalog> GetPlanCatalogAsync(CallBudget budget)
    {
        var cacheKey = "maxio-plan-catalog:" + _productFamilyHandle;
        if (_cache.TryGetValue(cacheKey, out SubscriptionPlanCatalog? cached) && cached is not null)
        {
            return cached;
        }

        var plans = new List<SubscriptionPlan>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var isComplete = false;

        // The SDK does not page this list, so the pages are walked here, bounded by a page cap,
        // a no-progress guard and the call budget.
        for (var page = 1; page <= MaxPlanPages; page++)
        {
            var products = await ListProductsPageAsync(page, budget);

            var newProducts = 0;
            foreach (var product in products.Select(p => p?.Product).OfType<Maxio.Product>())
            {
                var key = product.Id?.ToString() ?? product.Handle ?? string.Empty;
                if (!seen.Add(key))
                {
                    continue;
                }
                newProducts++;

                if (product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
                {
                    plans.Add(MapPlan(product));
                }
            }

            if (products.Count < PlanPageSize)
            {
                isComplete = true;
                break;
            }

            if (newProducts == 0)
            {
                _logger.LogWarning("Maxio product listing for family {ProductFamilyHandle} stopped advancing at page {Page}.", _productFamilyHandle, page);
                break;
            }
        }

        if (!isComplete)
        {
            _logger.LogWarning("Maxio product listing for family {ProductFamilyHandle} was cut short after {PlanCount} plans.", _productFamilyHandle, plans.Count);
        }

        var catalog = new SubscriptionPlanCatalog(plans, isComplete);
        if (isComplete)
        {
            _cache.Set(cacheKey, catalog, CatalogCacheDuration);
        }
        return catalog;
    }

    // ---------------------------------------------------------------- subscribe

    private async Task<SubscribeResult> SubscribeWithClaimAsync(SubscriptionClaim claim, BillingBuyer buyer, SubscriptionPlan plan, CallBudget budget)
    {
        int customerId;
        Maxio.Subscription? existing;
        try
        {
            customerId = await EnsureCustomerAsync(buyer, budget);

            // The claim store may not outlive the process; Maxio is the record of what already exists.
            existing = await TryFindSubscriptionAsync(claim.Reference, budget);
        }
        catch
        {
            await ReleaseClaimAsync(claim);
            throw;
        }

        if (existing?.Id is int existingId)
        {
            await CompleteClaimAsync(claim, existingId);
            _logger.LogInformation("Adopted existing Maxio subscription {SubscriptionId} for reference {SubscriptionReference}.", existingId, claim.Reference);
            return new SubscribeResult(MapSubscription(existing), Created: false);
        }

        Maxio.SubscriptionResponse response;
        try
        {
            response = await _client.Subscriptions.CreateSubscription(
                new CreateSubscriptionOperationRequest
                {
                    Body = new Maxio.CreateSubscriptionRequest
                    {
                        Subscription = new Maxio.CreateSubscription
                        {
                            ProductHandle = plan.Handle,
                            CustomerId = customerId,
                            Reference = claim.Reference,
                            // No card is captured in this flow, so the shopper is invoiced rather than charged
                            // automatically; automatic collection would be refused for want of a payment method.
                            PaymentCollectionMethod = Maxio.Enums.CollectionMethod.Remittance
                        }
                    }
                },
                cancellationToken: budget.Token);
        }
        catch (ApiException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var rejected))
            {
                return await HandleRejectedSubscriptionAsync(claim, rejected.Errors ?? Array.Empty<string>(), budget);
            }
            else if (ex.Error.TryGetRawError(out RawError _) && (int)ex.StatusCode >= 500)
            {
                return await SettleUnknownSubscriptionAsync(claim, ex);
            }

            await ReleaseClaimAsync(claim);
            throw Translate(nameof(_client.Subscriptions.CreateSubscription), ex, budget);
        }
        catch (Exception ex) when (IsAmbiguousWriteFailure(ex))
        {
            return await SettleUnknownSubscriptionAsync(claim, ex);
        }
        catch (SdkException ex)
        {
            await ReleaseClaimAsync(claim);
            throw Translate(nameof(_client.Subscriptions.CreateSubscription), ex, budget);
        }

        var subscription = response.Subscription;
        if (subscription?.Id is not int subscriptionId)
        {
            // A success that does not say what was created: the write landed, but which one is unknown.
            return await SettleUnknownSubscriptionAsync(claim, cause: null);
        }

        await CompleteClaimAsync(claim, subscriptionId);
        _logger.LogInformation("Created Maxio subscription {SubscriptionId} for reference {SubscriptionReference}.", subscriptionId, claim.Reference);
        return new SubscribeResult(MapSubscription(subscription), Created: true);
    }

    /// <summary>
    /// Another request holds the claim for this reference. Returns its result, or null when an abandoned claim
    /// was released and the caller may claim again.
    /// </summary>
    private async Task<SubscribeResult?> ResolveExistingSubscriptionClaimAsync(string reference, CallBudget budget)
    {
        var existing = await _claims.GetAsync<SubscriptionClaim>(reference, budget.Token);
        if (existing is null)
        {
            return null;
        }

        if (existing.State == BillingClaimState.Completed && existing.BillingSubscriptionId is int subscriptionId)
        {
            var subscription = await ReadSubscriptionAsync(subscriptionId, budget);
            return new SubscribeResult(MapSubscription(subscription), Created: false);
        }

        if (existing.State == BillingClaimState.Pending && !existing.IsStale(Now, ClaimStaleAfter))
        {
            throw new BillingException(BillingErrorKind.InProgress, "A subscription to this plan is already being processed.");
        }

        // Unknown, or pending for a request that is gone: Maxio decides what happened.
        var (found, released) = await SettleSubscriptionClaimAsync(existing, budget);
        if (found is not null)
        {
            return new SubscribeResult(MapSubscription(found), Created: false);
        }
        if (released)
        {
            return null;
        }

        throw OutcomePending();
    }

    private async Task<(Maxio.Subscription? Found, bool Released)> SettleSubscriptionClaimAsync(SubscriptionClaim claim, CallBudget budget)
    {
        var found = await TryFindSubscriptionAsync(claim.Reference, budget);
        if (found?.Id is int subscriptionId)
        {
            await CompleteClaimAsync(claim, subscriptionId);
            _logger.LogInformation("Settled subscription reference {SubscriptionReference}: Maxio subscription {SubscriptionId}.", claim.Reference, subscriptionId);
            return (found, false);
        }

        if (claim.IsStale(Now, ClaimStaleAfter))
        {
            // Maxio has nothing for the reference long after the attempt: nothing was created, so a new attempt is safe.
            await ReleaseClaimAsync(claim);
            _logger.LogInformation("Released abandoned subscription claim {SubscriptionReference}: nothing found at Maxio.", claim.Reference);
            return (null, true);
        }

        return (null, false);
    }

    private async Task<SubscribeResult> SettleUnknownSubscriptionAsync(SubscriptionClaim claim, Exception? cause)
    {
        _logger.LogWarning(cause, "Outcome of creating subscription {SubscriptionReference} is unknown; re-reading Maxio.", claim.Reference);

        using var settle = new CallBudget(SettleBudget, CancellationToken.None);
        try
        {
            var found = await TryFindSubscriptionAsync(claim.Reference, settle);
            if (found?.Id is int subscriptionId)
            {
                await CompleteClaimAsync(claim, subscriptionId);
                _logger.LogInformation("Settled subscription reference {SubscriptionReference}: Maxio subscription {SubscriptionId}.", claim.Reference, subscriptionId);
                return new SubscribeResult(MapSubscription(found), Created: true);
            }
        }
        catch (BillingException ex)
        {
            _logger.LogWarning(ex, "Re-reading subscription {SubscriptionReference} failed.", claim.Reference);
        }

        claim.MarkUnknown(Now);
        await _claims.UpdateAsync(claim, CancellationToken.None);
        _logger.LogError("Subscription {SubscriptionReference} left in unknown state; it is settled on the next request for this shopper.", claim.Reference);
        throw OutcomePending();
    }

    private async Task<SubscribeResult> HandleRejectedSubscriptionAsync(SubscriptionClaim claim, IReadOnlyList<string> errors, CallBudget budget)
    {
        _logger.LogWarning("Maxio rejected subscription {SubscriptionReference}: {Errors}", claim.Reference, string.Join("; ", errors));

        // A rejection can mean the reference is already taken by an earlier attempt: adopt it if so.
        Maxio.Subscription? found = null;
        try
        {
            found = await TryFindSubscriptionAsync(claim.Reference, budget);
        }
        catch (BillingException ex)
        {
            _logger.LogWarning(ex, "Re-reading rejected subscription {SubscriptionReference} failed.", claim.Reference);
        }

        if (found?.Id is int subscriptionId)
        {
            await CompleteClaimAsync(claim, subscriptionId);
            return new SubscribeResult(MapSubscription(found), Created: false);
        }

        await ReleaseClaimAsync(claim);
        throw new BillingException(BillingErrorKind.Rejected, "The billing provider rejected the subscription.", errors);
    }

    // ---------------------------------------------------------------- customer

    private async Task<int> EnsureCustomerAsync(BillingBuyer buyer, CallBudget budget)
    {
        var reference = BillingReferences.ForCustomer(buyer.UserName);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var claim = new BillingCustomerClaim(reference, buyer.UserName, Now);
            if (await _claims.TryAddAsync(claim, budget.Token))
            {
                return await CreateOrAdoptCustomerAsync(claim, buyer, budget);
            }

            var existing = await _claims.GetAsync<BillingCustomerClaim>(reference, budget.Token);
            if (existing is null)
            {
                continue;
            }

            if (existing.State == BillingClaimState.Completed && existing.BillingCustomerId is int customerId)
            {
                return customerId;
            }

            if (existing.State == BillingClaimState.Pending && !existing.IsStale(Now, ClaimStaleAfter))
            {
                throw new BillingException(BillingErrorKind.InProgress, "Your billing account is being set up. Try again shortly.");
            }

            var found = await TryReadCustomerByReferenceAsync(reference, budget);
            if (found?.Id is int foundId)
            {
                existing.Complete(foundId, Now);
                await _claims.UpdateAsync(existing, CancellationToken.None);
                return foundId;
            }

            if (existing.IsStale(Now, ClaimStaleAfter))
            {
                await ReleaseClaimAsync(existing);
                continue;
            }

            throw OutcomePending();
        }

        throw new BillingException(BillingErrorKind.InProgress, "Your billing account is being set up. Try again shortly.");
    }

    private async Task<int> CreateOrAdoptCustomerAsync(BillingCustomerClaim claim, BillingBuyer buyer, CallBudget budget)
    {
        Maxio.Customer? existing;
        try
        {
            existing = await TryReadCustomerByReferenceAsync(claim.Reference, budget);
        }
        catch
        {
            await ReleaseClaimAsync(claim);
            throw;
        }

        if (existing?.Id is int existingId)
        {
            await CompleteClaimAsync(claim, existingId);
            _logger.LogInformation("Adopted existing Maxio customer {CustomerId} for reference {CustomerReference}.", existingId, claim.Reference);
            return existingId;
        }

        Maxio.CustomerResponse response;
        try
        {
            response = await _client.Customers.CreateCustomer(
                new CreateCustomerOperationRequest
                {
                    Body = new Maxio.CreateCustomerRequest
                    {
                        Customer = new Maxio.CreateCustomer
                        {
                            FirstName = FirstNameFrom(buyer.Email),
                            LastName = CustomerLastNamePlaceholder,
                            Email = buyer.Email,
                            Reference = claim.Reference
                        }
                    }
                },
                cancellationToken: budget.Token);
        }
        catch (ApiException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var rejected))
            {
                return await HandleRejectedCustomerAsync(claim, ErrorsFrom(rejected), budget);
            }
            else if (ex.Error.TryGetRawError(out RawError _) && (int)ex.StatusCode >= 500)
            {
                return await SettleUnknownCustomerAsync(claim, ex);
            }

            await ReleaseClaimAsync(claim);
            throw Translate(nameof(_client.Customers.CreateCustomer), ex, budget);
        }
        catch (Exception ex) when (IsAmbiguousWriteFailure(ex))
        {
            return await SettleUnknownCustomerAsync(claim, ex);
        }
        catch (SdkException ex)
        {
            await ReleaseClaimAsync(claim);
            throw Translate(nameof(_client.Customers.CreateCustomer), ex, budget);
        }

        if (response.Customer?.Id is not int customerId)
        {
            return await SettleUnknownCustomerAsync(claim, cause: null);
        }

        await CompleteClaimAsync(claim, customerId);
        _logger.LogInformation("Created Maxio customer {CustomerId} for reference {CustomerReference}.", customerId, claim.Reference);
        return customerId;
    }

    private async Task<int> SettleUnknownCustomerAsync(BillingCustomerClaim claim, Exception? cause)
    {
        _logger.LogWarning(cause, "Outcome of creating customer {CustomerReference} is unknown; re-reading Maxio.", claim.Reference);

        using var settle = new CallBudget(SettleBudget, CancellationToken.None);
        try
        {
            var found = await TryReadCustomerByReferenceAsync(claim.Reference, settle);
            if (found?.Id is int customerId)
            {
                await CompleteClaimAsync(claim, customerId);
                _logger.LogInformation("Settled customer reference {CustomerReference}: Maxio customer {CustomerId}.", claim.Reference, customerId);
                return customerId;
            }
        }
        catch (BillingException ex)
        {
            _logger.LogWarning(ex, "Re-reading customer {CustomerReference} failed.", claim.Reference);
        }

        claim.MarkUnknown(Now);
        await _claims.UpdateAsync(claim, CancellationToken.None);
        _logger.LogError("Customer {CustomerReference} left in unknown state; it is settled on the next request for this shopper.", claim.Reference);
        throw OutcomePending();
    }

    private async Task<int> HandleRejectedCustomerAsync(BillingCustomerClaim claim, IReadOnlyList<string> errors, CallBudget budget)
    {
        _logger.LogWarning("Maxio rejected customer {CustomerReference}: {Errors}", claim.Reference, string.Join("; ", errors));

        // Customer references are unique at Maxio: a rejection may mean the customer already exists.
        Maxio.Customer? found = null;
        try
        {
            found = await TryReadCustomerByReferenceAsync(claim.Reference, budget);
        }
        catch (BillingException ex)
        {
            _logger.LogWarning(ex, "Re-reading rejected customer {CustomerReference} failed.", claim.Reference);
        }

        if (found?.Id is int customerId)
        {
            await CompleteClaimAsync(claim, customerId);
            return customerId;
        }

        await ReleaseClaimAsync(claim);
        throw new BillingException(BillingErrorKind.Rejected, "The billing provider rejected the customer account.", errors);
    }

    private async Task<int?> FindCustomerIdAsync(BillingBuyer buyer, CallBudget budget)
    {
        var reference = BillingReferences.ForCustomer(buyer.UserName);
        var claim = await _claims.GetAsync<BillingCustomerClaim>(reference, budget.Token);
        if (claim is { State: BillingClaimState.Completed, BillingCustomerId: int customerId })
        {
            return customerId;
        }

        var found = await TryReadCustomerByReferenceAsync(reference, budget);
        return found?.Id;
    }

    // ---------------------------------------------------------------- my subscriptions

    /// <summary>Settles the buyer's unfinished subscription claims; returns the plan handles still not settled.</summary>
    private async Task<List<string>> SettleUnsettledSubscriptionClaimsAsync(BillingBuyer buyer, CallBudget budget)
    {
        var pending = new List<string>();
        foreach (var claim in await _claims.ListUnsettledSubscriptionClaimsAsync(buyer.UserName, budget.Token))
        {
            if (claim.State == BillingClaimState.Pending && !claim.IsStale(Now, ClaimStaleAfter))
            {
                pending.Add(claim.PlanHandle);
                continue;
            }

            try
            {
                var (found, released) = await SettleSubscriptionClaimAsync(claim, budget);
                if (found is null && !released)
                {
                    pending.Add(claim.PlanHandle);
                }
            }
            catch (BillingException ex)
            {
                _logger.LogWarning(ex, "Settling subscription {SubscriptionReference} failed; it stays pending.", claim.Reference);
                pending.Add(claim.PlanHandle);
            }
        }
        return pending;
    }

    private bool IsInConfiguredFamily(Maxio.Subscription subscription) =>
        string.Equals(subscription.Product?.ProductFamily?.Handle, _productFamilyHandle, StringComparison.OrdinalIgnoreCase);

    // ---------------------------------------------------------------- provider reads

    private async Task<IReadOnlyList<Maxio.ProductResponse>> ListProductsPageAsync(int page, CallBudget budget)
    {
        try
        {
            return await _client.ProductFamilies.ListProductsForProductFamily(
                new ListProductsForProductFamilyRequest
                {
                    ProductFamilyId = "handle:" + _productFamilyHandle,
                    Page = page,
                    PerPage = PlanPageSize
                },
                cancellationToken: budget.Token);
        }
        catch (ApiException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out _))
            {
                _logger.LogError("Maxio product family {ProductFamilyHandle} (Maxio:ProductFamilyHandle) was not found.", _productFamilyHandle);
                throw new BillingException(BillingErrorKind.ProviderUnavailable, "Subscription plans are not available right now.", innerException: ex);
            }
            else if (ex.Error.TryGetRawError(out RawError _))
            {
                throw Translate(nameof(_client.ProductFamilies.ListProductsForProductFamily), ex, budget);
            }
            throw Translate(nameof(_client.ProductFamilies.ListProductsForProductFamily), ex, budget);
        }
        catch (Exception ex) when (IsProviderFailure(ex, budget))
        {
            throw Translate(nameof(_client.ProductFamilies.ListProductsForProductFamily), ex, budget);
        }
    }

    private async Task<Maxio.Customer?> TryReadCustomerByReferenceAsync(string reference, CallBudget budget)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = reference },
                cancellationToken: budget.Token);
            return response.Customer;
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex) when (IsProviderFailure(ex, budget))
        {
            throw Translate(nameof(_client.Customers.ReadCustomerByReference), ex, budget);
        }
    }

    private async Task<Maxio.Subscription?> TryFindSubscriptionAsync(string reference, CallBudget budget)
    {
        try
        {
            var response = await _client.Subscriptions.FindSubscription(
                new FindSubscriptionRequest { Reference = reference },
                cancellationToken: budget.Token);
            return response.Subscription;
        }
        catch (ApiException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out RawError _))
            {
                return null;
            }
            else if (ex.Error.TryGetRawError(out RawError _))
            {
                throw Translate(nameof(_client.Subscriptions.FindSubscription), ex, budget);
            }
            throw Translate(nameof(_client.Subscriptions.FindSubscription), ex, budget);
        }
        catch (Exception ex) when (IsProviderFailure(ex, budget))
        {
            throw Translate(nameof(_client.Subscriptions.FindSubscription), ex, budget);
        }
    }

    private async Task<Maxio.Subscription> ReadSubscriptionAsync(int subscriptionId, CallBudget budget)
    {
        Maxio.SubscriptionResponse response;
        try
        {
            response = await _client.Subscriptions.ReadSubscription(
                new ReadSubscriptionRequest { SubscriptionId = subscriptionId },
                cancellationToken: budget.Token);
        }
        catch (Exception ex) when (IsProviderFailure(ex, budget))
        {
            throw Translate(nameof(_client.Subscriptions.ReadSubscription), ex, budget);
        }

        if (response.Subscription?.Id is null)
        {
            _logger.LogError("Maxio returned no subscription body for subscription {SubscriptionId}.", subscriptionId);
            throw new BillingException(BillingErrorKind.ProviderUnavailable, "The billing provider returned a response that could not be processed.");
        }
        return response.Subscription;
    }

    private async Task<IReadOnlyList<Maxio.SubscriptionResponse>> ListCustomerSubscriptionsAsync(int customerId, CallBudget budget)
    {
        try
        {
            return await _client.Customers.ListCustomerSubscriptions(
                new ListCustomerSubscriptionsRequest { CustomerId = customerId },
                cancellationToken: budget.Token);
        }
        catch (Exception ex) when (IsProviderFailure(ex, budget))
        {
            throw Translate(nameof(_client.Customers.ListCustomerSubscriptions), ex, budget);
        }
    }

    // ---------------------------------------------------------------- claims

    private async Task CompleteClaimAsync(BillingCustomerClaim claim, int customerId)
    {
        claim.Complete(customerId, Now);
        // The provider has acted: record it even if the caller has gone away.
        await _claims.UpdateAsync(claim, CancellationToken.None);
    }

    private async Task CompleteClaimAsync(SubscriptionClaim claim, int subscriptionId)
    {
        claim.Complete(subscriptionId, Now);
        await _claims.UpdateAsync(claim, CancellationToken.None);
    }

    private async Task ReleaseClaimAsync<TClaim>(TClaim claim) where TClaim : BillingClaim
    {
        try
        {
            await _claims.RemoveAsync(claim, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Never mask the failure being reported; a claim left behind goes stale and is released later.
            _logger.LogError(ex, "Releasing billing claim {Reference} failed.", claim.Reference);
        }
    }

    // ---------------------------------------------------------------- error boundary

    /// <summary>
    /// A write failure after which the provider may still have acted: no response, an unreadable success,
    /// a provider 5xx, or the call being cancelled while in flight.
    /// </summary>
    private static bool IsAmbiguousWriteFailure(Exception ex) => ex switch
    {
        SdkConnectionException => true, // includes SdkTimeoutException
        ResponseDeserializationException deserialization => (int)deserialization.StatusCode is >= 200 and < 300,
        ApiException api => (int)api.StatusCode >= 500,
        OperationCanceledException => true,
        _ => false
    };

    private static bool IsProviderFailure(Exception ex, CallBudget budget) =>
        ex is SdkException || (ex is OperationCanceledException && budget.Expired);

    /// <summary>
    /// The one place an SDK failure becomes a caller-facing <see cref="BillingException"/>. Our credentials and
    /// our quota are never reported as the caller's fault, and no provider text reaches the caller.
    /// </summary>
    private BillingException Translate(string operation, Exception ex, CallBudget budget)
    {
        switch (ex)
        {
            case ResponseDeserializationException deserialization:
                _logger.LogError(ex, "Maxio {Operation} returned HTTP {StatusCode} with a body that could not be read.", operation, (int)deserialization.StatusCode);
                return new BillingException(BillingErrorKind.ProviderUnavailable, "The billing provider returned a response that could not be processed.", innerException: ex);

            case ApiException api when api.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                _logger.LogError("Maxio {Operation} refused our credentials (HTTP {StatusCode}); check Maxio:ApiKey and Maxio:Subdomain.", operation, (int)api.StatusCode);
                return new BillingException(BillingErrorKind.ProviderUnavailable, "Billing is unavailable right now.", innerException: ex);

            case ApiException api when api.StatusCode == HttpStatusCode.TooManyRequests:
                _logger.LogWarning("Maxio {Operation} throttled this application (HTTP 429).", operation);
                return new BillingException(BillingErrorKind.ProviderBusy, "Billing is busy right now. Try again shortly.", innerException: ex);

            case ApiException api:
                _logger.LogError("Maxio {Operation} failed with HTTP {StatusCode}.", operation, (int)api.StatusCode);
                return new BillingException(BillingErrorKind.ProviderUnavailable, "Billing is unavailable right now.", innerException: ex);

            case SdkTimeoutException timeout:
                _logger.LogError("Maxio {Operation} did not answer within {Timeout}.", operation, timeout.Timeout);
                return new BillingException(BillingErrorKind.Timeout, "The billing provider did not answer in time.", innerException: ex);

            case SdkConnectionException:
                _logger.LogError(ex, "Maxio {Operation} could not be reached.", operation);
                return new BillingException(BillingErrorKind.ProviderUnavailable, "The billing provider could not be reached.", innerException: ex);

            case AuthSchemeException:
                _logger.LogError(ex, "Maxio {Operation}: credentials could not be applied.", operation);
                return new BillingException(BillingErrorKind.ProviderUnavailable, "Billing is unavailable right now.", innerException: ex);

            case OperationCanceledException when budget.Expired:
                _logger.LogError("Maxio {Operation} did not complete within the call budget.", operation);
                return BudgetExpired(ex);

            default:
                _logger.LogError(ex, "Maxio {Operation} failed unexpectedly.", operation);
                return new BillingException(BillingErrorKind.ProviderUnavailable, "Billing is unavailable right now.", innerException: ex);
        }
    }

    private static BillingException BudgetExpired(Exception ex) =>
        new(BillingErrorKind.Timeout, "The billing provider did not answer in time.", innerException: ex);

    private static BillingException OutcomePending() =>
        new(BillingErrorKind.OutcomePending, "Your request was sent to the billing provider but its outcome is not confirmed yet. Check your subscriptions shortly.");

    // ---------------------------------------------------------------- mapping

    private static SubscriptionPlan MapPlan(Maxio.Product product) => new(
        Handle: product.Handle!,
        Name: string.IsNullOrWhiteSpace(product.Name) ? product.Handle! : product.Name,
        Description: string.IsNullOrWhiteSpace(product.Description) ? null : product.Description,
        PriceInCents: product.PriceInCents ?? 0,
        Interval: product.Interval,
        IntervalUnit: product.IntervalUnit?.Value);

    private static SubscriptionDetails MapSubscription(Maxio.Subscription subscription) => new(
        SubscriptionId: subscription.Id!.Value,
        PlanHandle: subscription.Product?.Handle,
        PlanName: subscription.Product?.Name,
        PriceInCents: subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents,
        Currency: subscription.Currency,
        Interval: subscription.Product?.Interval,
        IntervalUnit: subscription.Product?.IntervalUnit?.Value,
        State: subscription.State?.Value ?? "unknown",
        // When the next charge is attempted; the end of the current period when no assessment is scheduled.
        NextBillingAt: subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        ActivatedAt: subscription.ActivatedAt);

    private static IReadOnlyList<string> ErrorsFrom(Maxio.CustomerErrorResponse1 rejected)
    {
        if (rejected.Errors is null)
        {
            return Array.Empty<string>();
        }
        if (rejected.Errors.TryGetCustomerError(out var customerError))
        {
            return string.IsNullOrWhiteSpace(customerError.Customer) ? Array.Empty<string>() : new[] { customerError.Customer };
        }
        if (rejected.Errors.TryGetListOfString(out var messages))
        {
            return messages;
        }
        return Array.Empty<string>();
    }

    private static string FirstNameFrom(string email)
    {
        var at = email.IndexOf('@');
        var localPart = at > 0 ? email[..at] : email;
        return string.IsNullOrWhiteSpace(localPart) ? "eShop" : localPart;
    }

    /// <summary>
    /// The whole-call deadline for one operation: the caller's own cancellation linked with a time budget,
    /// passed to every provider call the operation makes.
    /// </summary>
    private sealed class CallBudget : IDisposable
    {
        private readonly CancellationTokenSource _source;
        private readonly CancellationToken _callerToken;

        public CallBudget(TimeSpan budget, CancellationToken callerToken)
        {
            _callerToken = callerToken;
            _source = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
            _source.CancelAfter(budget);
        }

        public CancellationToken Token => _source.Token;

        /// <summary>True when the budget ran out (as opposed to the caller cancelling).</summary>
        public bool Expired => _source.IsCancellationRequested && !_callerToken.IsCancellationRequested;

        public void Dispose() => _source.Dispose();
    }
}
