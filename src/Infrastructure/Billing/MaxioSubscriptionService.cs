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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data.Config;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Subscription billing with Maxio Advanced Billing as the system of record.
/// Every provider write follows claim → SDK call → record, with the claim held in <see cref="BillingClaimStore"/>.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionBillingService
{
    /// <summary>Ceiling for one SDK call including its retries; the only bound that covers the whole call.</summary>
    public static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(25);

    /// <summary>
    /// Ceiling for the provider reads of one public operation (several SDK calls). Writes are deliberately outside it:
    /// they run under their own <see cref="CallBudget"/> so a deadline never cuts a create off after Maxio may have acted.
    /// </summary>
    public static readonly TimeSpan OperationBudget = TimeSpan.FromSeconds(60);

    /// <summary>How long an unresolved claim is protected before another caller or the sweeper may settle it.</summary>
    public static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(2);

    public const int PlanPageSize = 200;
    public const int MaxPlanPages = 5;

    private const string CustomerReferencePrefix = "eshop-";
    private const int SweepBatchSize = 50;
    private static readonly TimeSpan CompleteCatalogCacheDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PartialCatalogCacheDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SiteSettingsCacheDuration = TimeSpan.FromHours(1);

    // Subscriptions in these states no longer count as "subscribed"; any other (or unrecognised) state does.
    private static readonly SubscriptionState[] EndedStates =
    {
        SubscriptionState.Canceled,
        SubscriptionState.Expired,
        SubscriptionState.FailedToCreate
    };

    private readonly MaxioAdvancedBillingClient _client;
    private readonly BillingClaimStore _claims;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MaxioSubscriptionService> _logger;
    private readonly string _productFamilyHandle;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        BillingClaimStore claims,
        IMemoryCache cache,
        IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _claims = claims;
        _cache = cache;
        _logger = logger;
        _productFamilyHandle = options.Value.ProductFamilyHandle!.Trim();
    }

    public Task<SubscriptionPlanCatalog> GetPlansAsync(CancellationToken cancellationToken = default) =>
        WithinOperationBudget(GetPlansCoreAsync, cancellationToken);

    public Task<SubscribeOutcome> SubscribeAsync(Subscriber subscriber, string planHandle, CancellationToken cancellationToken = default) =>
        WithinOperationBudget(token => SubscribeCoreAsync(subscriber, planHandle, token), cancellationToken);

    public Task<IReadOnlyList<BillingSubscription>> GetSubscriptionsAsync(Subscriber subscriber, CancellationToken cancellationToken = default) =>
        WithinOperationBudget(token => GetSubscriptionsCoreAsync(subscriber, token), cancellationToken);

    private async Task<SubscriptionPlanCatalog> GetPlansCoreAsync(CancellationToken cancellationToken)
    {
        var cacheKey = $"maxio-plans:{_productFamilyHandle}";
        if (_cache.TryGetValue(cacheKey, out SubscriptionPlanCatalog? cached) && cached is not null)
        {
            return cached;
        }

        var catalog = await LoadPlansAsync(cancellationToken);
        _cache.Set(cacheKey, catalog, catalog.IsComplete ? CompleteCatalogCacheDuration : PartialCatalogCacheDuration);
        return catalog;
    }

    private async Task<SubscribeOutcome> SubscribeCoreAsync(Subscriber subscriber, string planHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new BillingException(BillingFailureKind.InvalidRequest, "planHandle is required.");
        }

        var plan = await ResolvePlanAsync(planHandle.Trim(), cancellationToken);
        if (plan.RequiresPaymentMethod)
        {
            throw new BillingException(BillingFailureKind.InvalidRequest,
                $"Plan '{plan.Handle}' requires a payment method, which this storefront does not collect.");
        }

        var customerReference = CustomerReferenceFor(subscriber);
        var customerId = await EnsureCustomerAsync(subscriber, customerReference, cancellationToken);

        // Two passes at most: the second only runs after an ended subscription released the previous claim.
        for (var pass = 0; pass < 2; pass++)
        {
            var result = await _claims.AcquireAsync(
                new SubscriptionEnrollment(customerReference, plan.Handle, _claims.Now), ClaimLease, cancellationToken);
            var claim = result.Claim;

            switch (result.Outcome)
            {
                case ClaimOutcome.Busy:
                    throw new BillingException(BillingFailureKind.Conflict,
                        "A subscription to this plan is already being processed. Check your subscriptions shortly.");

                case ClaimOutcome.Existing:
                    var current = await ReadSubscriptionAsync(claim.ProviderSubscriptionId!.Value, cancellationToken);
                    if (current is not null && IsLive(current))
                    {
                        return new SubscribeOutcome(SubscribeStatus.AlreadySubscribed, Map(current), claim.SubscriptionReference);
                    }

                    // The enrolled subscription has ended (or is gone): free the slot for a new enrollment.
                    await _claims.ReleaseAsync(claim);
                    continue;

                case ClaimOutcome.TakenOver:
                    // An earlier attempt may already have reached Maxio with this reference: settle before creating.
                    Subscription? previous;
                    try
                    {
                        previous = await FindSubscriptionByReferenceAsync(claim.SubscriptionReference, cancellationToken);
                    }
                    catch (BillingException)
                    {
                        claim.MarkUnknown(_claims.Now);
                        await _claims.SaveAsync();
                        throw;
                    }

                    if (previous?.Id is int previousId)
                    {
                        claim.Activate(previousId, _claims.Now);
                        await _claims.SaveAsync();
                        return new SubscribeOutcome(SubscribeStatus.AlreadySubscribed, Map(previous), claim.SubscriptionReference);
                    }

                    break;
            }

            return await CreateEnrollmentAsync(claim, plan, customerId, cancellationToken);
        }

        throw new BillingException(BillingFailureKind.Conflict,
            "A subscription to this plan is already being processed. Check your subscriptions shortly.");
    }

    private async Task<IReadOnlyList<BillingSubscription>> GetSubscriptionsCoreAsync(Subscriber subscriber, CancellationToken cancellationToken)
    {
        var customerId = await TryReadCustomerIdByReferenceAsync(CustomerReferenceFor(subscriber), cancellationToken);
        if (customerId is null)
        {
            return Array.Empty<BillingSubscription>();
        }

        var subscriptions = await ListCustomerSubscriptionsAsync(customerId.Value, cancellationToken);
        return subscriptions
            .Where(IsInCatalogFamily)
            .Select(Map)
            .OrderByDescending(s => s.CreatedAt)
            .ToList();
    }

    public async Task<int> SettleUnresolvedClaimsAsync(CancellationToken cancellationToken = default)
    {
        var untouchedSince = _claims.Now - ClaimLease;
        var settled = 0;

        foreach (var claim in await _claims.ListUnresolvedAsync<SubscriptionEnrollment>(untouchedSince, SweepBatchSize, cancellationToken))
        {
            if (!await _claims.TryRenewAsync(claim, cancellationToken))
            {
                continue;
            }

            try
            {
                var found = await FindSubscriptionByReferenceAsync(claim.SubscriptionReference, cancellationToken);
                if (found?.Id is int subscriptionId)
                {
                    claim.Activate(subscriptionId, _claims.Now);
                    await _claims.SaveAsync();
                    _logger.LogInformation("Settled enrollment: Maxio subscription {SubscriptionId} exists for reference {Reference}.",
                        subscriptionId, claim.SubscriptionReference);
                }
                else
                {
                    // The lease is long past any in-flight create, so the absence is final.
                    await _claims.ReleaseAsync(claim);
                    _logger.LogInformation("Released enrollment: no Maxio subscription exists for reference {Reference}.",
                        claim.SubscriptionReference);
                }

                settled++;
            }
            catch (BillingException ex)
            {
                claim.MarkUnknown(_claims.Now);
                await _claims.SaveAsync();
                _logger.LogWarning(ex, "Could not settle enrollment {Reference} yet; will retry.", claim.SubscriptionReference);
            }
        }

        foreach (var claim in await _claims.ListUnresolvedAsync<BillingCustomer>(untouchedSince, SweepBatchSize, cancellationToken))
        {
            if (!await _claims.TryRenewAsync(claim, cancellationToken))
            {
                continue;
            }

            try
            {
                var customerId = await TryReadCustomerIdByReferenceAsync(claim.Id, cancellationToken);
                if (customerId is int id)
                {
                    claim.Activate(id, _claims.Now);
                    await _claims.SaveAsync();
                }
                else
                {
                    await _claims.ReleaseAsync(claim);
                }

                settled++;
            }
            catch (BillingException ex)
            {
                claim.MarkUnknown(_claims.Now);
                await _claims.SaveAsync();
                _logger.LogWarning(ex, "Could not settle billing customer claim yet; will retry.");
            }
        }

        return settled;
    }

    // ---------------------------------------------------------------- plans

    private async Task<SubscriptionPlanCatalog> LoadPlansAsync(CancellationToken cancellationToken)
    {
        var plans = new List<SubscriptionPlan>();

        // ListProductsForProductFamily is not an SDK-paged operation: walk Page/PerPage ourselves, bounded by MaxPlanPages.
        for (var page = 1; page <= MaxPlanPages; page++)
        {
            IReadOnlyList<ProductResponse> batch;
            try
            {
                batch = await Bounded(token => _client.ProductFamilies.ListProductsForProductFamily(
                    new ListProductsForProductFamilyRequest
                    {
                        ProductFamilyId = $"handle:{_productFamilyHandle}",
                        Page = page,
                        PerPage = PlanPageSize
                    },
                    cancellationToken: token), cancellationToken);
            }
            catch (ApiException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out _))
                {
                    // 404: the configured family does not exist on this site — a deployment fault, not the caller's.
                    _logger.LogError("Maxio product family configured as Maxio:ProductFamilyHandle was not found (HTTP {Status}).", (int)ex.StatusCode);
                    throw Unavailable("The subscription catalog is not available.", ex);
                }

                if (ex.Error.TryGetRawError(out RawError _))
                {
                    throw Translate(ex, "ListProductsForProductFamily");
                }

                throw Translate(ex, "ListProductsForProductFamily");
            }
            catch (Exception ex) when (IsSdkFailure(ex, cancellationToken))
            {
                throw Translate(ex, "ListProductsForProductFamily");
            }

            foreach (var product in batch.Select(r => r.Product))
            {
                if (product is null || string.IsNullOrWhiteSpace(product.Handle) || product.ArchivedAt is not null)
                {
                    continue;
                }

                plans.Add(new SubscriptionPlan(
                    product.Handle!,
                    product.Name ?? product.Handle!,
                    product.Description,
                    product.PriceInCents ?? 0,
                    product.Interval,
                    product.IntervalUnit?.Value,
                    product.RequireCreditCard ?? false));
            }

            if (batch.Count < PlanPageSize)
            {
                return new SubscriptionPlanCatalog(plans, IsComplete: true);
            }
        }

        _logger.LogWarning("Maxio plan catalog truncated at {MaxPages} pages of {PageSize}; the result is partial.", MaxPlanPages, PlanPageSize);
        return new SubscriptionPlanCatalog(plans, IsComplete: false);
    }

    /// <summary>Cross-operation invariant: the plan handle must be one the catalog read offers.</summary>
    private async Task<SubscriptionPlan> ResolvePlanAsync(string planHandle, CancellationToken cancellationToken)
    {
        var catalog = await GetPlansCoreAsync(cancellationToken);
        var plan = catalog.Plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is not null)
        {
            return plan;
        }

        if (!catalog.IsComplete)
        {
            throw new BillingException(BillingFailureKind.Unavailable,
                "The subscription catalog could not be loaded completely; please retry shortly.");
        }

        throw new BillingException(BillingFailureKind.InvalidRequest, $"'{planHandle}' is not an available subscription plan.");
    }

    // ---------------------------------------------------------------- customers

    private async Task<int> EnsureCustomerAsync(Subscriber subscriber, string reference, CancellationToken cancellationToken)
    {
        var result = await _claims.AcquireAsync(new BillingCustomer(reference, _claims.Now), ClaimLease, cancellationToken);
        var claim = result.Claim;

        if (result.Outcome == ClaimOutcome.Busy)
        {
            throw new BillingException(BillingFailureKind.Conflict,
                "Your billing account is being set up. Please retry in a moment.");
        }

        // Read by reference first in every case: it covers a customer created by an earlier run or another node,
        // a create whose outcome was unknown, and a recorded id that the site no longer holds.
        int? existingId;
        try
        {
            existingId = await TryReadCustomerIdByReferenceAsync(reference, cancellationToken);
        }
        catch when (claim.State != BillingClaimState.Active)
        {
            // Nothing was written: free the claim so the next attempt is not blocked for a whole lease.
            await _claims.ReleaseAsync(claim);
            throw;
        }

        if (existingId is int id)
        {
            if (claim.State != BillingClaimState.Active || claim.ProviderCustomerId != id)
            {
                claim.Activate(id, _claims.Now);
                await _claims.SaveAsync();
            }

            return id;
        }

        if (result.Outcome == ClaimOutcome.Existing && !await _claims.TryRenewAsync(claim, cancellationToken))
        {
            // Recorded customer is gone from the site and someone else is already recreating it.
            throw new BillingException(BillingFailureKind.Conflict,
                "Your billing account is being set up. Please retry in a moment.");
        }

        return await CreateCustomerAsync(claim, subscriber, reference);
    }

    private async Task<int> CreateCustomerAsync(BillingCustomer claim, Subscriber subscriber, string reference)
    {
        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = FirstNameFor(subscriber),
                LastName = "eShop customer",
                Email = subscriber.Email,
                Reference = reference
            }
        };

        try
        {
            // Writes are bounded by the call budget only — never by the caller's token — so an abandoned HTTP
            // request cannot cut a create off mid-flight after Maxio may have acted.
            var response = await Bounded(token => _client.Customers.CreateCustomer(
                new CreateCustomerOperationRequest { Body = body }, cancellationToken: token), CancellationToken.None);

            if (response.Customer?.Id is not int customerId)
            {
                return await SettleCustomerCreateAsync(claim, reference, cause: null);
            }

            claim.Activate(customerId, _claims.Now);
            await _claims.SaveAsync();
            _logger.LogInformation("Created Maxio customer {CustomerId} for billing claim.", customerId);
            return customerId;
        }
        catch (ApiException<CreateCustomerError> ex)
        {
            if ((int)ex.StatusCode >= 500)
            {
                return await SettleCustomerCreateAsync(claim, reference, ex);
            }

            if (ex.Error.TryGetCustomerErrorResponse1(out var validation))
            {
                // Customer references are unique at Maxio: a 422 can mean another node created it first.
                var concurrent = await TryReadCustomerIdOrNullAsync(reference);
                if (concurrent is int concurrentId)
                {
                    claim.Activate(concurrentId, _claims.Now);
                    await _claims.SaveAsync();
                    return concurrentId;
                }

                var errors = Describe(validation);
                _logger.LogWarning("Maxio rejected customer creation (HTTP {Status}): {Errors}", (int)ex.StatusCode, string.Join("; ", errors));
                await _claims.ReleaseAsync(claim);
                throw new BillingException(BillingFailureKind.Rejected, "The billing provider rejected the customer record.", errors, ex);
            }

            await _claims.ReleaseAsync(claim);
            if (ex.Error.TryGetRawError(out RawError _))
            {
                throw Translate(ex, "CreateCustomer");
            }

            throw Translate(ex, "CreateCustomer");
        }
        catch (ResponseDeserializationException ex) when (IsSuccess(ex.StatusCode))
        {
            return await SettleCustomerCreateAsync(claim, reference, ex);
        }
        catch (SdkConnectionException ex)
        {
            return await SettleCustomerCreateAsync(claim, reference, ex);
        }
        catch (OperationCanceledException ex)
        {
            return await SettleCustomerCreateAsync(claim, reference, ex);
        }
        catch (SdkException ex)
        {
            await _claims.ReleaseAsync(claim);
            throw Translate(ex, "CreateCustomer");
        }
    }

    /// <summary>Unknown outcome of CreateCustomer: re-read by the reference that was sent.</summary>
    private async Task<int> SettleCustomerCreateAsync(BillingCustomer claim, string reference, Exception? cause)
    {
        _logger.LogWarning(cause, "Maxio CreateCustomer outcome unknown; reconciling by customer reference.");

        int? found;
        try
        {
            found = await TryReadCustomerIdByReferenceAsync(reference, CancellationToken.None);
        }
        catch (BillingException lookupFailure)
        {
            claim.MarkUnknown(_claims.Now);
            await _claims.SaveAsync();
            _logger.LogError(lookupFailure, "Could not confirm Maxio customer creation; claim left for the reconciliation sweep.");
            throw new BillingException(BillingFailureKind.Unavailable,
                "Your billing account could not be confirmed yet; please retry shortly.", innerException: lookupFailure);
        }

        if (found is int customerId)
        {
            claim.Activate(customerId, _claims.Now);
            await _claims.SaveAsync();
            _logger.LogInformation("Reconciled Maxio customer {CustomerId} after an unknown create outcome.", customerId);
            return customerId;
        }

        // Customer references are unique at Maxio, so a later create cannot duplicate this one: release the claim.
        await _claims.ReleaseAsync(claim);
        throw Unavailable("The billing provider could not be reached; please retry shortly.", cause);
    }

    /// <returns>The Maxio customer id, or null when Maxio holds no customer with this reference (404).</returns>
    private async Task<int?> TryReadCustomerIdByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        CustomerResponse response;
        try
        {
            response = await Bounded(token => _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = reference }, cancellationToken: token), cancellationToken);
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex) when (IsSdkFailure(ex, cancellationToken))
        {
            throw Translate(ex, "ReadCustomerByReference");
        }

        // A 2xx without an id is unreadable, not "no customer" — never turn it into an absence (it would trigger a create).
        return response.Customer?.Id ?? throw Unavailable("The billing provider returned an unreadable customer record.", null);
    }

    private async Task<int?> TryReadCustomerIdOrNullAsync(string reference)
    {
        try
        {
            return await TryReadCustomerIdByReferenceAsync(reference, CancellationToken.None);
        }
        catch (BillingException)
        {
            return null;
        }
    }

    // ---------------------------------------------------------------- subscriptions

    private async Task<SubscribeOutcome> CreateEnrollmentAsync(SubscriptionEnrollment claim, SubscriptionPlan plan, int customerId, CancellationToken cancellationToken)
    {
        CollectionMethod collectionMethod;
        try
        {
            // A live subscription to this plan created outside this claim (an earlier run whose local store was lost,
            // the Maxio UI, …) satisfies the request without creating a second one.
            var live = (await ListCustomerSubscriptionsAsync(customerId, cancellationToken))
                .FirstOrDefault(s => IsLive(s) && string.Equals(s.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase));
            if (live?.Id is int liveId)
            {
                claim.Activate(liveId, _claims.Now);
                await _claims.SaveAsync();
                return new SubscribeOutcome(SubscribeStatus.AlreadySubscribed, Map(live), live.Reference);
            }

            collectionMethod = await ResolveInvoicedCollectionMethodAsync(cancellationToken);
        }
        catch
        {
            // Nothing was written: free the claim.
            await _claims.ReleaseAsync(claim);
            throw;
        }

        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = plan.Handle,
                CustomerId = customerId,
                Reference = claim.SubscriptionReference,
                // The storefront captures no payment method, so signup and renewals are invoiced rather than charged.
                PaymentCollectionMethod = collectionMethod
            }
        };

        try
        {
            var response = await Bounded(token => _client.Subscriptions.CreateSubscription(
                new CreateSubscriptionOperationRequest { Body = body }, cancellationToken: token), CancellationToken.None);

            if (response.Subscription?.Id is not int subscriptionId)
            {
                return await SettleSubscriptionCreateAsync(claim, cause: null);
            }

            claim.Activate(subscriptionId, _claims.Now);
            await _claims.SaveAsync();
            _logger.LogInformation("Created Maxio subscription {SubscriptionId} (reference {Reference}) on plan {Plan}.",
                subscriptionId, claim.SubscriptionReference, plan.Handle);
            return new SubscribeOutcome(SubscribeStatus.Created, Map(response.Subscription), claim.SubscriptionReference);
        }
        catch (ApiException<CreateSubscriptionError> ex)
        {
            if ((int)ex.StatusCode >= 500)
            {
                return await SettleSubscriptionCreateAsync(claim, ex);
            }

            await _claims.ReleaseAsync(claim);
            if (ex.Error.TryGetErrorListResponse1(out var validation))
            {
                _logger.LogWarning("Maxio rejected subscription {Reference} on plan {Plan} (HTTP {Status}): {Errors}",
                    claim.SubscriptionReference, plan.Handle, (int)ex.StatusCode, string.Join("; ", validation.Errors));
                throw new BillingException(BillingFailureKind.Rejected, "The billing provider rejected the subscription.", validation.Errors, ex);
            }

            if (ex.Error.TryGetRawError(out RawError _))
            {
                throw Translate(ex, "CreateSubscription");
            }

            throw Translate(ex, "CreateSubscription");
        }
        catch (ResponseDeserializationException ex) when (IsSuccess(ex.StatusCode))
        {
            return await SettleSubscriptionCreateAsync(claim, ex);
        }
        catch (SdkConnectionException ex)
        {
            return await SettleSubscriptionCreateAsync(claim, ex);
        }
        catch (OperationCanceledException ex)
        {
            return await SettleSubscriptionCreateAsync(claim, ex);
        }
        catch (SdkException ex)
        {
            await _claims.ReleaseAsync(claim);
            throw Translate(ex, "CreateSubscription");
        }
    }

    /// <summary>
    /// Unknown outcome of CreateSubscription: re-read by the reference that was sent. If it cannot be confirmed now,
    /// the claim is kept as <see cref="BillingClaimState.Unknown"/> (blocking re-sends) and the sweeper settles it.
    /// </summary>
    private async Task<SubscribeOutcome> SettleSubscriptionCreateAsync(SubscriptionEnrollment claim, Exception? cause)
    {
        _logger.LogWarning(cause, "Maxio CreateSubscription outcome unknown for reference {Reference}; reconciling.", claim.SubscriptionReference);

        Subscription? found = null;
        try
        {
            found = await FindSubscriptionByReferenceAsync(claim.SubscriptionReference, CancellationToken.None);
        }
        catch (BillingException lookupFailure)
        {
            _logger.LogError(lookupFailure, "Could not look up subscription reference {Reference}.", claim.SubscriptionReference);
        }

        if (found?.Id is int subscriptionId)
        {
            claim.Activate(subscriptionId, _claims.Now);
            await _claims.SaveAsync();
            _logger.LogInformation("Reconciled Maxio subscription {SubscriptionId} for reference {Reference}.", subscriptionId, claim.SubscriptionReference);
            return new SubscribeOutcome(SubscribeStatus.Created, Map(found), claim.SubscriptionReference);
        }

        // Not visible (yet) or lookup failed: the create may still be landing. Keep the claim so nothing re-sends,
        // and let SettleUnresolvedClaimsAsync decide once the lease has passed.
        claim.MarkUnknown(_claims.Now);
        await _claims.SaveAsync();
        return new SubscribeOutcome(SubscribeStatus.Pending, null, claim.SubscriptionReference);
    }

    /// <summary>
    /// The non-card collection method valid for this site: <c>remittance</c> on Relationship Invoicing sites,
    /// <c>invoice</c> on legacy Statements sites (per the SDK's <see cref="CreateSubscription.PaymentCollectionMethod"/> docs).
    /// </summary>
    private async Task<CollectionMethod> ResolveInvoicedCollectionMethodAsync(CancellationToken cancellationToken)
    {
        const string cacheKey = "maxio-site:invoiced-collection-method";
        if (_cache.TryGetValue(cacheKey, out CollectionMethod? cached) && cached is not null)
        {
            return cached;
        }

        SiteResponse response;
        try
        {
            response = await Bounded(token => _client.Sites.ReadSite(cancellationToken: token), cancellationToken);
        }
        catch (Exception ex) when (IsSdkFailure(ex, cancellationToken))
        {
            throw Translate(ex, "ReadSite");
        }

        // Absent flag: assume the current (Relationship Invoicing) architecture.
        var method = response.Site.RelationshipInvoicingEnabled == false ? CollectionMethod.Invoice : CollectionMethod.Remittance;
        _cache.Set(cacheKey, method, SiteSettingsCacheDuration);
        return method;
    }

    /// <returns>The subscription, or null when Maxio has none with this reference (404).</returns>
    private async Task<Subscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        SubscriptionResponse response;
        try
        {
            response = await Bounded(token => _client.Subscriptions.FindSubscription(
                new FindSubscriptionRequest { Reference = reference }, cancellationToken: token), cancellationToken);
        }
        catch (ApiException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out RawError _))
            {
                return null;
            }

            if (ex.Error.TryGetRawError(out RawError _))
            {
                throw Translate(ex, "FindSubscription");
            }

            throw Translate(ex, "FindSubscription");
        }
        catch (Exception ex) when (IsSdkFailure(ex, cancellationToken))
        {
            throw Translate(ex, "FindSubscription");
        }

        return response.Subscription ?? throw Unavailable("The billing provider returned an unreadable subscription.", null);
    }

    /// <returns>The subscription, or null when Maxio no longer has it (404).</returns>
    private async Task<Subscription?> ReadSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken)
    {
        SubscriptionResponse response;
        try
        {
            response = await Bounded(token => _client.Subscriptions.ReadSubscription(
                new ReadSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: token), cancellationToken);
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex) when (IsSdkFailure(ex, cancellationToken))
        {
            throw Translate(ex, "ReadSubscription");
        }

        return response.Subscription ?? throw Unavailable("The billing provider returned an unreadable subscription.", null);
    }

    private async Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        IReadOnlyList<SubscriptionResponse> responses;
        try
        {
            responses = await Bounded(token => _client.Customers.ListCustomerSubscriptions(
                new ListCustomerSubscriptionsRequest { CustomerId = customerId }, cancellationToken: token), cancellationToken);
        }
        catch (Exception ex) when (IsSdkFailure(ex, cancellationToken))
        {
            throw Translate(ex, "ListCustomerSubscriptions");
        }

        return responses
            .Select(r => r.Subscription)
            .Where(s => s?.Id is not null)
            .Select(s => s!)
            .ToList();
    }

    // ---------------------------------------------------------------- helpers

    private async Task<T> WithinOperationBudget<T>(Func<CancellationToken, Task<T>> operation, CancellationToken callerToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        deadline.CancelAfter(OperationBudget);
        try
        {
            return await operation(deadline.Token);
        }
        catch (OperationCanceledException ex) when (!callerToken.IsCancellationRequested)
        {
            // Our deadline, not the caller going away.
            _logger.LogError("Billing operation exceeded its {Budget} budget.", OperationBudget);
            throw Unavailable("The billing provider did not respond in time; please retry shortly.", ex);
        }
    }

    private static async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(CallBudget);
        return await call(budget.Token);
    }

    /// <summary>An SDK failure, or our own call budget firing — but never the caller's cancellation, which must propagate.</summary>
    private static bool IsSdkFailure(Exception ex, CancellationToken callerToken) =>
        ex is SdkException || (ex is OperationCanceledException && !callerToken.IsCancellationRequested);

    private static bool IsSuccess(HttpStatusCode status) => (int)status is >= 200 and < 300;

    private BillingException Translate(Exception ex, string operation)
    {
        switch (ex)
        {
            case ResponseDeserializationException rde:
                _logger.LogError(rde, "Maxio {Operation} returned HTTP {Status} with a body that did not match {TargetType}.",
                    operation, (int)rde.StatusCode, rde.TargetType.Name);
                return IsSuccess(rde.StatusCode) || (int)rde.StatusCode >= 500
                    ? Unavailable("The billing provider returned a response that could not be processed.", rde)
                    : new BillingException(BillingFailureKind.Rejected, "The billing provider rejected the request.", innerException: rde);

            case ApiException api:
                var status = (int)api.StatusCode;
                if (status is 401 or 403)
                {
                    // Our credentials, not the caller's: never surface as 401/403.
                    _logger.LogError("Maxio {Operation} refused our credentials (HTTP {Status}). Check Maxio:ApiKey / Maxio:Subdomain.", operation, status);
                    return Unavailable("The billing provider is not available.", api);
                }

                if (status is 400 or 409 or 422)
                {
                    _logger.LogWarning("Maxio {Operation} rejected the request (HTTP {Status}).", operation, status);
                    return new BillingException(BillingFailureKind.Rejected, "The billing provider rejected the request.", innerException: api);
                }

                _logger.LogError("Maxio {Operation} failed with HTTP {Status}.", operation, status);
                return Unavailable("The billing provider is not available; please retry shortly.", api);

            case SdkTimeoutException timeout:
                _logger.LogError(timeout, "Maxio {Operation} did not answer within {Timeout}.", operation, timeout.Timeout);
                return Unavailable("The billing provider did not respond in time; please retry shortly.", timeout);

            case SdkConnectionException connection:
                _logger.LogError(connection, "Maxio {Operation} could not be reached.", operation);
                return Unavailable("The billing provider could not be reached; please retry shortly.", connection);

            case AuthSchemeException auth:
                _logger.LogError(auth, "Maxio {Operation}: credentials could not be applied.", operation);
                return Unavailable("The billing provider is not available.", auth);

            case OperationCanceledException canceled:
                _logger.LogError("Maxio {Operation} exceeded the {Budget} call budget.", operation, CallBudget);
                return Unavailable("The billing provider did not respond in time; please retry shortly.", canceled);

            default:
                _logger.LogError(ex, "Maxio {Operation} failed unexpectedly.", operation);
                return Unavailable("The billing provider is not available.", ex);
        }
    }

    private static BillingException Unavailable(string message, Exception? inner) =>
        new(BillingFailureKind.Unavailable, message, innerException: inner);

    private static IReadOnlyList<string> Describe(CustomerErrorResponse1 validation)
    {
        if (validation.Errors is { } errors)
        {
            if (errors.TryGetListOfString(out var messages))
            {
                return messages;
            }

            if (errors.TryGetCustomerError(out var customerError) && !string.IsNullOrWhiteSpace(customerError.Customer))
            {
                return new[] { customerError.Customer! };
            }
        }

        return Array.Empty<string>();
    }

    private static bool IsLive(Subscription subscription) =>
        subscription.State is null || !EndedStates.Contains(subscription.State);

    private bool IsInCatalogFamily(Subscription subscription) =>
        subscription.Product is { } product
        && (product.ProductFamily?.Handle is null
            || string.Equals(product.ProductFamily.Handle, _productFamilyHandle, StringComparison.OrdinalIgnoreCase));

    private static BillingSubscription Map(Subscription s) => new(
        s.Id ?? 0,
        s.Reference,
        s.Product?.Handle,
        s.Product?.Name,
        s.ProductPriceInCents ?? s.Product?.PriceInCents,
        s.Currency,
        s.State?.Value ?? "unknown",
        // next_assessment_at is when the next charge will actually be attempted; it tracks current_period_ends_at
        // except while a failed renewal is being retried.
        s.NextAssessmentAt ?? s.CurrentPeriodEndsAt,
        s.CurrentPeriodEndsAt,
        s.CreatedAt);

    /// <summary>
    /// Stable billing identity for an eShop user. The JWT carries the user name (the only stable identifier:
    /// identity ids are regenerated when the in-memory store is used), so the reference is derived from it.
    /// </summary>
    public static string CustomerReferenceFor(Subscriber subscriber)
    {
        if (string.IsNullOrWhiteSpace(subscriber.UserName))
        {
            throw new BillingException(BillingFailureKind.InvalidRequest, "The caller has no user name.");
        }

        var reference = CustomerReferencePrefix + subscriber.UserName.Trim().ToLowerInvariant();
        if (reference.Length > BillingClaimConstraints.CustomerReferenceMaxLength)
        {
            throw new BillingException(BillingFailureKind.InvalidRequest, "The user name is too long for a billing account.");
        }

        return reference;
    }

    private static string FirstNameFor(Subscriber subscriber)
    {
        var localPart = subscriber.Email.Split('@')[0].Trim();
        return string.IsNullOrEmpty(localPart) ? "eShop" : localPart;
    }
}
