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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// <see cref="ISubscriptionBillingService"/> backed by Maxio Advanced Billing.
/// </summary>
/// <remarks>
/// Registered as a singleton: it holds the subscribe gate and the catalog cache, and the SDK client it
/// wraps is itself long-lived.
/// </remarks>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const string ProductFamilyIdCacheKey = "Maxio:ProductFamilyId";
    private const string SiteInfoCacheKey = "Maxio:SiteInfo";

    /// <summary>
    /// Collection methods that do not require a stored payment profile, keyed by the site's billing
    /// architecture. Sending the wrong one for the architecture is not a valid option at Maxio, so the
    /// value is resolved from the site rather than hard-coded.
    /// </summary>
    private const string RelationshipInvoicingCollectionMethod = "remittance";

    private const string LegacyStatementsCollectionMethod = "invoice";

    /// <summary>The SDK's own default page size — a value the API is known to accept.</summary>
    private const int ProductsPageSize = 20;

    /// <summary>Stops a runaway pager if the provider ever returns full pages indefinitely.</summary>
    private const int MaxProductPages = 50;

    /// <summary>
    /// States in which a subscription no longer entitles the customer to anything, so subscribing again is
    /// a genuinely new enrollment rather than a duplicate. Every other state — including one this SDK
    /// version does not know — counts as live and blocks a second subscribe.
    /// </summary>
    private static readonly HashSet<string> TerminalStates =
        new(StringComparer.OrdinalIgnoreCase) { "canceled", "expired", "failed_to_create" };

    /// <summary>
    /// Striped single-flight gates, so a double-click from one user cannot run two subscribe flows at once
    /// inside this process. Striping keeps the memory bounded regardless of how many users sign in; two
    /// unrelated users colliding on a stripe are merely serialised for the length of one subscribe.
    /// Across processes the guarantee is carried by Maxio's uniqueness on the customer reference plus the
    /// list-before-create check below, not by this gate.
    /// </summary>
    private readonly SemaphoreSlim[] _subscribeGates =
        Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(MaxioAdvancedBillingClient client, IOptions<MaxioOptions> options,
        IMemoryCache cache, ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _options = options.Value;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var site = await GetSiteInfoAsync(cancellationToken);
        var products = await ListFamilyProductsAsync(cancellationToken);

        return products
            .Where(product => !string.IsNullOrWhiteSpace(product.Handle) && product.ArchivedAt is null)
            .Select(product => MapPlan(product, site.Currency))
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(Subscriber subscriber, string? planHandle,
        CancellationToken cancellationToken = default)
    {
        if (subscriber is null || string.IsNullOrWhiteSpace(subscriber.Email))
        {
            throw new BillingProviderException(BillingFailureKind.Rejected,
                "The signed-in user has no email address, which the billing provider requires.");
        }

        var reference = MaxioSubscriberMapper.ToCustomerReference(subscriber.Email);
        var gate = GateFor(reference);

        await gate.WaitAsync(cancellationToken);
        try
        {
            var plan = await ResolvePlanAsync(planHandle, cancellationToken);
            var customerId = await EnsureCustomerAsync(reference, subscriber.Email, cancellationToken);

            var existing = await FindLiveSubscriptionAsync(customerId, plan.Handle, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Customer {CustomerId} is already subscribed to '{PlanHandle}' (subscription {SubscriptionId}); not creating another.",
                    customerId, plan.Handle, existing.Id);
                return new SubscribeResult(existing, alreadySubscribed: true);
            }

            var created = await CreateSubscriptionAsync(customerId, plan.Handle, cancellationToken);
            return new SubscribeResult(created, alreadySubscribed: false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(Subscriber subscriber,
        CancellationToken cancellationToken = default)
    {
        if (subscriber is null || string.IsNullOrWhiteSpace(subscriber.Email))
        {
            throw new BillingProviderException(BillingFailureKind.Rejected,
                "The signed-in user has no email address, which the billing provider requires.");
        }

        var reference = MaxioSubscriberMapper.ToCustomerReference(subscriber.Email);
        var customer = await TryReadCustomerAsync(reference, cancellationToken);
        if (customer is null)
        {
            // Never subscribed: an empty list, not an error.
            return Array.Empty<CustomerSubscription>();
        }

        return await ListCustomerSubscriptionsAsync(RequireCustomerId(customer), cancellationToken);
    }

    // ---------------------------------------------------------------- catalog

    private async Task<SubscriptionPlan> ResolvePlanAsync(string? planHandle, CancellationToken cancellationToken)
    {
        var plans = await GetPlansAsync(cancellationToken);

        if (plans.Count == 0)
        {
            throw new BillingProviderException(BillingFailureKind.Misconfigured,
                "No subscription plans are available.");
        }

        var requested = string.IsNullOrWhiteSpace(planHandle) ? _options.DefaultPlanHandle : planHandle;

        if (string.IsNullOrWhiteSpace(requested))
        {
            return plans[0];
        }

        var match = plans.FirstOrDefault(
            plan => string.Equals(plan.Handle, requested.Trim(), StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            throw new BillingProviderException(BillingFailureKind.NotFound,
                $"No subscription plan with handle '{requested.Trim()}' is available.");
        }

        return match;
    }

    private async Task<IReadOnlyList<Product>> ListFamilyProductsAsync(CancellationToken cancellationToken)
    {
        var familyId = await GetProductFamilyIdAsync(cancellationToken);

        try
        {
            return await PageFamilyProductsAsync(familyId, cancellationToken);
        }
        catch (BillingProviderException ex) when (ex.Kind == BillingFailureKind.NotFound)
        {
            // Maxio reassigns numeric ids when a catalog is re-seeded, so a cached id can go stale.
            // Drop it, resolve the family by handle again, and retry once.
            _logger.LogWarning(
                "Cached Maxio product family id {FamilyId} is no longer valid; re-resolving handle '{Handle}'.",
                familyId, _options.ProductFamilyHandle);
            _cache.Remove(ProductFamilyIdCacheKey);

            var refreshedId = await GetProductFamilyIdAsync(cancellationToken);
            return await PageFamilyProductsAsync(refreshedId, cancellationToken);
        }
    }

    private async Task<IReadOnlyList<Product>> PageFamilyProductsAsync(int familyId,
        CancellationToken cancellationToken)
    {
        var familyIdText = familyId.ToString(CultureInfo.InvariantCulture);
        var products = new List<Product>();

        for (var page = 1; page <= MaxProductPages; page++)
        {
            IReadOnlyList<ProductResponse> responses;
            try
            {
                var currentPage = page;
                responses = await BoundedAsync(
                    ct => _client.ProductFamilies.ListProductsForProductFamily(
                        productFamilyId: familyIdText,
                        dateField: null,
                        filter: null,
                        startDate: null,
                        endDate: null,
                        startDatetime: null,
                        endDatetime: null,
                        includeArchived: false,
                        include: null,
                        page: currentPage,
                        perPage: ProductsPageSize,
                        ct: ct),
                    cancellationToken);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out var notFoundMessage))
                {
                    _logger.LogWarning("Maxio list-products for family {FamilyId} returned: {Message}",
                        familyId, notFoundMessage);
                    throw new BillingProviderException(BillingFailureKind.NotFound,
                        "The configured subscription catalog could not be read.", HttpStatusCode.NotFound, ex);
                }

                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw Translate("list products for product family", raw, ex);
                }

                throw UnknownFailure("list products for product family", ex);
            }
            catch (Exception ex) when (IsTransportOrParseFailure(ex))
            {
                throw TranslateTransport("list products for product family", ex, cancellationToken);
            }

            products.AddRange(responses.Select(response => response.Product));

            if (responses.Count < ProductsPageSize)
            {
                return products;
            }
        }

        _logger.LogWarning("Stopped paging Maxio products for family {FamilyId} after {MaxPages} pages.",
            familyId, MaxProductPages);
        return products;
    }

    private async Task<int> GetProductFamilyIdAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(ProductFamilyIdCacheKey, out int cachedId))
        {
            return cachedId;
        }

        IReadOnlyList<ProductFamilyResponse> families;
        try
        {
            families = await BoundedAsync(
                ct => _client.ProductFamilies.ListProductFamilies(
                    dateField: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    ct: ct),
                cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate("list product families", ex.Error, ex);
        }
        catch (Exception ex) when (IsTransportOrParseFailure(ex))
        {
            throw TranslateTransport("list product families", ex, cancellationToken);
        }

        var family = families
            .Select(response => response.ProductFamily)
            .FirstOrDefault(candidate => candidate is not null && string.Equals(
                candidate.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));

        if (family?.Id is null)
        {
            _logger.LogError("No Maxio product family with handle '{Handle}' exists on this site.",
                _options.ProductFamilyHandle);
            throw new BillingProviderException(BillingFailureKind.Misconfigured,
                "The subscription catalog is not configured correctly.");
        }

        _cache.Set(ProductFamilyIdCacheKey, family.Id.Value, _options.CatalogCacheDuration);
        return family.Id.Value;
    }

    /// <summary>Billing-site facts that shape every request but never change per call.</summary>
    private sealed record MaxioSiteInfo(string? Currency, bool RelationshipInvoicingEnabled,
        string? DefaultPaymentCollectionMethod);

    private async Task<MaxioSiteInfo> GetSiteInfoAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(SiteInfoCacheKey, out MaxioSiteInfo? cached) && cached is not null)
        {
            return cached;
        }

        SiteResponse response;
        try
        {
            response = await BoundedAsync(ct => _client.Sites.ReadSite(ct), cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate("read site", ex.Error, ex);
        }
        catch (Exception ex) when (IsTransportOrParseFailure(ex))
        {
            throw TranslateTransport("read site", ex, cancellationToken);
        }

        var site = new MaxioSiteInfo(
            response.Site.Currency,
            response.Site.RelationshipInvoicingEnabled ?? false,
            response.Site.DefaultPaymentCollectionMethod);

        _logger.LogInformation(
            "Maxio site: currency={Currency}, relationshipInvoicing={RelationshipInvoicing}, defaultCollectionMethod={DefaultCollectionMethod}",
            site.Currency, site.RelationshipInvoicingEnabled, site.DefaultPaymentCollectionMethod);

        _cache.Set(SiteInfoCacheKey, site, _options.CatalogCacheDuration);
        return site;
    }

    /// <summary>
    /// Decides how Maxio should collect the new subscription's balance.
    /// </summary>
    /// <remarks>
    /// A site whose default is <c>automatic</c> asks Maxio to charge a card the moment the subscription is
    /// created; with no payment profile on file that is rejected outright. This API captures no card, so it
    /// enrolls on an invoiced collection method instead. Which one is valid differs by billing
    /// architecture — <c>remittance</c> under Relationship Invoicing, <c>invoice</c> under legacy
    /// Statements — so it is read from the site rather than assumed, and can be overridden with
    /// <c>Maxio:PaymentCollectionMethod</c>.
    /// </remarks>
    private async Task<CollectionMethod> ResolveCollectionMethodAsync(CancellationToken cancellationToken)
    {
        var configured = _options.PaymentCollectionMethod;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return CollectionMethod.FromValue(configured.Trim().ToLowerInvariant());
        }

        var site = await GetSiteInfoAsync(cancellationToken);

        return site.RelationshipInvoicingEnabled
            ? CollectionMethod.FromValue(RelationshipInvoicingCollectionMethod)
            : CollectionMethod.FromValue(LegacyStatementsCollectionMethod);
    }

    // ---------------------------------------------------------------- customer

    private async Task<int> EnsureCustomerAsync(string reference, string email,
        CancellationToken cancellationToken)
    {
        var existing = await TryReadCustomerAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return RequireCustomerId(existing);
        }

        var (firstName, lastName) = MaxioSubscriberMapper.ToCustomerName(email);
        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        CustomerResponse? response = null;
        Exception? ambiguousFailure = null;

        try
        {
            using (MaxioSingleSendGuard.BeginSingleSend($"create Maxio customer '{reference}'"))
            {
                response = await BoundedAsync(ct => _client.Customers.CreateCustomer(body, ct), cancellationToken);
            }
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // The 422 accessor points at a shared, pagination-shaped model that cannot hold a customer
            // validation message, so it is logged best-effort only and never shown to the caller.
            if (ex.Error.TryGetCustomerErrorResponse1(out var validation))
            {
                _logger.LogError(ex,
                    "Maxio rejected the customer for reference {Reference}. Typed error fields: per_page={PerPage}, price_point={PricePoint}",
                    reference,
                    string.Join(", ", validation.Errors?.PerPage ?? Array.Empty<string>()),
                    string.Join(", ", validation.Errors?.PricePoint ?? Array.Empty<string>()));

                // Most likely cause: another process created this customer first. Maxio allows only one
                // customer per reference, so re-read before deciding this was the caller's fault.
                var raced = await TryReadCustomerAsync(reference, cancellationToken);
                if (raced is not null)
                {
                    return RequireCustomerId(raced);
                }

                throw new BillingProviderException(BillingFailureKind.Rejected,
                    "The billing provider rejected the customer record for this account.",
                    HttpStatusCode.UnprocessableEntity, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Translate("create customer", raw, ex);
            }

            throw UnknownFailure("create customer", ex);
        }
        catch (Exception ex) when (IsAmbiguousWriteFailure(ex))
        {
            ThrowIfCallerCancelled(ex, cancellationToken);
            ambiguousFailure = ex;
        }

        if (response is not null && ambiguousFailure is null)
        {
            return RequireCustomerId(response.Customer);
        }

        // The write may or may not have been applied — re-read rather than guessing.
        _logger.LogWarning(ambiguousFailure,
            "Could not confirm the outcome of creating Maxio customer '{Reference}'; reconciling.", reference);

        var reconciled = await TryReadCustomerAsync(reference, cancellationToken);
        if (reconciled is not null)
        {
            return RequireCustomerId(reconciled);
        }

        throw TranslateTransport("create customer", ambiguousFailure!, cancellationToken);
    }

    private async Task<Customer?> TryReadCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(
                ct => _client.Customers.ReadCustomerByReference(reference, ct), cancellationToken);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            // A genuine miss — this user has no Maxio customer yet.
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate("read customer by reference", ex.Error, ex);
        }
        catch (Exception ex) when (IsTransportOrParseFailure(ex))
        {
            // An unreadable response is NOT an absent customer; saying so here would turn a corrupt
            // response into a duplicate enrollment.
            throw TranslateTransport("read customer by reference", ex, cancellationToken);
        }
    }

    private static int RequireCustomerId(Customer customer)
    {
        if (customer.Id is null)
        {
            throw new BillingProviderException(BillingFailureKind.Unknown,
                "The billing provider returned a customer record without an identifier.");
        }

        return customer.Id.Value;
    }

    // ---------------------------------------------------------------- subscriptions

    private async Task<CustomerSubscription> CreateSubscriptionAsync(int customerId, string planHandle,
        CancellationToken cancellationToken)
    {
        var collectionMethod = await ResolveCollectionMethodAsync(cancellationToken);

        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = planHandle,
                CustomerId = customerId,
                PaymentCollectionMethod = collectionMethod
                // No payment-profile, credit-card or customer-attribute fields: the balance is invoiced
                // rather than charged, and the customer is created explicitly so the flow stays idempotent.
            }
        };

        SubscriptionResponse? response = null;
        Exception? ambiguousFailure = null;

        try
        {
            using (MaxioSingleSendGuard.BeginSingleSend(
                       $"create Maxio subscription for customer {customerId} on '{planHandle}'"))
            {
                response = await BoundedAsync(
                    ct => _client.Subscriptions.CreateSubscription(body, ct), cancellationToken);
            }
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var validation))
            {
                var messages = validation.Errors ?? Array.Empty<string>();
                _logger.LogError(ex,
                    "Maxio rejected the subscription for customer {CustomerId} on '{PlanHandle}': {Messages}",
                    customerId, planHandle, string.Join("; ", messages));

                throw new BillingProviderException(BillingFailureKind.Rejected,
                    "The billing provider rejected this subscription request.",
                    HttpStatusCode.UnprocessableEntity, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Translate("create subscription", raw, ex);
            }

            throw UnknownFailure("create subscription", ex);
        }
        catch (Exception ex) when (IsAmbiguousWriteFailure(ex))
        {
            ThrowIfCallerCancelled(ex, cancellationToken);
            ambiguousFailure = ex;
        }

        if (ambiguousFailure is null && response?.Subscription is not null)
        {
            return MapSubscription(response.Subscription);
        }

        // Either the call failed in a way that cannot distinguish "not applied" from "applied but the
        // answer was lost" (a transport fault, a refused re-send, an unparseable body), or Maxio answered
        // 2xx with no subscription. Settle it by re-reading, exactly as for the customer.
        _logger.LogWarning(ambiguousFailure,
            "Could not confirm the outcome of subscribing customer {CustomerId} to '{PlanHandle}'; reconciling.",
            customerId, planHandle);

        var reconciled = await TryFindLiveSubscriptionAsync(customerId, planHandle, cancellationToken);
        if (reconciled is not null)
        {
            return reconciled;
        }

        if (ambiguousFailure is JsonException)
        {
            // Nothing was created and the provider's own reason was lost with the unparseable body. This
            // is a deterministic rejection, not an outage: answering 5xx would invite a pointless retry.
            throw new BillingProviderException(BillingFailureKind.Rejected,
                "The billing provider rejected this subscription request.", null, ambiguousFailure);
        }

        throw TranslateTransport("create subscription", ambiguousFailure
            ?? new InvalidOperationException("Maxio returned no subscription."), cancellationToken);
    }

    private async Task<CustomerSubscription?> FindLiveSubscriptionAsync(int customerId, string planHandle,
        CancellationToken cancellationToken)
    {
        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(subscription =>
            string.Equals(subscription.PlanHandle, planHandle, StringComparison.OrdinalIgnoreCase)
            && IsLive(subscription));
    }

    /// <summary>Best-effort variant used while reconciling, where a second failure must not mask the first.</summary>
    private async Task<CustomerSubscription?> TryFindLiveSubscriptionAsync(int customerId, string planHandle,
        CancellationToken cancellationToken)
    {
        try
        {
            return await FindLiveSubscriptionAsync(customerId, planHandle, cancellationToken);
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning(ex, "Reconciliation read failed for customer {CustomerId} on '{PlanHandle}'.",
                customerId, planHandle);
            return null;
        }
    }

    private async Task<IReadOnlyList<CustomerSubscription>> ListCustomerSubscriptionsAsync(int customerId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SubscriptionResponse> responses;
        try
        {
            responses = await BoundedAsync(
                ct => _client.Customers.ListCustomerSubscriptions(customerId, ct), cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate("list customer subscriptions", ex.Error, ex);
        }
        catch (Exception ex) when (IsTransportOrParseFailure(ex))
        {
            throw TranslateTransport("list customer subscriptions", ex, cancellationToken);
        }

        return responses
            .Select(response => response.Subscription)
            .Where(subscription => subscription is not null)
            .Select(subscription => MapSubscription(subscription!))
            .ToList();
    }

    private static bool IsLive(CustomerSubscription subscription) =>
        string.IsNullOrEmpty(subscription.State) || !TerminalStates.Contains(subscription.State);

    // ---------------------------------------------------------------- mapping

    private static SubscriptionPlan MapPlan(Product product, string? currency) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? product.Handle ?? string.Empty,
        Description = product.Description,
        PriceInCents = product.PriceInCents ?? 0,
        Currency = currency,
        Interval = product.Interval ?? 0,
        IntervalUnit = product.IntervalUnit?.Value,
        SetupFeeInCents = product.InitialChargeInCents ?? 0,
        TrialInterval = product.TrialInterval ?? 0,
        TrialIntervalUnit = product.TrialIntervalUnit?.Value,
        TrialPriceInCents = product.TrialPriceInCents ?? 0,
        RequiresPaymentProfileAtSignup = product.RequireCreditCard ?? false
    };

    private static CustomerSubscription MapSubscription(Subscription subscription) => new()
    {
        Id = subscription.Id ?? 0,
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,
        State = subscription.State?.Value,
        PriceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0,
        Currency = subscription.Currency,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod?.Value,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextAssessmentAt,
        TotalRevenueInCents = subscription.TotalRevenueInCents ?? 0,
        CreatedAt = subscription.CreatedAt,
        CanceledAt = subscription.CanceledAt
    };

    // ---------------------------------------------------------------- plumbing

    private SemaphoreSlim GateFor(string reference) =>
        _subscribeGates[(uint)StringComparer.Ordinal.GetHashCode(reference) % _subscribeGates.Length];

    /// <summary>
    /// Applies the one total budget every provider call gets, linked to the caller's own token so a
    /// disconnected client also stops the outbound work. The SDK's timeouts bound a single attempt only.
    /// </summary>
    private async Task<T> BoundedAsync<T>(Func<CancellationToken, Task<T>> call,
        CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(_options.CallBudget);
        return await call(budget.Token);
    }

    /// <summary>Failures on a read: nothing was changed, so there is nothing to reconcile.</summary>
    private static bool IsTransportOrParseFailure(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or OperationCanceledException
            or JsonException or AuthSchemeException or MaxioDuplicateSendException;

    /// <summary>Failures on a write whose outcome cannot be known without re-reading provider state.</summary>
    private static bool IsAmbiguousWriteFailure(Exception ex) => IsTransportOrParseFailure(ex);

    private static void ThrowIfCallerCancelled(Exception ex, CancellationToken cancellationToken)
    {
        if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
    }

    private BillingProviderException Translate(string operation, RawError raw, Exception inner)
    {
        var status = raw.StatusCode;
        _logger.LogError(inner, "Maxio {Operation} failed with HTTP {StatusCode}: {Body}",
            operation, (int)status, ReadBodySafely(raw));

        var kind = (int)status switch
        {
            401 or 403 => BillingFailureKind.Misconfigured,
            404 => BillingFailureKind.NotFound,
            409 => BillingFailureKind.Conflict,
            400 or 422 => BillingFailureKind.Rejected,
            >= 500 or 429 => BillingFailureKind.Unavailable,
            _ => BillingFailureKind.Unknown
        };

        return new BillingProviderException(kind, MessageFor(kind), status, inner);
    }

    private BillingProviderException TranslateTransport(string operation, Exception ex,
        CancellationToken cancellationToken)
    {
        ThrowIfCallerCancelled(ex, cancellationToken);

        var kind = ex switch
        {
            AuthSchemeException => BillingFailureKind.Misconfigured,
            JsonException => BillingFailureKind.Unknown,
            MaxioDuplicateSendException => BillingFailureKind.Unknown,
            _ => BillingFailureKind.Unavailable
        };

        _logger.LogError(ex, "Maxio {Operation} failed: {ExceptionType}", operation, ex.GetType().Name);
        return new BillingProviderException(kind, MessageFor(kind), null, ex);
    }

    private BillingProviderException UnknownFailure(string operation, Exception ex)
    {
        _logger.LogError(ex, "Maxio {Operation} failed with an unrecognised error shape.", operation);
        return new BillingProviderException(BillingFailureKind.Unknown,
            MessageFor(BillingFailureKind.Unknown), null, ex);
    }

    /// <summary>Caller-safe text. Provider and framework exception messages are logged, never returned.</summary>
    private static string MessageFor(BillingFailureKind kind) => kind switch
    {
        BillingFailureKind.Rejected => "The billing provider rejected this request.",
        BillingFailureKind.NotFound => "The requested billing record does not exist.",
        BillingFailureKind.Conflict => "This request conflicts with the current billing state.",
        BillingFailureKind.Misconfigured => "The billing integration is not configured correctly.",
        BillingFailureKind.Unavailable => "The billing provider is currently unavailable.",
        _ => "The billing provider returned a response that could not be processed."
    };

    private static string ReadBodySafely(RawError raw)
    {
        try
        {
            return raw.ReadAsString();
        }
        catch (Exception)
        {
            return "<unreadable>";
        }
    }
}
