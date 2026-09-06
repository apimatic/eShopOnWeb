using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
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

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Maxio Advanced Billing as eShopOnWeb's subscription billing system of record.
/// </summary>
/// <remarks>
/// This is the only type in the solution that knows the Maxio SDK exists. Everything it can throw is
/// translated into <see cref="BillingException"/> here, so no SDK type, provider type name or raw provider
/// message escapes into application code or onto the wire.
/// </remarks>
public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    /// <summary>Largest page the products-for-family operation accepts; larger values are coerced down.</summary>
    private const int ProductsPerPage = 200;

    /// <summary>A stop, so a provider that never signals the end of the catalog cannot spin forever.</summary>
    private const int MaxCatalogPages = 20;

    private const string PlansCacheKey = "maxio:plans";
    private const string ProductFamilyCacheKey = "maxio:product-family-id";
    private const string SiteCacheKey = "maxio:site";

    /// <summary>
    /// States in which a subscription is finished, so subscribing again is a legitimate new enrollment.
    /// Everything else — including a state this build has never seen — counts as live, so an unfamiliar
    /// value can never cause a duplicate enrollment.
    /// </summary>
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        SubscriptionState.Canceled.Value,
        SubscriptionState.Expired.Value,
        SubscriptionState.TrialEnded.Value,
        SubscriptionState.FailedToCreate.Value
    };

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly IBillingOperationLock _operationLock;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        IBillingOperationLock operationLock,
        IMemoryCache cache,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _operationLock = operationLock;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        return await GetPlansAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<SubscribeResult> SubscribeAsync(
        BillingCustomerIdentity customer,
        string planHandle,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(customer, nameof(customer));
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new BillingException(BillingFailureKind.InvalidRequest, "A subscription plan handle is required.");
        }

        var requestedHandle = planHandle.Trim();
        var plan = (await GetPlansAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(candidate => string.Equals(candidate.Handle, requestedHandle, StringComparison.OrdinalIgnoreCase));

        if (plan is null)
        {
            throw new BillingException(
                BillingFailureKind.NotFound,
                $"No subscription plan with handle '{requestedHandle}' is available.");
        }

        if (plan.RequiresPaymentProfileAtSignup)
        {
            // Configuration drift on the provider's side. Catching it here turns an opaque rejection at
            // enrollment time into a message that says what is actually wrong.
            throw new BillingException(
                BillingFailureKind.InvalidRequest,
                $"Plan '{plan.Handle}' requires a stored payment profile at signup, which this subscribe flow does not capture.");
        }

        // Everything from here to the create is one critical section per shopper. Without it, two concurrent
        // requests can both observe "not subscribed" and both enroll.
        using var _ = await _operationLock.AcquireAsync(customer.Reference, cancellationToken).ConfigureAwait(false);

        var billingCustomer = await EnsureCustomerAsync(customer, cancellationToken).ConfigureAwait(false);
        var customerId = billingCustomer.Id!.Value;

        var existing = await FindLiveSubscriptionAsync(customerId, plan.Handle, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Maxio customer {CustomerId} is already subscribed to {PlanHandle} (subscription {SubscriptionId}); returning the existing subscription.",
                customerId, plan.Handle, existing.Id);

            return new SubscribeResult(MapSubscription(existing, plan, plan.Currency), AlreadySubscribed: true);
        }

        var created = await CreateSubscriptionAsync(customerId, plan.Handle, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Created Maxio subscription {SubscriptionId} for customer {CustomerId} on plan {PlanHandle}.",
            created.Id, customerId, plan.Handle);

        return new SubscribeResult(MapSubscription(created, plan, plan.Currency), AlreadySubscribed: false);
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(
        BillingCustomerIdentity customer,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(customer, nameof(customer));
        EnsureConfigured();

        var billingCustomer = await FindCustomerAsync(customer.Reference, cancellationToken).ConfigureAwait(false);
        if (billingCustomer is null)
        {
            // A shopper who has never subscribed has no billing customer at all. That is an empty list.
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await ListCustomerSubscriptionsAsync(billingCustomer.Id!.Value, cancellationToken)
            .ConfigureAwait(false);

        // Plan metadata is a display fallback only, so a catalog hiccup must not fail the shopper's own list.
        var plans = await TryGetPlansAsync(cancellationToken).ConfigureAwait(false);
        var siteCurrency = await TryGetSiteCurrencyAsync(cancellationToken).ConfigureAwait(false);

        return subscriptions
            .Select(subscription => MapSubscription(subscription, FindPlan(plans, subscription.Product?.Handle), siteCurrency))
            .OrderByDescending(subscription => subscription.Id)
            .ToArray();
    }

    // ---------------------------------------------------------------------------------------------------
    // Catalog
    // ---------------------------------------------------------------------------------------------------

    private async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(PlansCacheKey, out IReadOnlyList<SubscriptionPlan>? cached) && cached is not null)
        {
            return cached;
        }

        var familyId = await ResolveProductFamilyIdAsync(cancellationToken).ConfigureAwait(false);
        var currency = await TryGetSiteCurrencyAsync(cancellationToken).ConfigureAwait(false);
        var products = await ListProductsAsync(familyId, cancellationToken).ConfigureAwait(false);

        var plans = products
            .Select(response => response.Product)
            .Where(product => !string.IsNullOrWhiteSpace(product.Handle) && product.ArchivedAt is null)
            .Select(product => MapPlan(product, currency))
            .OrderBy(plan => plan.PriceInCents)
            .ToArray();

        _cache.Set(PlansCacheKey, (IReadOnlyList<SubscriptionPlan>)plans, CacheLifetime);
        return plans;
    }

    private async Task<IReadOnlyList<SubscriptionPlan>> TryGetPlansAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await GetPlansAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (BillingException ex)
        {
            _logger.LogWarning(ex, "Could not load the Maxio plan catalog; subscription plan details will be limited.");
            return Array.Empty<SubscriptionPlan>();
        }
    }

    private async Task<int> ResolveProductFamilyIdAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(ProductFamilyCacheKey, out int cachedId) && cachedId != 0)
        {
            return cachedId;
        }

        var handle = _settings.ProductFamilyHandle!.Trim();

        var families = await InvokeAsync("list product families", async token =>
        {
            try
            {
                return await _client.ProductFamilies
                    .ListProductFamilies(null, null, null, null, null, ct: token)
                    .ConfigureAwait(false);
            }
            catch (SdkException<RawError> ex)
            {
                throw TranslateRawError("list product families", ex.Error, ex);
            }
        }, cancellationToken).ConfigureAwait(false);

        var id = families?
            .Select(family => family?.ProductFamily)
            .FirstOrDefault(family => string.Equals(family?.Handle, handle, StringComparison.OrdinalIgnoreCase))?
            .Id;

        if (id is null or 0)
        {
            throw new BillingException(
                BillingFailureKind.NotConfigured,
                $"The configured Maxio product family '{handle}' does not exist on this site.");
        }

        _cache.Set(ProductFamilyCacheKey, id.Value, CacheLifetime);
        return id.Value;
    }

    private async Task<IReadOnlyList<ProductResponse>> ListProductsAsync(int familyId, CancellationToken cancellationToken)
    {
        var familyIdText = familyId.ToString(CultureInfo.InvariantCulture);
        var products = new List<ProductResponse>();

        for (var page = 1; page <= MaxCatalogPages; page++)
        {
            var pageNumber = page;

            var batch = await InvokeAsync("list products for product family", async token =>
            {
                try
                {
                    return await _client.ProductFamilies.ListProductsForProductFamily(
                        productFamilyId: familyIdText,
                        dateField: null,
                        filter: null,
                        startDate: null,
                        endDate: null,
                        startDatetime: null,
                        endDatetime: null,
                        includeArchived: false,
                        include: null,
                        page: pageNumber,
                        perPage: ProductsPerPage,
                        ct: token).ConfigureAwait(false);
                }
                catch (SdkException<ListProductsForProductFamilyError> ex)
                {
                    if (ex.Error.TryGetString(out var detail))
                    {
                        // 404 is the only status this operation maps to a typed body.
                        _logger.LogError("Maxio could not find product family {FamilyId}: {Detail}", familyIdText, detail);
                        throw new BillingException(
                            BillingFailureKind.NotConfigured,
                            $"The configured Maxio product family '{_settings.ProductFamilyHandle}' could not be read.",
                            providerStatusCode: (int)HttpStatusCode.NotFound,
                            innerException: ex);
                    }

                    if (ex.Error.TryGetRawError(out var raw))
                    {
                        throw TranslateRawError("list products for product family", raw, ex);
                    }

                    throw TranslateUnrecognized("list products for product family", ex);
                }
            }, cancellationToken).ConfigureAwait(false);

            if (batch is null || batch.Count == 0) break;

            products.AddRange(batch.Where(response => response is not null));

            if (batch.Count < ProductsPerPage) break;
        }

        return products;
    }

    private async Task<MaxioSiteInfo> GetSiteAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(SiteCacheKey, out MaxioSiteInfo? cached) && cached is not null)
        {
            return cached;
        }

        var response = await InvokeAsync("read site", async token =>
        {
            try
            {
                return await _client.Sites.ReadSite(ct: token).ConfigureAwait(false);
            }
            catch (SdkException<RawError> ex)
            {
                throw TranslateRawError("read site", ex.Error, ex);
            }
        }, cancellationToken).ConfigureAwait(false);

        var site = response.Site;
        var info = new MaxioSiteInfo(
            site.Currency,
            site.RelationshipInvoicingEnabled ?? false,
            site.DefaultPaymentCollectionMethod,
            site.Subdomain,
            site.Test);

        _logger.LogInformation(
            "Connected to Maxio site {Subdomain} (test site: {IsTestSite}); primary currency {Currency}, relationship invoicing {RelationshipInvoicing}, default collection method {DefaultCollectionMethod}.",
            info.Subdomain, info.IsTestSite, info.Currency,
            info.RelationshipInvoicingEnabled, info.DefaultPaymentCollectionMethod);

        _cache.Set(SiteCacheKey, info, CacheLifetime);
        return info;
    }

    private async Task<string?> TryGetSiteCurrencyAsync(CancellationToken cancellationToken)
    {
        try
        {
            return (await GetSiteAsync(cancellationToken).ConfigureAwait(false)).Currency;
        }
        catch (BillingException ex)
        {
            // Currency is display metadata: Maxio's product model carries none, so it comes from the site.
            // Losing it must not take the plan list down with it. Enrolling, by contrast, needs this read to
            // succeed — see ResolveCollectionMethodAsync.
            _logger.LogWarning(ex, "Could not read the Maxio site; plan currency will be omitted.");
            return null;
        }
    }

    /// <summary>
    /// Decides how Maxio should collect the balance a new subscription assesses at signup.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is load-bearing, not a detail. A plan whose product does not require a payment profile still
    /// assesses its first period at creation, and this site's default is card collection — so a subscription
    /// created with the collection method unset is rejected outright with "no payment method was on file".
    /// Naming a non-card method instead has Maxio invoice the shopper for the balance.
    /// </para>
    /// <para>
    /// Which non-card method is legal depends on the site's billing architecture, so it is read from the site
    /// rather than hard-coded. <c>Maxio:PaymentCollectionMethod</c> overrides it, for a deployment that does
    /// capture cards and wants <c>automatic</c>.
    /// </para>
    /// </remarks>
    private async Task<CollectionMethod> ResolveCollectionMethodAsync(CancellationToken cancellationToken)
    {
        var configured = _settings.PaymentCollectionMethod?.Trim();
        if (!string.IsNullOrEmpty(configured))
        {
            var method = CollectionMethod.FromValue(configured!);
            if (!method.IsKnownValue())
            {
                throw new BillingException(
                    BillingFailureKind.NotConfigured,
                    $"{MaxioSettings.ConfigurationSection}:{nameof(MaxioSettings.PaymentCollectionMethod)} is not a payment collection method Maxio recognises.");
            }

            return method;
        }

        var site = await GetSiteAsync(cancellationToken).ConfigureAwait(false);

        return site.RelationshipInvoicingEnabled
            ? CollectionMethod.Remittance
            : CollectionMethod.Invoice;
    }

    // ---------------------------------------------------------------------------------------------------
    // Customers
    // ---------------------------------------------------------------------------------------------------

    private async Task<Customer> EnsureCustomerAsync(BillingCustomerIdentity identity, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerAsync(identity.Reference, cancellationToken).ConfigureAwait(false);
        if (existing is not null) return existing;

        try
        {
            var created = await CreateCustomerAsync(identity, cancellationToken).ConfigureAwait(false);
            if (created?.Id is not null) return created;

            throw new BillingException(
                BillingFailureKind.Unreadable,
                "Maxio accepted the new customer but did not return it.");
        }
        catch (BillingException ex) when (ex.Kind is BillingFailureKind.Conflict or BillingFailureKind.OutcomeUnknown)
        {
            // Maxio enforces uniqueness on the customer reference, so a conflict means somebody else won the
            // race; an unknown outcome means our own write may have landed. Either way: re-read and adopt.
            var reconciled = await FindCustomerAsync(identity.Reference, cancellationToken).ConfigureAwait(false);
            if (reconciled is not null)
            {
                _logger.LogInformation(
                    "Adopted Maxio customer {CustomerId} after a concurrent or unresolved create.", reconciled.Id);
                return reconciled;
            }

            throw;
        }
    }

    private async Task<Customer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        var response = await InvokeAsync<CustomerResponse?>("look up customer by reference", async token =>
        {
            try
            {
                return await _client.Customers.ReadCustomerByReference(reference, ct: token).ConfigureAwait(false);
            }
            catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                // The only status that means "no customer with that reference". Every other status must
                // propagate, or a transient failure would silently create a duplicate customer.
                return null;
            }
            catch (SdkException<RawError> ex)
            {
                throw TranslateRawError("look up customer by reference", ex.Error, ex);
            }
        }, cancellationToken).ConfigureAwait(false);

        var customer = response?.Customer;

        // Treat a success that carries no usable customer as "not found" too. An unreadable body is a
        // different fact entirely and has already been raised as one.
        return customer?.Id is null ? null : customer;
    }

    private async Task<Customer?> CreateCustomerAsync(BillingCustomerIdentity identity, CancellationToken cancellationToken)
    {
        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = identity.FirstName,
                LastName = identity.LastName,
                Email = identity.Email,
                Reference = identity.Reference
            }
        };

        var response = await InvokeAsync("create customer", async token =>
        {
            try
            {
                return await _client.Customers.CreateCustomer(body, ct: token).ConfigureAwait(false);
            }
            catch (SdkException<CreateCustomerError> ex)
            {
                if (ex.Error.TryGetCustomerErrorResponse1(out var validation))
                {
                    // 422. This payload models only two field-specific lists and carries no general message,
                    // so it is frequently empty — hence the fallback wording on the exception itself.
                    var details = (validation.Errors?.PerPage ?? Array.Empty<string>())
                        .Concat(validation.Errors?.PricePoint ?? Array.Empty<string>())
                        .ToArray();

                    _logger.LogWarning("Maxio rejected the customer record: {Details}", string.Join("; ", details));

                    throw new BillingException(
                        BillingFailureKind.Conflict,
                        "Maxio rejected the customer record for this shopper.",
                        details,
                        (int)HttpStatusCode.UnprocessableEntity,
                        ex);
                }

                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw TranslateRawError("create customer", raw, ex);
                }

                throw TranslateUnrecognized("create customer", ex);
            }
        }, cancellationToken, singleSend: true).ConfigureAwait(false);

        return response.Customer;
    }

    // ---------------------------------------------------------------------------------------------------
    // Subscriptions
    // ---------------------------------------------------------------------------------------------------

    private async Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var responses = await InvokeAsync<IReadOnlyList<SubscriptionResponse>?>("list customer subscriptions", async token =>
        {
            try
            {
                return await _client.Customers.ListCustomerSubscriptions(customerId, ct: token).ConfigureAwait(false);
            }
            catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            catch (SdkException<RawError> ex)
            {
                throw TranslateRawError("list customer subscriptions", ex.Error, ex);
            }
        }, cancellationToken).ConfigureAwait(false);

        if (responses is null) return Array.Empty<Subscription>();

        // The payload on this envelope is nullable, unlike most others in this API.
        return responses
            .Select(response => response?.Subscription)
            .Where(subscription => subscription is not null)
            .Select(subscription => subscription!)
            .ToArray();
    }

    private async Task<Subscription?> FindLiveSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, cancellationToken).ConfigureAwait(false);
        var live = subscriptions.Where(IsLive).ToArray();

        var unidentifiable = live.Count(subscription => string.IsNullOrWhiteSpace(subscription.Product?.Handle));
        if (unidentifiable > 0)
        {
            // If Maxio ever stops nesting the product on a subscription, the duplicate check below quietly
            // stops working. Make that loud rather than letting it enroll the shopper twice in silence.
            _logger.LogWarning(
                "Maxio customer {CustomerId} has {Count} live subscription(s) whose plan could not be identified; duplicate-subscription detection is degraded.",
                customerId, unidentifiable);
        }

        return live.FirstOrDefault(subscription =>
            string.Equals(subscription.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<Subscription> CreateSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken)
    {
        // Identify the product by handle and the customer by the id just resolved; with no price point named,
        // the product's default applies. The collection method is the third and least obvious required field:
        // Maxio assesses the first period at creation, and without a non-card method it tries to charge a card
        // that this flow never captured.
        var collectionMethod = await ResolveCollectionMethodAsync(cancellationToken).ConfigureAwait(false);

        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = planHandle,
                CustomerId = customerId,
                PaymentCollectionMethod = collectionMethod
            }
        };

        try
        {
            var response = await InvokeAsync("create subscription", async token =>
            {
                try
                {
                    return await _client.Subscriptions.CreateSubscription(body, ct: token).ConfigureAwait(false);
                }
                catch (SdkException<CreateSubscriptionError> ex)
                {
                    if (ex.Error.TryGetErrorListResponse1(out var errors))
                    {
                        // 422 — Maxio's own validation messages for the request we sent.
                        throw new BillingException(
                            BillingFailureKind.InvalidRequest,
                            "Maxio rejected the subscription request.",
                            errors.Errors,
                            (int)HttpStatusCode.UnprocessableEntity,
                            ex);
                    }

                    if (ex.Error.TryGetRawError(out var raw))
                    {
                        throw TranslateRawError("create subscription", raw, ex);
                    }

                    throw TranslateUnrecognized("create subscription", ex);
                }
            }, cancellationToken, singleSend: true).ConfigureAwait(false);

            var subscription = response.Subscription;
            if (subscription?.Id is null)
            {
                throw new BillingException(
                    BillingFailureKind.Unreadable,
                    "Maxio accepted the subscription but did not return it.");
            }

            // Deliberately not asserting a particular state here: which state an invoiced (rather than
            // card-charged) signup lands in is Maxio's to decide, and rejecting an unfamiliar one would fail a
            // subscription that was in fact created. Record it instead.
            _logger.LogInformation(
                "Maxio subscription {SubscriptionId} created with collection method {CollectionMethod}; state {State}.",
                subscription.Id, collectionMethod.Value, subscription.State?.Value);

            return subscription;
        }
        catch (BillingException ex) when (ex.Kind == BillingFailureKind.OutcomeUnknown)
        {
            // The request may have reached Maxio before the connection failed. Establish what actually
            // happened instead of assuming nothing did.
            var reconciled = await FindLiveSubscriptionAsync(customerId, planHandle, cancellationToken).ConfigureAwait(false);
            if (reconciled is not null)
            {
                _logger.LogWarning(
                    "A subscribe for customer {CustomerId} failed in transport but had already taken effect as subscription {SubscriptionId}.",
                    customerId, reconciled.Id);
                return reconciled;
            }

            throw;
        }
    }

    private static bool IsLive(Subscription subscription)
    {
        var state = subscription.State?.Value;

        // Maxio may introduce states this build does not know. Treating an unknown value as live is the safe
        // direction: the worst case is telling the shopper they are already subscribed.
        return string.IsNullOrWhiteSpace(state) || !TerminalStates.Contains(state!);
    }

    // ---------------------------------------------------------------------------------------------------
    // Mapping
    // ---------------------------------------------------------------------------------------------------

    private static SubscriptionPlan MapPlan(Product product, string? siteCurrency) => new(
        Handle: product.Handle!,
        Name: string.IsNullOrWhiteSpace(product.Name) ? product.Handle! : product.Name!,
        Description: product.Description,
        PriceInCents: product.PriceInCents ?? 0L,
        Currency: siteCurrency,
        Interval: product.Interval,
        IntervalUnit: product.IntervalUnit?.Value,
        RequiresPaymentProfileAtSignup: product.RequireCreditCard ?? false,
        ProductFamilyHandle: product.ProductFamily?.Handle);

    private static CustomerSubscription MapSubscription(Subscription subscription, SubscriptionPlan? plan, string? siteCurrency)
    {
        // Every member of the nested product is nullable and the generated type cannot say which ones this
        // endpoint populates, so read them best-effort and fall back to the plan already resolved rather than
        // showing the shopper a blank plan.
        var handle = Coalesce(subscription.Product?.Handle, plan?.Handle);
        var name = Coalesce(subscription.Product?.Name, plan?.Name, handle) ?? "Unknown plan";

        return new CustomerSubscription(
            Id: subscription.Id ?? 0,
            PlanHandle: handle,
            PlanName: name,
            PriceInCents: subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? plan?.PriceInCents,
            Currency: Coalesce(subscription.Currency, plan?.Currency, siteCurrency),
            State: Coalesce(subscription.State?.Value) ?? "unknown",
            NextBillingDate: subscription.NextAssessmentAt,
            CurrentPeriodStartedAt: subscription.CurrentPeriodStartedAt,
            CurrentPeriodEndsAt: subscription.CurrentPeriodEndsAt,
            ActivatedAt: subscription.ActivatedAt,
            CustomerId: subscription.Customer?.Id);
    }

    private static SubscriptionPlan? FindPlan(IReadOnlyList<SubscriptionPlan> plans, string? handle) =>
        string.IsNullOrWhiteSpace(handle)
            ? null
            : plans.FirstOrDefault(plan => string.Equals(plan.Handle, handle, StringComparison.OrdinalIgnoreCase));

    private static string? Coalesce(params string?[] candidates) =>
        candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate));

    private TimeSpan CacheLifetime => TimeSpan.FromSeconds(Math.Max(1, _settings.CatalogCacheSeconds));

    // ---------------------------------------------------------------------------------------------------
    // The error boundary
    // ---------------------------------------------------------------------------------------------------

    private void EnsureConfigured()
    {
        var missing = _settings.Validate();
        if (missing.Count == 0) return;

        throw new BillingException(
            BillingFailureKind.NotConfigured,
            "Subscription billing is not configured for this deployment.",
            missing);
    }

    /// <summary>
    /// Runs one logical SDK call: bounds it, opens the ambient scope the message handlers need, and converts
    /// every failure mode into a <see cref="BillingException"/>.
    /// </summary>
    /// <remarks>
    /// Per-attempt timeouts — the SDK's retry timeout and <c>HttpClient.Timeout</c> — do not bound a whole
    /// call; only a cancellation token does. So the budget lives here, in one place, rather than at each call
    /// site, where a newly added operation would silently miss it.
    /// </remarks>
    private async Task<T> InvokeAsync<T>(
        string operation,
        Func<CancellationToken, Task<T>> call,
        CancellationToken cancellationToken,
        bool singleSend = false)
    {
        using var scope = MaxioCallScope.Begin(singleSend);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _settings.CallBudgetSeconds)));

        try
        {
            return await call(budget.Token).ConfigureAwait(false);
        }
        catch (BillingException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller went away. Not a billing failure.
            throw;
        }
        catch (OperationCanceledException ex)
        {
            throw Unresolved(operation, scope, $"Maxio did not respond within {_settings.CallBudgetSeconds} seconds.", ex);
        }
        catch (MaxioDuplicateSendBlockedException ex)
        {
            _logger.LogError(ex, "Blocked a retry of the Maxio {Operation} write.", operation);
            throw new BillingException(
                BillingFailureKind.OutcomeUnknown,
                "The billing request was dispatched but its outcome could not be confirmed.",
                providerStatusCode: scope.LastStatusCode,
                innerException: ex);
        }
        catch (HttpRequestException ex)
        {
            throw Unresolved(operation, scope, "Maxio could not be reached.", ex);
        }
        catch (JsonException ex)
        {
            throw TranslateJsonFailure(operation, scope, ex);
        }
    }

    /// <summary>
    /// A transport failure on a write is not "it did not happen": the bytes may have arrived. Say so, so the
    /// caller reconciles instead of retrying blindly.
    /// </summary>
    private BillingException Unresolved(string operation, MaxioCallScope scope, string message, Exception inner)
    {
        if (scope.SingleSend && scope.AnySendAuthorized)
        {
            _logger.LogError(inner, "Maxio {Operation} failed after the request was dispatched.", operation);
            return new BillingException(
                BillingFailureKind.OutcomeUnknown,
                "The billing request was dispatched but its outcome could not be confirmed.",
                innerException: inner);
        }

        _logger.LogError(inner, "Maxio {Operation} failed.", operation);
        return new BillingException(BillingFailureKind.Unavailable, message, innerException: inner);
    }

    /// <summary>
    /// A <see cref="JsonException"/> reaches this boundary from two directions that mean opposite things: an
    /// unreadable success body, where the outcome is genuinely unknown, and an unreadable <em>error</em>
    /// body, where the SDK throws while building the error object and destroys the HTTP status with it.
    /// Answering "server error" to a deterministic rejection would tell a retrying caller to keep retrying
    /// something that can never succeed, so the status observed on the wire is recovered from the call scope.
    /// </summary>
    private BillingException TranslateJsonFailure(string operation, MaxioCallScope scope, JsonException exception)
    {
        var status = scope.LastStatusCode;

        if (status is >= 400 and < 600)
        {
            _logger.LogError(exception,
                "Maxio {Operation} was rejected with HTTP {StatusCode} and the error body could not be parsed.",
                operation, status);

            return new BillingException(
                KindForStatus(status.Value),
                "Maxio rejected the request and did not explain why.",
                providerStatusCode: status,
                innerException: exception);
        }

        _logger.LogError(exception, "Maxio {Operation} returned a response that could not be processed.", operation);

        return new BillingException(
            BillingFailureKind.Unreadable,
            "Maxio returned a response that could not be processed.",
            providerStatusCode: status,
            innerException: exception);
    }

    private BillingException TranslateRawError(string operation, RawError error, Exception inner)
    {
        var status = (int)error.StatusCode;
        _logger.LogError("Maxio {Operation} failed with HTTP {StatusCode}: {Body}", operation, status, ReadBody(error));

        return new BillingException(
            KindForStatus(status),
            MessageForStatus(status),
            providerStatusCode: status,
            innerException: inner);
    }

    private BillingException TranslateUnrecognized(string operation, Exception inner)
    {
        // Reached only if the SDK grows an error case this build does not enumerate.
        _logger.LogError(inner, "Maxio {Operation} failed with an unrecognized error shape.", operation);
        return new BillingException(BillingFailureKind.Unavailable, "Maxio reported an unexpected failure.", innerException: inner);
    }

    private string ReadBody(RawError error)
    {
        try
        {
            return error.ReadAsString();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "The Maxio error body could not be read as text.");
            return "<unreadable>";
        }
    }

    private static BillingFailureKind KindForStatus(int status) => status switch
    {
        (int)HttpStatusCode.NotFound => BillingFailureKind.NotFound,
        (int)HttpStatusCode.Conflict => BillingFailureKind.Conflict,
        (int)HttpStatusCode.Unauthorized or (int)HttpStatusCode.Forbidden => BillingFailureKind.NotPermitted,
        (int)HttpStatusCode.TooManyRequests => BillingFailureKind.Unavailable,
        >= 400 and < 500 => BillingFailureKind.InvalidRequest,
        _ => BillingFailureKind.Unavailable
    };

    private static string MessageForStatus(int status) => KindForStatus(status) switch
    {
        BillingFailureKind.NotFound => "Maxio does not have the requested record.",
        BillingFailureKind.Conflict => "Maxio rejected the request because it conflicts with existing billing data.",
        BillingFailureKind.NotPermitted => "eShopOnWeb is not authorized to perform this billing operation.",
        BillingFailureKind.InvalidRequest => "Maxio rejected the billing request.",
        _ => "Maxio is currently unavailable."
    };
}
