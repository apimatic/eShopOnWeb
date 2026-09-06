using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Subscription billing backed by Maxio Advanced Billing.
/// </summary>
/// <remarks>
/// <para>
/// This is the integration boundary: every Maxio failure - a rejected request, an unreachable host, a body
/// that cannot be read - leaves this class as a <see cref="BillingException"/> carrying a caller-safe message
/// and, where one was observed, the provider status. Nothing above this layer sees an SDK type.
/// </para>
/// <para>
/// Only handles cross this boundary. Maxio reassigns numeric ids when a catalog is re-seeded, so the numeric
/// product-family id is resolved from its handle on demand, cached briefly, and dropped and re-resolved the
/// moment Maxio says it no longer exists.
/// </para>
/// </remarks>
public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const int PageSize = 100;
    private const int MaxPages = 50;

    /// <summary>How long a reconciliation read may take after a write whose outcome is unknown.</summary>
    private static readonly TimeSpan ReconcileBudget = TimeSpan.FromSeconds(10);

    /// <summary>The states in which a subscription no longer entitles the shopper to anything.</summary>
    private static readonly SubscriptionState[] TerminalStates =
    {
        SubscriptionState.Canceled,
        SubscriptionState.Expired,
        SubscriptionState.FailedToCreate
    };

    private readonly MaxioClientAccessor _accessor;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;
    private readonly KeyedAsyncLock _customerLocks = new();
    private readonly TimedCache<string> _productFamilyIdCache;
    private readonly TimedCache<MaxioSiteInfo> _siteCache;

    public MaxioSubscriptionBillingService(
        MaxioClientAccessor accessor,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _accessor = accessor;
        _settings = settings.Value;
        _logger = logger;

        var cacheLifetime = TimeSpan.FromSeconds(Math.Max(1, _settings.CatalogCacheSeconds));
        _productFamilyIdCache = new TimedCache<string>(cacheLifetime);
        _siteCache = new TimedCache<MaxioSiteInfo>(cacheLifetime);
    }

    private MaxioAdvancedBillingClient Client => _accessor.Client;

    public Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default) =>
        WithCallBudgetAsync(GetPlansCoreAsync, cancellationToken);

    public Task<SubscribeResult> SubscribeAsync(BillingCustomerIdentity identity, string planHandle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);

        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new BillingException(BillingFailureKind.Validation, "A plan handle is required.");
        }

        return WithCallBudgetAsync(ct => SubscribeCoreAsync(identity, planHandle.Trim(), ct), cancellationToken);
    }

    public Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(
        BillingCustomerIdentity identity,
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);

        return WithCallBudgetAsync(ct => GetSubscriptionsCoreAsync(identity, includeInactive, ct), cancellationToken);
    }

    // ---------------------------------------------------------------------------------------------------
    // Flows
    // ---------------------------------------------------------------------------------------------------

    private async Task<IReadOnlyList<SubscriptionPlan>> GetPlansCoreAsync(CancellationToken ct)
    {
        var site = await TryGetSiteAsync(ct).ConfigureAwait(false);
        var products = await ListProductsAsync(allowFamilyRefresh: true, ct).ConfigureAwait(false);

        return products
            .Select(response => response.Product)
            .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
            .Select(product => MapPlan(product, site?.Currency))
            .OrderBy(plan => plan.PriceInCents)
            .ToList();
    }

    private async Task<SubscribeResult> SubscribeCoreAsync(BillingCustomerIdentity identity, string planHandle, CancellationToken ct)
    {
        // Serialize per shopper so two clicks cannot both pass the "not subscribed yet" check.
        using var _ = await _customerLocks.AcquireAsync(identity.CustomerReference, ct).ConfigureAwait(false);

        var product = await ReadProductByHandleAsync(planHandle, ct).ConfigureAwait(false);

        if (product.RequireCreditCard == true)
        {
            // A necessary filter, though not a sufficient one - see ResolvePaymentCollectionMethod. This API
            // captures no card and runs no 3-D Secure flow, so a plan that demands one cannot be served here.
            throw new BillingException(
                BillingFailureKind.Validation,
                $"Plan '{planHandle}' requires a payment method, which this API does not collect.");
        }

        var site = await GetSiteAsync(ct).ConfigureAwait(false);
        var paymentCollectionMethod = ResolvePaymentCollectionMethod(site, planHandle);

        var customer = await EnsureCustomerAsync(identity, ct).ConfigureAwait(false);

        if (customer.Id is not { } customerId)
        {
            throw new BillingException(
                BillingFailureKind.UnreadableResponse,
                "Maxio returned a customer record without an identifier.");
        }

        var reference = identity.SubscriptionReference(planHandle);
        var existing = await FindExistingSubscriptionAsync(customerId, planHandle, reference, ct).ConfigureAwait(false);

        if (existing is not null)
        {
            _logger.LogInformation(
                "Shopper {Reference} is already subscribed to plan {Plan} (subscription {SubscriptionId}); returning the existing subscription.",
                identity.CustomerReference, planHandle, existing.Id);

            return new SubscribeResult(existing, AlreadySubscribed: true);
        }

        var created = await CreateSubscriptionAsync(customerId, planHandle, reference, paymentCollectionMethod, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Subscribed shopper {Reference} to plan {Plan} (subscription {SubscriptionId}, state {State}).",
            identity.CustomerReference, product.Handle, created.Id, created.State);

        return new SubscribeResult(created, AlreadySubscribed: false);
    }

    private async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsCoreAsync(
        BillingCustomerIdentity identity,
        bool includeInactive,
        CancellationToken ct)
    {
        var customer = await ReadCustomerByReferenceAsync(identity.CustomerReference, ct).ConfigureAwait(false);

        if (customer?.Id is not { } customerId)
        {
            // A shopper who has never subscribed simply has no billing customer yet.
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, ct).ConfigureAwait(false);

        return subscriptions
            .Where(subscription => includeInactive || subscription.IsActive)
            .OrderByDescending(subscription => subscription.CurrentPeriodStartedAt ?? DateTimeOffset.MinValue)
            .ToList();
    }

    // ---------------------------------------------------------------------------------------------------
    // Catalog
    // ---------------------------------------------------------------------------------------------------

    private async Task<IReadOnlyList<ProductResponse>> ListProductsAsync(bool allowFamilyRefresh, CancellationToken ct)
    {
        var productFamilyId = await _productFamilyIdCache
            .GetAsync(ResolveProductFamilyIdAsync, ct)
            .ConfigureAwait(false);

        try
        {
            return await ListProductsForFamilyAsync(productFamilyId, ct).ConfigureAwait(false);
        }
        catch (BillingException ex) when (allowFamilyRefresh && ex.ProviderStatusCode == (int)HttpStatusCode.NotFound)
        {
            // The cached numeric id no longer exists - Maxio reassigns ids when a catalog is re-seeded.
            // Drop it, resolve the handle again, and retry exactly once.
            _logger.LogInformation(
                "Maxio no longer recognises product family id {ProductFamilyId}; re-resolving handle '{Handle}'.",
                productFamilyId, _settings.ProductFamilyHandle);

            _productFamilyIdCache.Invalidate();
            return await ListProductsAsync(allowFamilyRefresh: false, ct).ConfigureAwait(false);
        }
    }

    private async Task<IReadOnlyList<ProductResponse>> ListProductsForFamilyAsync(string productFamilyId, CancellationToken ct)
    {
        var all = new List<ProductResponse>();

        for (var page = 1; page <= MaxPages; page++)
        {
            IReadOnlyList<ProductResponse> pageItems;

            try
            {
                pageItems = await Client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: productFamilyId,
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: false,
                    include: null,
                    page: page,
                    perPage: PageSize,
                    ct: ct).ConfigureAwait(false);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out var notFound))
                {
                    _logger.LogWarning("Maxio could not list products for product family {ProductFamilyId}: {Detail}",
                        productFamilyId, notFound);

                    throw new BillingException(
                        BillingFailureKind.Misconfigured,
                        "The configured Maxio product family could not be read.",
                        (int)HttpStatusCode.NotFound,
                        ex);
                }

                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw FromProviderError("list products for product family", raw, ex);
                }

                throw new BillingException(
                    BillingFailureKind.Unavailable,
                    "Maxio rejected the request for the plan catalog.",
                    innerException: ex);
            }
            catch (JsonException ex)
            {
                throw FromUnreadableBody("list products for product family", scope: null, ex);
            }
            catch (Exception ex) when (IsTransportFailure(ex))
            {
                throw FromTransportFailure("list products for product family", ex);
            }

            all.AddRange(pageItems);

            if (pageItems.Count < PageSize)
            {
                return all;
            }
        }

        _logger.LogWarning("Stopped reading the Maxio plan catalog after {MaxPages} pages.", MaxPages);
        return all;
    }

    private async Task<string> ResolveProductFamilyIdAsync(CancellationToken ct)
    {
        var handle = _settings.ProductFamilyHandle!;
        IReadOnlyList<ProductFamilyResponse> families;

        try
        {
            families = await Client.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: ct).ConfigureAwait(false);
        }
        catch (SdkException<RawError> ex)
        {
            throw FromProviderError("list product families", ex.Error, ex);
        }
        catch (JsonException ex)
        {
            throw FromUnreadableBody("list product families", scope: null, ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            throw FromTransportFailure("list product families", ex);
        }

        var match = families
            .Select(response => response.ProductFamily)
            .FirstOrDefault(family => string.Equals(family?.Handle, handle, StringComparison.OrdinalIgnoreCase));

        if (match?.Id is not { } id)
        {
            throw new BillingException(
                BillingFailureKind.Misconfigured,
                $"No product family with handle '{handle}' exists on the configured Maxio site.");
        }

        return id.ToString(CultureInfo.InvariantCulture);
    }

    private async Task<Product> ReadProductByHandleAsync(string planHandle, CancellationToken ct)
    {
        try
        {
            var response = await Client.Products.ReadProductByHandle(planHandle, ct).ConfigureAwait(false);
            return response.Product;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            throw new BillingException(
                BillingFailureKind.PlanNotFound,
                $"No subscription plan with handle '{planHandle}' exists.",
                (int)HttpStatusCode.NotFound,
                ex);
        }
        catch (SdkException<RawError> ex)
        {
            throw FromProviderError("read product by handle", ex.Error, ex);
        }
        catch (JsonException ex)
        {
            throw FromUnreadableBody("read product by handle", scope: null, ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            throw FromTransportFailure("read product by handle", ex);
        }
    }

    private Task<MaxioSiteInfo> GetSiteAsync(CancellationToken ct) =>
        _siteCache.GetAsync(ReadSiteAsync, ct);

    private async Task<MaxioSiteInfo?> TryGetSiteAsync(CancellationToken ct)
    {
        try
        {
            return await GetSiteAsync(ct).ConfigureAwait(false);
        }
        catch (BillingException ex)
        {
            // On the catalog path the site is read only for its currency, which is presentation detail.
            // Losing it must not take the plan list down with it. The subscribe path calls GetSiteAsync
            // instead, because there the site decides how the subscription is billed.
            _logger.LogWarning(ex, "Could not read the Maxio site; plans will be returned without a currency.");
            return null;
        }
    }

    private async Task<MaxioSiteInfo> ReadSiteAsync(CancellationToken ct)
    {
        try
        {
            var response = await Client.Sites.ReadSite(ct).ConfigureAwait(false);

            return new MaxioSiteInfo(
                response.Site.Currency,
                response.Site.RelationshipInvoicingEnabled,
                response.Site.DefaultPaymentCollectionMethod);
        }
        catch (SdkException<RawError> ex)
        {
            throw FromProviderError("read site", ex.Error, ex);
        }
        catch (JsonException ex)
        {
            throw FromUnreadableBody("read site", scope: null, ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            throw FromTransportFailure("read site", ex);
        }
    }

    // ---------------------------------------------------------------------------------------------------
    // Customers
    // ---------------------------------------------------------------------------------------------------

    private async Task<Customer> EnsureCustomerAsync(BillingCustomerIdentity identity, CancellationToken ct)
    {
        var existing = await ReadCustomerByReferenceAsync(identity.CustomerReference, ct).ConfigureAwait(false);

        if (existing is not null)
        {
            return existing;
        }

        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = identity.FirstName,
                LastName = identity.LastName,
                Email = identity.Email,
                Reference = identity.CustomerReference
            }
        };

        Customer? created = null;
        Exception? unknownOutcome = null;

        using (var scope = MaxioCallScope.Begin(writeOnce: true))
        {
            try
            {
                var response = await Client.Customers.CreateCustomer(body, ct).ConfigureAwait(false);
                created = response.Customer;
            }
            catch (SdkException<CreateCustomerError> ex)
            {
                if (ex.Error.TryGetCustomerErrorResponse1(out var validation))
                {
                    // The generated 422 shape models pagination and price-point fields, so it is not a
                    // reliable source of a human-readable message. Log the raw body too, always.
                    var detail = DescribeCustomerValidation(validation);

                    _logger.LogWarning(
                        "Maxio rejected the customer create for reference {Reference} with 422: {Detail}",
                        identity.CustomerReference, detail);

                    // A duplicate reference is the shape a lost-response retry takes: re-read before failing.
                    var raced = await ReconcileCustomerAsync(identity.CustomerReference).ConfigureAwait(false);

                    if (raced is not null)
                    {
                        return raced;
                    }

                    throw new BillingException(
                        BillingFailureKind.Validation,
                        $"Maxio rejected the billing customer for this account. {detail}".TrimEnd(),
                        (int)HttpStatusCode.UnprocessableEntity,
                        ex);
                }

                if (ex.Error.TryGetRawError(out var raw))
                {
                    _logger.LogWarning("Maxio returned {Status} creating a customer: {Body}",
                        (int)raw.StatusCode, SafeReadBody(raw));

                    throw FromProviderError("create customer", raw, ex);
                }

                throw new BillingException(
                    BillingFailureKind.Unavailable,
                    "Maxio rejected the billing customer for an unknown reason.",
                    innerException: ex);
            }
            catch (JsonException ex)
            {
                throw FromUnreadableBody("create customer", scope, ex);
            }
            catch (Exception ex) when (IsWriteBlocked(ex) || IsTransportFailure(ex))
            {
                unknownOutcome = ex;
            }
        }

        if (unknownOutcome is not null)
        {
            _logger.LogWarning(unknownOutcome,
                "The customer create for reference {Reference} produced no readable outcome; reconciling against Maxio.",
                identity.CustomerReference);

            var reconciled = await ReconcileCustomerAsync(identity.CustomerReference).ConfigureAwait(false);

            if (reconciled is not null)
            {
                return reconciled;
            }

            throw new BillingException(
                BillingFailureKind.UnknownOutcome,
                "The billing customer could not be confirmed. Please try again in a moment.",
                innerException: unknownOutcome);
        }

        if (created is null)
        {
            throw new BillingException(
                BillingFailureKind.UnreadableResponse,
                "Maxio accepted the billing customer but returned no customer record.");
        }

        _logger.LogInformation("Created Maxio customer {CustomerId} for reference {Reference}.",
            created.Id, identity.CustomerReference);

        return created;
    }

    /// <summary>
    /// Re-reads the customer after a write whose outcome is unknown, on its own short budget so it still runs
    /// when the original call's budget is what expired.
    /// </summary>
    private async Task<Customer?> ReconcileCustomerAsync(string customerReference)
    {
        using var cts = new CancellationTokenSource(ReconcileBudget);

        try
        {
            return await ReadCustomerByReferenceAsync(customerReference, cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is BillingException or OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not reconcile the billing customer for reference {Reference}.", customerReference);
            return null;
        }
    }

    private async Task<Customer?> ReadCustomerByReferenceAsync(string customerReference, CancellationToken ct)
    {
        try
        {
            var response = await Client.Customers.ReadCustomerByReference(customerReference, ct).ConfigureAwait(false);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            // Not an error path: this is how Maxio reports "no customer with that reference", and it is the
            // normal branch the first time a shopper subscribes.
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw FromProviderError("read customer by reference", ex.Error, ex);
        }
        catch (JsonException ex)
        {
            // "I could not read the answer" is not "there is no such customer" - never let a corrupt body
            // look like an absence here, because this lookup gates a create.
            throw FromUnreadableBody("read customer by reference", scope: null, ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            throw FromTransportFailure("read customer by reference", ex);
        }
    }

    // ---------------------------------------------------------------------------------------------------
    // Subscriptions
    // ---------------------------------------------------------------------------------------------------

    private async Task<CustomerSubscription?> FindExistingSubscriptionAsync(
        int customerId,
        string planHandle,
        string subscriptionReference,
        CancellationToken ct)
    {
        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, ct).ConfigureAwait(false);

        return subscriptions.FirstOrDefault(subscription =>
            subscription.IsActive
            && (string.Equals(subscription.Reference, subscriptionReference, StringComparison.Ordinal)
                || string.Equals(subscription.PlanHandle, planHandle, StringComparison.OrdinalIgnoreCase)));
    }

    private async Task<IReadOnlyList<CustomerSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct)
    {
        try
        {
            var responses = await Client.Customers.ListCustomerSubscriptions(customerId, ct).ConfigureAwait(false);

            return responses
                .Select(response => response.Subscription)
                .Where(subscription => subscription is not null)
                .Select(subscription => MapSubscription(subscription!))
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw FromProviderError("list customer subscriptions", ex.Error, ex);
        }
        catch (JsonException ex)
        {
            throw FromUnreadableBody("list customer subscriptions", scope: null, ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            throw FromTransportFailure("list customer subscriptions", ex);
        }
    }

    /// <summary>
    /// Chooses how the subscription's balance is collected.
    /// </summary>
    /// <remarks>
    /// This is not optional plumbing. Leaving the collection method unset applies the site default, and on a
    /// site defaulting to automatic collection Maxio tries to charge the signup balance immediately and
    /// rejects the whole creation when no payment profile is on file - which it does even for a plan whose
    /// own "requires a credit card" flag is false. Since this API captures no card, the subscription must
    /// explicitly ask to be billed rather than charged. Which value means that is decided by the site's
    /// architecture: Relationship Invoicing sites accept <c>remittance</c>, legacy Statements sites accept
    /// <c>invoice</c>, and the two are not interchangeable.
    /// </remarks>
    private CollectionMethod ResolvePaymentCollectionMethod(MaxioSiteInfo site, string planHandle)
    {
        if (site.RelationshipInvoicingEnabled == true)
        {
            return CollectionMethod.Remittance;
        }

        if (site.RelationshipInvoicingEnabled == false)
        {
            return CollectionMethod.Invoice;
        }

        // The site did not say which architecture it runs. Its own default is the only other evidence, and it
        // is only useful if it is not the automatic collection that caused the problem in the first place.
        if (!string.IsNullOrWhiteSpace(site.DefaultPaymentCollectionMethod))
        {
            var siteDefault = CollectionMethod.FromValue(site.DefaultPaymentCollectionMethod);

            if (siteDefault != CollectionMethod.Automatic)
            {
                _logger.LogInformation(
                    "Maxio did not report its billing architecture; falling back to the site default collection method '{Method}'.",
                    site.DefaultPaymentCollectionMethod);

                return siteDefault;
            }
        }

        throw new BillingException(
            BillingFailureKind.Misconfigured,
            $"Plan '{planHandle}' cannot be subscribed to without a payment method on this Maxio site.");
    }

    private async Task<CustomerSubscription> CreateSubscriptionAsync(
        int customerId,
        string planHandle,
        string subscriptionReference,
        CollectionMethod paymentCollectionMethod,
        CancellationToken ct)
    {
        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                // Handles, never ids: Maxio reassigns numeric ids when a catalog is re-seeded.
                ProductHandle = planHandle,
                CustomerId = customerId,

                // The subscription's own application-supplied key. NOT 'Ref', which is a referral code whose
                // invalid value fails creation outright.
                Reference = subscriptionReference,

                // Bill the shopper rather than charge a card, because there is no card. See
                // ResolvePaymentCollectionMethod for why omitting this is not an option.
                PaymentCollectionMethod = paymentCollectionMethod

                // No payment-profile fields: unset optional members are omitted from the request body
                // entirely rather than sent as null.
            }
        };

        Subscription? created = null;
        Exception? unknownOutcome = null;

        using (var scope = MaxioCallScope.Begin(writeOnce: true))
        {
            try
            {
                var response = await Client.Subscriptions.CreateSubscription(body, ct).ConfigureAwait(false);
                created = response.Subscription;
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                if (ex.Error.TryGetErrorListResponse1(out var errors))
                {
                    var detail = errors?.Errors is { Count: > 0 } messages
                        ? string.Join("; ", messages)
                        : "Maxio gave no detail.";

                    // Logged in full so that a provider-side rejection (for example a site that does require a
                    // payment method) is diagnosable rather than an opaque 422.
                    _logger.LogWarning(
                        "Maxio rejected the subscription to plan {Plan} for customer {CustomerId} with 422: {Detail}",
                        planHandle, customerId, detail);

                    throw new BillingException(
                        BillingFailureKind.Validation,
                        $"Maxio rejected this subscription: {detail}",
                        (int)HttpStatusCode.UnprocessableEntity,
                        ex);
                }

                if (ex.Error.TryGetRawError(out var raw))
                {
                    _logger.LogWarning(
                        "Maxio returned {Status} creating a subscription to plan {Plan}: {Body}",
                        (int)raw.StatusCode, planHandle, SafeReadBody(raw));

                    throw FromProviderError("create subscription", raw, ex);
                }

                throw new BillingException(
                    BillingFailureKind.Unavailable,
                    "Maxio rejected the subscription for an unknown reason.",
                    innerException: ex);
            }
            catch (JsonException ex)
            {
                throw FromUnreadableBody("create subscription", scope, ex);
            }
            catch (Exception ex) when (IsWriteBlocked(ex) || IsTransportFailure(ex))
            {
                // The single send we allowed may or may not have reached Maxio. Settle it by reading, never
                // by re-sending.
                unknownOutcome = ex;
            }
        }

        if (unknownOutcome is not null)
        {
            _logger.LogWarning(unknownOutcome,
                "The subscription create for reference {Reference} produced no readable outcome; reconciling against Maxio.",
                subscriptionReference);

            var reconciled = await ReconcileSubscriptionAsync(subscriptionReference).ConfigureAwait(false);

            if (reconciled is not null)
            {
                return reconciled;
            }

            throw new BillingException(
                BillingFailureKind.UnknownOutcome,
                "The subscription could not be confirmed. Check your subscriptions before trying again.",
                innerException: unknownOutcome);
        }

        if (created is null)
        {
            throw new BillingException(
                BillingFailureKind.UnreadableResponse,
                "Maxio accepted the subscription but returned no subscription record.");
        }

        if (!string.Equals(created.Reference, subscriptionReference, StringComparison.Ordinal))
        {
            // Maxio did not echo our key back. Re-read rather than retry - a second create would be the one
            // thing that could actually duplicate the enrollment.
            _logger.LogWarning(
                "Maxio returned subscription {SubscriptionId} with reference '{Returned}' instead of '{Expected}'; re-reading.",
                created.Id, created.Reference, subscriptionReference);

            var reread = await ReconcileSubscriptionAsync(subscriptionReference).ConfigureAwait(false);

            if (reread is not null)
            {
                return reread;
            }
        }

        return MapSubscription(created);
    }

    private async Task<CustomerSubscription?> ReconcileSubscriptionAsync(string subscriptionReference)
    {
        using var cts = new CancellationTokenSource(ReconcileBudget);

        try
        {
            return await FindSubscriptionByReferenceAsync(subscriptionReference, cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is BillingException or OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not reconcile the subscription for reference {Reference}.", subscriptionReference);
            return null;
        }
    }

    private async Task<CustomerSubscription?> FindSubscriptionByReferenceAsync(string subscriptionReference, CancellationToken ct)
    {
        try
        {
            var response = await Client.Subscriptions.FindSubscription(subscriptionReference, ct).ConfigureAwait(false);
            return response.Subscription is { } subscription ? MapSubscription(subscription) : null;
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                // No subscription carries that reference - the normal "nothing was created" answer.
                return null;
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromProviderError("find subscription", raw, ex);
            }

            throw new BillingException(
                BillingFailureKind.Unavailable,
                "Maxio rejected the subscription lookup.",
                innerException: ex);
        }
        catch (JsonException ex)
        {
            throw FromUnreadableBody("find subscription", scope: null, ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            throw FromTransportFailure("find subscription", ex);
        }
    }

    // ---------------------------------------------------------------------------------------------------
    // Mapping
    // ---------------------------------------------------------------------------------------------------

    private static SubscriptionPlan MapPlan(Product product, string? currency) => new()
    {
        Handle = product.Handle!,
        Name = string.IsNullOrWhiteSpace(product.Name) ? product.Handle! : product.Name!,
        Description = product.Description,
        PriceInCents = product.PriceInCents ?? 0,
        Currency = currency,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit?.Value,
        TrialPriceInCents = product.TrialPriceInCents,
        TrialInterval = product.TrialInterval,
        TrialIntervalUnit = product.TrialIntervalUnit?.Value,

        // RequireCreditCard, not the similarly named RequestCreditCard on the same record: only the former
        // means a payment method must be captured before the plan can be subscribed to.
        RequiresPaymentMethod = product.RequireCreditCard ?? false,
        ExpirationInterval = product.ExpirationInterval,
        ExpirationIntervalUnit = product.ExpirationIntervalUnit?.Value
    };

    private static CustomerSubscription MapSubscription(Subscription subscription) => new()
    {
        Id = subscription.Id ?? 0,
        Reference = subscription.Reference,
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,
        State = subscription.State?.Value,
        IsActive = IsActive(subscription.State),
        PriceInCents = subscription.ProductPriceInCents ?? subscription.CurrentBillingAmountInCents,
        Currency = subscription.Currency,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextAssessmentAt,
        TrialEndedAt = subscription.TrialEndedAt,
        CanceledAt = subscription.CanceledAt
    };

    /// <summary>
    /// A subscription counts as active unless Maxio put it in a terminal state. An unrecognised state counts
    /// as active on purpose: treating an unknown state as "gone" would let the subscribe flow enroll the
    /// shopper a second time.
    /// </summary>
    private static bool IsActive(SubscriptionState? state) =>
        state is null || !TerminalStates.Contains(state);

    private static string DescribeCustomerValidation(CustomerErrorResponse1? validation)
    {
        var messages = new List<string>();

        if (validation?.Errors?.PerPage is { Count: > 0 } perPage)
        {
            messages.AddRange(perPage);
        }

        if (validation?.Errors?.PricePoint is { Count: > 0 } pricePoint)
        {
            messages.AddRange(pricePoint);
        }

        return messages.Count > 0 ? string.Join("; ", messages) : "Maxio gave no detail.";
    }

    // ---------------------------------------------------------------------------------------------------
    // Failure translation
    // ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// Gives the whole logical operation - every SDK call it makes, every retry, all backoff - one deadline.
    /// The per-attempt timeouts cap a single stalled socket; only this caps what the caller experiences.
    /// </summary>
    private async Task<T> WithCallBudgetAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _settings.CallBudgetSeconds)));

        try
        {
            return await operation(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BillingException(
                BillingFailureKind.Unavailable,
                "The billing system did not respond in time. Please try again.");
        }
    }

    private BillingException FromProviderError(string operation, RawError raw, Exception inner)
    {
        var status = (int)raw.StatusCode;

        _logger.LogWarning("Maxio returned {Status} for '{Operation}'.", status, operation);

        var (kind, message) = status switch
        {
            400 or 422 => (BillingFailureKind.Validation, "The billing system rejected this request as invalid."),
            401 or 403 => (BillingFailureKind.ProviderUnauthorized, "eShopOnWeb is not authorised to talk to the billing system."),
            404 => (BillingFailureKind.Misconfigured, "The requested billing record does not exist."),
            409 => (BillingFailureKind.Conflict, "The request conflicts with existing billing state."),
            429 => (BillingFailureKind.RateLimited, "The billing system is rate limiting requests. Please try again shortly."),
            _ => (BillingFailureKind.Unavailable, "The billing system is currently unavailable.")
        };

        return new BillingException(kind, message, status, inner);
    }

    private BillingException FromTransportFailure(string operation, Exception inner)
    {
        _logger.LogWarning(inner, "Could not reach Maxio for '{Operation}'.", operation);

        return new BillingException(
            BillingFailureKind.Unavailable,
            "The billing system could not be reached. Please try again.",
            innerException: inner);
    }

    /// <summary>
    /// A <see cref="JsonException"/> reaches this boundary from two directions that need opposite answers: a
    /// drifted success body means the outcome is genuinely unknown, while a non-2xx body that does not match
    /// its generated error shape means we were deterministically rejected and only the reason was lost -
    /// answering "unavailable" there would tell a retrying caller to keep retrying something that can never
    /// succeed. The status recorded by <see cref="MaxioHttpDiagnosticsHandler"/> is what tells them apart,
    /// because the SDK has already discarded it by the time we get here.
    /// </summary>
    private BillingException FromUnreadableBody(string operation, MaxioCallScope? scope, JsonException inner)
    {
        var status = scope?.LastStatusCode;

        if (status is >= 400 and < 500)
        {
            _logger.LogWarning(inner,
                "Maxio rejected '{Operation}' with {Status} and a body that did not match the expected error shape.",
                operation, status);

            return new BillingException(
                BillingFailureKind.Validation,
                "The billing system rejected this request. Retrying it unchanged will not help.",
                status,
                inner);
        }

        _logger.LogError(inner, "Could not read the Maxio response for '{Operation}' (status {Status}).",
            operation, status?.ToString(CultureInfo.InvariantCulture) ?? "unknown");

        return new BillingException(
            BillingFailureKind.UnreadableResponse,
            "The billing system returned a response that could not be processed.",
            status,
            inner);
    }

    private static bool IsTransportFailure(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException
        || ex.InnerException is HttpRequestException or TaskCanceledException;

    private static bool IsWriteBlocked(Exception ex) =>
        ex is MaxioWriteBlockedException || ex.InnerException is MaxioWriteBlockedException;

    private string SafeReadBody(RawError raw)
    {
        try
        {
            return raw.ReadAsString();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "The Maxio error body could not be read as text.");
            return "<unreadable>";
        }
    }
}
