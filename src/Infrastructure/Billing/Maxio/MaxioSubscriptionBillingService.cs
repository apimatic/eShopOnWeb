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

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// <see cref="ISubscriptionBillingService"/> backed by Maxio Advanced Billing, which is the system of
/// record for plans, customers and subscriptions — eShopOnWeb persists none of it.
/// </summary>
internal sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const string ProductFamilyIdCacheKey = "Maxio:ProductFamilyId";
    private const string SiteSettingsCacheKey = "Maxio:SiteSettings";
    private const string PlansCacheKey = "Maxio:Plans";

    private const int PlansPageSize = 100;
    private const int MaxPlanPages = 25;

    /// <summary>
    /// Subscription states that block a second enrollment in the same plan. These are the SDK's own
    /// "Live States", plus <c>awaiting_signup</c>: an awaiting-signup subscription means a signup is
    /// already in flight, which is exactly the double-click we must not duplicate. Note that
    /// <c>assessing</c> and <c>pending</c> are documented as internal/transient — they are good enough to
    /// block a duplicate create, but they are not an access grant.
    /// </summary>
    private static readonly HashSet<string> BlockingSubscriptionStates = new(StringComparer.OrdinalIgnoreCase)
    {
        SubscriptionState.Active.Value,
        SubscriptionState.Assessing.Value,
        SubscriptionState.Pending.Value,
        SubscriptionState.Trialing.Value,
        SubscriptionState.Paused.Value,
        SubscriptionState.AwaitingSignup.Value
    };

    private readonly MaxioAdvancedBillingClient _client;
    private readonly IOptionsMonitor<MaxioOptions> _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;
    private readonly KeyedAsyncLock _subscriberLocks = new();
    private readonly KeyedAsyncLock _cacheLocks = new();

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        IOptionsMonitor<MaxioOptions> options,
        IMemoryCache cache,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _options = options;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        return await GetPlansAsync(forceRefresh: false, cancellationToken);
    }

    public async Task<SubscribeResult> SubscribeAsync(
        SubscriberIdentity subscriber,
        string planHandle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new BillingException(BillingFailureKind.InvalidRequest, "A plan handle is required.");
        }

        planHandle = planHandle.Trim();
        var plan = await ResolvePlanAsync(planHandle, cancellationToken);

        if (plan.RequiresPaymentMethod)
        {
            // A fast fail, not the authority: Maxio decides whether payment information is required, and a
            // rejection from CreateSubscription is surfaced on its own terms below.
            throw new BillingException(
                BillingFailureKind.InvalidRequest,
                $"Plan '{plan.Handle}' requires a payment method, which this API does not collect.");
        }

        // Serialize per shopper so a double-click cannot race itself past the pre-existing-subscription check.
        using var _ = await _subscriberLocks.AcquireAsync(subscriber.Reference, cancellationToken);

        var customerId = await FindOrCreateCustomerAsync(subscriber, cancellationToken);

        var existing = await FindBlockingSubscriptionAsync(customerId, planHandle, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Maxio customer {CustomerId} is already subscribed to {PlanHandle} (subscription {SubscriptionId}, state {State}); returning the existing subscription.",
                customerId, planHandle, existing.Id, existing.State);

            return new SubscribeResult(existing, Created: false);
        }

        var created = await CreateSubscriptionAsync(customerId, planHandle, cancellationToken);

        _logger.LogInformation(
            "Created Maxio subscription {SubscriptionId} for customer {CustomerId} on plan {PlanHandle} (state {State}).",
            created.Id, customerId, planHandle, created.State);

        return new SubscribeResult(created, Created: true);
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(
        SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        EnsureConfigured();

        var customer = await FindCustomerByReferenceAsync(MaxioReferenceFor(subscriber), cancellationToken);
        if (customer?.Id is not int customerId)
        {
            // No billing customer yet: the shopper has never subscribed. Reading must not create one.
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, cancellationToken);

        return subscriptions
            .Select(MapSubscription)
            .Where(subscription => subscription is not null)
            .Select(subscription => subscription!)
            .OrderByDescending(subscription => subscription.CreatedAt ?? DateTimeOffset.MinValue)
            .ToList();
    }

    // ---------------------------------------------------------------------------------------------
    // Catalog
    // ---------------------------------------------------------------------------------------------

    private async Task<SubscriptionPlan> ResolvePlanAsync(string planHandle, CancellationToken cancellationToken)
    {
        var plans = await GetPlansAsync(forceRefresh: false, cancellationToken);
        var plan = FindPlan(plans, planHandle);

        if (plan is null)
        {
            // The catalog may have changed since the list was cached — re-read once before rejecting.
            plans = await GetPlansAsync(forceRefresh: true, cancellationToken);
            plan = FindPlan(plans, planHandle);
        }

        return plan ?? throw new BillingException(
            BillingFailureKind.NotFound,
            $"No subscription plan with handle '{planHandle}' is available.");
    }

    private static SubscriptionPlan? FindPlan(IReadOnlyList<SubscriptionPlan> plans, string planHandle) =>
        plans.FirstOrDefault(plan => string.Equals(plan.Handle, planHandle, StringComparison.OrdinalIgnoreCase));

    private async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        if (forceRefresh)
        {
            _cache.Remove(PlansCacheKey);
        }

        return await GetOrCreateAsync(
            PlansCacheKey,
            async ct =>
            {
                var familyId = await GetProductFamilyIdAsync(ct);
                var site = await GetSiteSettingsAsync(ct);
                var products = await ListProductsAsync(familyId, ct);

                return (IReadOnlyList<SubscriptionPlan>)products
                    .Where(product => !string.IsNullOrWhiteSpace(product.Handle))
                    .Where(product => product.ArchivedAt is null)
                    .Select(product => new SubscriptionPlan(
                        Handle: product.Handle!,
                        Name: product.Name ?? product.Handle!,
                        Description: product.Description,
                        PriceInCents: product.PriceInCents ?? 0,
                        Currency: site.Currency,
                        Interval: product.Interval,
                        IntervalUnit: product.IntervalUnit?.Value,
                        // require_credit_card is the flag that controls whether a payment profile must be
                        // entered to sign up. request_credit_card is a deprecated legacy hosted-page value
                        // and is deliberately not consulted.
                        RequiresPaymentMethod: product.RequireCreditCard ?? false))
                    .OrderBy(plan => plan.PriceInCents)
                    .ToList();
            },
            cancellationToken);
    }

    private async Task<int> GetProductFamilyIdAsync(CancellationToken cancellationToken)
    {
        var handle = _options.CurrentValue.ProductFamilyHandle!.Trim();

        var boxed = await GetOrCreateAsync(
            ProductFamilyIdCacheKey,
            async ct =>
            {
                var families = await InvokeAsync(
                    "ListProductFamilies",
                    writeOnce: false,
                    call: async token => await _client.ProductFamilies.ListProductFamilies(
                        dateField: null,
                        startDate: null,
                        endDate: null,
                        startDatetime: null,
                        endDatetime: null,
                        ct: token),
                    onSdkError: RawErrorTranslator("ListProductFamilies", "Could not read the Maxio product families."),
                    cancellationToken: ct);

                var match = families
                    .Select(response => response.ProductFamily)
                    .FirstOrDefault(family =>
                        family is not null
                        && string.Equals(family.Handle, handle, StringComparison.OrdinalIgnoreCase));

                if (match?.Id is not int id)
                {
                    throw new BillingException(
                        BillingFailureKind.Configuration,
                        $"No Maxio product family with handle '{handle}' exists on this site. Check Maxio:ProductFamilyHandle.");
                }

                return (object)id;
            },
            cancellationToken);

        return (int)boxed;
    }

    /// <summary>
    /// Reads the settings the plan catalog and the subscribe call need from the site itself: Maxio's
    /// product model carries no currency, and the valid set of payment-collection methods depends on which
    /// invoicing architecture the site runs.
    /// </summary>
    private async Task<MaxioSiteSettings> GetSiteSettingsAsync(CancellationToken cancellationToken) =>
        await GetOrCreateAsync(
            SiteSettingsCacheKey,
            async ct =>
            {
                var response = await InvokeAsync(
                    "ReadSite",
                    writeOnce: false,
                    call: async token => await _client.Sites.ReadSite(ct: token),
                    onSdkError: RawErrorTranslator("ReadSite", "Could not read the Maxio site settings."),
                    cancellationToken: ct);

                var site = response.Site;

                return new MaxioSiteSettings(
                    Currency: string.IsNullOrWhiteSpace(site.Currency) ? null : site.Currency,
                    RelationshipInvoicingEnabled: site.RelationshipInvoicingEnabled ?? false,
                    DefaultPaymentCollectionMethod: site.DefaultPaymentCollectionMethod);
            },
            cancellationToken);

    /// <summary>
    /// Chooses how Maxio should collect payment for a new subscription.
    /// <para>
    /// This API captures no card, so the site's default collection method — which on a typical site
    /// attempts an immediate charge — would reject every subscribe with "no payment method on file". The
    /// subscription is therefore created in the site's invoice-and-await-payment mode instead. Which enum
    /// value expresses that depends on the site's invoicing architecture: Relationship Invoicing accepts
    /// remittance/automatic/prepaid, while legacy Statements accepts invoice/automatic.
    /// </para>
    /// </summary>
    private async Task<CollectionMethod> ResolveCollectionMethodAsync(CancellationToken cancellationToken)
    {
        var configured = _options.CurrentValue.PaymentCollectionMethod;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var method = CollectionMethod.FromValue(configured.Trim().ToLowerInvariant());
            if (!method.IsKnownValue())
            {
                _logger.LogWarning(
                    "Maxio:PaymentCollectionMethod is set to '{Configured}', which this SDK does not recognise. Sending it anyway.",
                    configured);
            }

            return method;
        }

        var site = await GetSiteSettingsAsync(cancellationToken);

        return site.RelationshipInvoicingEnabled ? CollectionMethod.Remittance : CollectionMethod.Invoice;
    }

    private async Task<IReadOnlyList<Product>> ListProductsAsync(int familyId, CancellationToken cancellationToken)
    {
        var familyIdText = familyId.ToString(CultureInfo.InvariantCulture);
        var products = new List<Product>();

        for (var page = 1; page <= MaxPlanPages; page++)
        {
            var pageNumber = page;

            IReadOnlyList<ProductResponse> responses;
            try
            {
                responses = await InvokeAsync(
                    "ListProductsForProductFamily",
                    writeOnce: false,
                    call: async token => await _client.ProductFamilies.ListProductsForProductFamily(
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
                        perPage: PlansPageSize,
                        ct: token),
                    onSdkError: null,
                    cancellationToken: cancellationToken);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out var notFoundBody))
                {
                    _logger.LogError("Maxio rejected product family {FamilyId}: {Body}", familyIdText, notFoundBody);
                    throw new BillingException(
                        BillingFailureKind.Configuration,
                        "The configured Maxio product family is no longer available.",
                        (int)HttpStatusCode.NotFound,
                        innerException: ex);
                }

                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw Translate(raw, "ListProductsForProductFamily", "Could not read the Maxio plan catalog.", ex);
                }

                throw new BillingException(
                    BillingFailureKind.Unknown,
                    "Could not read the Maxio plan catalog.",
                    innerException: ex);
            }

            products.AddRange(responses.Select(response => response.Product));

            if (responses.Count < PlansPageSize)
            {
                return products;
            }
        }

        _logger.LogWarning(
            "Stopped reading the Maxio plan catalog after {MaxPages} pages of {PageSize}.", MaxPlanPages, PlansPageSize);

        return products;
    }

    // ---------------------------------------------------------------------------------------------
    // Customers
    // ---------------------------------------------------------------------------------------------

    private async Task<int> FindOrCreateCustomerAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken)
    {
        var reference = MaxioReferenceFor(subscriber);

        var existing = await FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing?.Id is int existingId)
        {
            return existingId;
        }

        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = subscriber.FirstName,
                LastName = subscriber.LastName,
                Email = subscriber.Email,
                Reference = reference
            }
        };

        Customer created;
        try
        {
            var response = await InvokeAsync(
                "CreateCustomer",
                writeOnce: true,
                call: async token => await _client.Customers.CreateCustomer(body: body, ct: token),
                onSdkError: null,
                cancellationToken: cancellationToken);

            created = response.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // Maxio requires customer references to be unique. A rejection here is most likely a concurrent
            // caller that won the race, so re-read before treating it as a failure.
            var messages = DescribeCreateCustomerError(ex.Error, out var statusCode);
            var recovered = await FindCustomerByReferenceAsync(reference, cancellationToken);
            if (recovered?.Id is int recoveredId)
            {
                return recoveredId;
            }

            throw Translate(
                statusCode,
                "CreateCustomer",
                "Maxio rejected the billing customer for this account.",
                messages,
                ex);
        }
        catch (BillingException ex) when (ex.Kind == BillingFailureKind.OutcomeUnknown)
        {
            // The create may or may not have landed. Settle it by re-reading rather than guessing.
            var recovered = await FindCustomerByReferenceAsync(reference, cancellationToken);
            if (recovered?.Id is int recoveredId)
            {
                return recoveredId;
            }

            throw;
        }

        if (created.Id is not int createdId)
        {
            throw new BillingException(
                BillingFailureKind.Unknown,
                "Maxio created a billing customer but did not return its id.");
        }

        _logger.LogInformation(
            "Created Maxio customer {CustomerId} for reference {Reference}.", createdId, reference);

        return createdId;
    }

    private async Task<Customer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await InvokeAsync(
                "ReadCustomerByReference",
                writeOnce: false,
                call: async token => await _client.Customers.ReadCustomerByReference(reference: reference, ct: token),
                onSdkError: null,
                cancellationToken: cancellationToken);

            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogDebug("No Maxio customer exists for reference {Reference}.", reference);
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex.Error, "ReadCustomerByReference", "Could not look up the billing customer.", ex);
        }
        catch (BillingException ex) when (IsEmptySuccessBody(ex))
        {
            // CustomerResponse.Customer is a required member, so a 2xx carrying no customer cannot be
            // deserialized and arrives here as an unreadable success rather than a 404. For this one
            // lookup that shape *is* the miss — it is matched narrowly (2xx only) so that a genuinely
            // corrupt body on any other status still fails loudly instead of being read as "no customer".
            _logger.LogWarning(
                ex, "Maxio answered the customer lookup for {Reference} with a success status but no customer; treating it as not found.",
                reference);

            return null;
        }
    }

    private static bool IsEmptySuccessBody(BillingException exception) =>
        exception.Kind == BillingFailureKind.Unknown
        && exception.InnerException is JsonException
        && exception.ProviderStatusCode is null or (>= 200 and < 300);

    private static IReadOnlyList<string> DescribeCreateCustomerError(CreateCustomerError error, out int? statusCode)
    {
        statusCode = null;
        var messages = new List<string>();

        // The generated 422 model for this operation only covers per_page/price_point, so it will usually
        // yield nothing usable — read it anyway, then fall back to the raw body.
        if (error.TryGetCustomerErrorResponse1(out var typed))
        {
            statusCode = (int)HttpStatusCode.UnprocessableEntity;
            AddAll(messages, typed?.Errors?.PerPage);
            AddAll(messages, typed?.Errors?.PricePoint);
        }
        else if (error.TryGetRawError(out var raw))
        {
            statusCode = (int)raw.StatusCode;
        }

        return messages;
    }

    private static void AddAll(List<string> target, IReadOnlyList<string>? source)
    {
        if (source is null)
        {
            return;
        }

        target.AddRange(source.Where(message => !string.IsNullOrWhiteSpace(message)));
    }

    // ---------------------------------------------------------------------------------------------
    // Subscriptions
    // ---------------------------------------------------------------------------------------------

    private async Task<CustomerSubscription?> FindBlockingSubscriptionAsync(
        int customerId,
        string planHandle,
        CancellationToken cancellationToken)
    {
        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, cancellationToken);

        var match = subscriptions.FirstOrDefault(subscription =>
            string.Equals(subscription.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase)
            && subscription.State?.Value is string state
            && BlockingSubscriptionStates.Contains(state));

        return match is null ? null : MapSubscription(match);
    }

    private async Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken)
    {
        var responses = await InvokeAsync(
            "ListCustomerSubscriptions",
            writeOnce: false,
            call: async token => await _client.Customers.ListCustomerSubscriptions(customerId: customerId, ct: token),
            onSdkError: RawErrorTranslator("ListCustomerSubscriptions", "Could not read the subscriptions for this account."),
            cancellationToken: cancellationToken);

        return responses
            .Select(response => response.Subscription)
            .Where(subscription => subscription is not null)
            .Select(subscription => subscription!)
            .ToList();
    }

    private async Task<CustomerSubscription> CreateSubscriptionAsync(
        int customerId,
        string planHandle,
        CancellationToken cancellationToken)
    {
        var collectionMethod = await ResolveCollectionMethodAsync(cancellationToken);

        // Neither a payment profile nor card attributes are set: that is how "no payment method" is
        // expressed. The collection method is what keeps Maxio from trying to charge one anyway.
        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                CustomerId = customerId,
                ProductHandle = planHandle,
                PaymentCollectionMethod = collectionMethod
            }
        };

        try
        {
            var response = await InvokeAsync(
                "CreateSubscription",
                writeOnce: true,
                call: async token => await _client.Subscriptions.CreateSubscription(body: body, ct: token),
                onSdkError: null,
                cancellationToken: cancellationToken);

            var mapped = response.Subscription is null ? null : MapSubscription(response.Subscription);

            return mapped ?? throw new BillingException(
                BillingFailureKind.Unknown,
                "Maxio accepted the subscription but did not return it.");
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            var messages = DescribeCreateSubscriptionError(ex.Error, out var statusCode);

            throw Translate(
                statusCode,
                "CreateSubscription",
                "Maxio rejected the subscription request.",
                messages,
                ex);
        }
        catch (BillingException ex) when (ex.Kind == BillingFailureKind.OutcomeUnknown)
        {
            // The write may have landed. Re-read before reporting failure, so a transport blip does not
            // hide a subscription the shopper is now being billed for.
            var reconciled = await FindBlockingSubscriptionAsync(customerId, planHandle, cancellationToken);
            if (reconciled is not null)
            {
                _logger.LogWarning(
                    "Reconciled an unknown-outcome subscribe for customer {CustomerId} on {PlanHandle}: subscription {SubscriptionId} exists.",
                    customerId, planHandle, reconciled.Id);

                return reconciled;
            }

            throw;
        }
    }

    private static IReadOnlyList<string> DescribeCreateSubscriptionError(CreateSubscriptionError error, out int? statusCode)
    {
        statusCode = null;
        var messages = new List<string>();

        if (error.TryGetErrorListResponse1(out var typed))
        {
            statusCode = (int)HttpStatusCode.UnprocessableEntity;
            AddAll(messages, typed?.Errors);
        }
        else if (error.TryGetRawError(out var raw))
        {
            statusCode = (int)raw.StatusCode;
        }

        return messages;
    }

    private static CustomerSubscription? MapSubscription(Subscription subscription)
    {
        if (subscription.Id is not int id)
        {
            return null;
        }

        return new CustomerSubscription(
            Id: id,
            State: subscription.State?.Value ?? "unknown",
            PlanHandle: subscription.Product?.Handle,
            PlanName: subscription.Product?.Name,
            PriceInCents: subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents,
            Currency: subscription.Currency,
            Interval: subscription.Product?.Interval,
            IntervalUnit: subscription.Product?.IntervalUnit?.Value,
            // current_period_ends_at is the field Maxio itself points at for the next billing date;
            // next_assessment_at is the fallback when it is absent.
            NextBillingDate: subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
            CurrentPeriodStartedAt: subscription.CurrentPeriodStartedAt,
            CreatedAt: subscription.CreatedAt,
            CustomerId: subscription.Customer?.Id);
    }

    // ---------------------------------------------------------------------------------------------
    // Call boundary
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Runs one Maxio operation under a total time budget, a write-once guard when the operation is a
    /// write, and the failure translation that is identical for every operation. Failures whose handling
    /// is operation-specific (typed error models) are left for the caller's own <c>catch</c>.
    /// </summary>
    private async Task<T> InvokeAsync<T>(
        string operation,
        bool writeOnce,
        Func<CancellationToken, Task<T>> call,
        Func<RawError, Exception, BillingException>? onSdkError,
        CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;

        using var scope = writeOnce
            ? MaxioCallScope.BeginWriteOnce(operation)
            : MaxioCallScope.BeginRead(operation);

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(options.RequestTimeout);

        try
        {
            return await call(budget.Token);
        }
        catch (SdkException<RawError> ex) when (onSdkError is not null)
        {
            throw onSdkError(ex.Error, ex);
        }
        catch (MaxioResendBlockedException ex)
        {
            _logger.LogError(ex, "Maxio operation {Operation} could not be completed without re-sending a write.", operation);

            throw new BillingException(
                BillingFailureKind.OutcomeUnknown,
                "The billing request could not be confirmed. Please check your subscriptions before retrying.",
                innerException: ex);
        }
        catch (JsonException ex)
        {
            // Two very different failures arrive as JsonException: an unreadable success body (outcome
            // genuinely unknown) and an error body that does not match its generated model (a rejection
            // whose status the SDK destroyed). The status recorded on the wire tells them apart.
            var status = scope.LastStatusCode;

            _logger.LogError(
                ex, "Maxio operation {Operation} returned a body that could not be processed (status {Status}).",
                operation, status is null ? "unknown" : ((int)status.Value).ToString(CultureInfo.InvariantCulture));

            if (status is HttpStatusCode observed && (int)observed >= 400)
            {
                throw new BillingException(
                    KindFor((int)observed),
                    "Maxio rejected the request and the reason could not be read.",
                    (int)observed,
                    innerException: ex);
            }

            throw new BillingException(
                writeOnce ? BillingFailureKind.OutcomeUnknown : BillingFailureKind.Unknown,
                "Maxio returned a response that could not be processed.",
                status is null ? null : (int)status.Value,
                innerException: ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(
                ex, "Maxio operation {Operation} exceeded its {Budget} budget.", operation, options.RequestTimeout);

            throw new BillingException(
                writeOnce ? BillingFailureKind.OutcomeUnknown : BillingFailureKind.Unavailable,
                writeOnce
                    ? "The billing request timed out and could not be confirmed. Please check your subscriptions before retrying."
                    : "The billing system did not respond in time. Please try again.",
                innerException: ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Maxio operation {Operation} could not reach the billing system.", operation);

            throw new BillingException(
                writeOnce ? BillingFailureKind.OutcomeUnknown : BillingFailureKind.Unavailable,
                writeOnce
                    ? "The billing request could not be confirmed. Please check your subscriptions before retrying."
                    : "The billing system is currently unreachable. Please try again.",
                innerException: ex);
        }
    }

    private Func<RawError, Exception, BillingException> RawErrorTranslator(string operation, string message) =>
        (raw, inner) => Translate(raw, operation, message, inner);

    private BillingException Translate(RawError raw, string operation, string message, Exception inner)
    {
        var status = (int)raw.StatusCode;
        _logger.LogError(
            inner, "Maxio operation {Operation} failed with status {Status}: {Body}",
            operation, status, ReadBodySafely(raw));

        return new BillingException(KindFor(status), message, status, innerException: inner);
    }

    private BillingException Translate(
        int? statusCode,
        string operation,
        string message,
        IReadOnlyList<string> providerMessages,
        Exception inner)
    {
        _logger.LogError(
            inner, "Maxio operation {Operation} failed with status {Status}: {Messages}",
            operation,
            statusCode?.ToString(CultureInfo.InvariantCulture) ?? "unknown",
            providerMessages.Count == 0 ? "(no detail)" : string.Join("; ", providerMessages));

        return new BillingException(
            statusCode is int status ? KindFor(status) : BillingFailureKind.Unknown,
            message,
            statusCode,
            providerMessages,
            inner);
    }

    private static string ReadBodySafely(RawError raw)
    {
        try
        {
            return raw.ReadAsString();
        }
        catch (Exception ex)
        {
            return $"(unreadable: {ex.GetType().Name})";
        }
    }

    private static BillingFailureKind KindFor(int statusCode) => statusCode switch
    {
        400 => BillingFailureKind.InvalidRequest,
        401 or 403 => BillingFailureKind.Configuration,
        404 => BillingFailureKind.NotFound,
        409 or 422 => BillingFailureKind.Rejected,
        408 or 429 => BillingFailureKind.Unavailable,
        >= 500 => BillingFailureKind.Unavailable,
        _ => BillingFailureKind.Unknown
    };

    /// <summary>
    /// Namespaces the application's stable user key into the Maxio customer <c>reference</c>, so one Maxio
    /// site can host more than one application without reference collisions.
    /// </summary>
    private string MaxioReferenceFor(SubscriberIdentity subscriber)
    {
        var prefix = _options.CurrentValue.CustomerReferencePrefix?.Trim();

        return string.IsNullOrEmpty(prefix)
            ? subscriber.Reference
            : $"{prefix}:{subscriber.Reference}";
    }

    private void EnsureConfigured()
    {
        var problem = _options.CurrentValue.Validate();
        if (problem is null)
        {
            return;
        }

        _logger.LogError("Maxio billing is not configured: {Problem}", problem);

        throw new BillingException(
            BillingFailureKind.Configuration,
            "Subscription billing is not configured on this server.");
    }

    private async Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken)
        where T : class
    {
        if (_cache.TryGetValue(key, out T? cached) && cached is not null)
        {
            return cached;
        }

        // Locked per key, not globally: a cache load can itself load another cached value (the plan list
        // needs the family id and the site currency), and a single shared lock would deadlock on that.
        using var _ = await _cacheLocks.AcquireAsync(key, cancellationToken);

        if (_cache.TryGetValue(key, out cached) && cached is not null)
        {
            return cached;
        }

        var value = await factory(cancellationToken);
        _cache.Set(key, value, _options.CurrentValue.CatalogCacheDuration);
        return value;
    }
}
