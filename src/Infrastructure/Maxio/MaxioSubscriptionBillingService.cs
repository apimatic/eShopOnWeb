using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Domain = Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maxio Advanced Billing adapter: the only place in the application that knows the billing provider's
/// types. Everything above it works with <see cref="ISubscriptionBillingService"/> and
/// <see cref="SubscriptionBillingException"/>.
/// </summary>
public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly SubscriberEnrollmentLock _enrollmentLock;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    private readonly SemaphoreSlim _familyLookupGate = new(1, 1);
    private readonly CollectionMethod? _paymentCollectionMethod;
    private CachedProductFamily? _cachedFamily;
    private int _creditCardFlagDisagreementLogged;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> options,
        SubscriberEnrollmentLock enrollmentLock,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _options = options.Value;
        _enrollmentLock = enrollmentLock;
        _logger = logger;

        // Resolved once: the configured value is a wire string, and CollectionMethod is a string-enum
        // record rather than a C# enum, so it is built through FromValue.
        _paymentCollectionMethod = string.IsNullOrWhiteSpace(_options.PaymentCollectionMethod)
            ? null
            : CollectionMethod.FromValue(_options.PaymentCollectionMethod!.Trim().ToLowerInvariant());
    }

    public Task<IReadOnlyList<Domain.SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default) =>
        WithBudgetAsync(async ct =>
        {
            var products = await ListFamilyProductsAsync(ct);
            return (IReadOnlyList<Domain.SubscriptionPlan>)products
                .Where(product => product.ArchivedAt is null)
                .Select(MapPlan)
                .OrderBy(plan => plan.PriceInCents ?? long.MaxValue)
                .ThenBy(plan => plan.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }, cancellationToken);

    public async Task<Domain.SubscriptionEnrollment> SubscribeAsync(
        Domain.SubscriberIdentity subscriber,
        string planHandle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new SubscriptionBillingException(
                SubscriptionBillingFailure.InvalidRequest,
                "A plan handle is required.");
        }

        var handle = planHandle.Trim();

        // Serialize enrollment per subscriber so two concurrent requests cannot both pass the
        // "already subscribed?" check before either has created anything.
        using var gate = await _enrollmentLock.AcquireAsync(subscriber.Reference, cancellationToken);

        return await WithBudgetAsync(ct => EnrollAsync(subscriber, handle, ct), cancellationToken);
    }

    public Task<IReadOnlyList<Domain.CustomerSubscription>> GetSubscriptionsAsync(
        Domain.SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        return WithBudgetAsync(async ct =>
        {
            var customer = await FindCustomerAsync(subscriber.Reference, ct);
            if (customer?.Id is null)
            {
                return (IReadOnlyList<Domain.CustomerSubscription>)Array.Empty<Domain.CustomerSubscription>();
            }

            var subscriptions = await ListCustomerSubscriptionsAsync(customer.Id.Value, ct);
            return subscriptions
                .Select(MapSubscription)
                .OrderByDescending(subscription => subscription.CreatedAt ?? DateTimeOffset.MinValue)
                .ToList();
        }, cancellationToken);
    }

    private async Task<Domain.SubscriptionEnrollment> EnrollAsync(
        Domain.SubscriberIdentity subscriber,
        string planHandle,
        CancellationToken ct)
    {
        // Only plans from the configured product family are subscribable, so validate the handle against
        // the family rather than letting the provider reject an arbitrary product handle.
        var products = await ListFamilyProductsAsync(ct);
        var plan = products.FirstOrDefault(product =>
            string.Equals(product.Handle, planHandle, StringComparison.OrdinalIgnoreCase)
            && product.ArchivedAt is null);

        if (plan is null)
        {
            throw new SubscriptionBillingException(
                SubscriptionBillingFailure.NotFound,
                $"No subscription plan with handle '{planHandle}' is available.");
        }

        var customerId = await EnsureCustomerAsync(subscriber, ct);

        var existing = await FindOccupyingSubscriptionAsync(customerId, planHandle, ct);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Subscriber {Reference} is already enrolled on plan {PlanHandle} (subscription {SubscriptionId}); returning it unchanged.",
                subscriber.Reference,
                planHandle,
                existing.Id);

            return new Domain.SubscriptionEnrollment(MapSubscription(existing), alreadySubscribed: true);
        }

        var subscriptionReference = subscriber.SubscriptionReference(planHandle);
        var created = await CreateSubscriptionAsync(customerId, planHandle, subscriptionReference, ct);

        _logger.LogInformation(
            "Enrolled subscriber {Reference} on plan {PlanHandle} as subscription {SubscriptionId}.",
            subscriber.Reference,
            planHandle,
            created.Id);

        return new Domain.SubscriptionEnrollment(MapSubscription(created), alreadySubscribed: false);
    }

    /// <summary>
    /// Resolves the shopper's billing customer, creating one only when the lookup by reference misses.
    /// </summary>
    private async Task<int> EnsureCustomerAsync(Domain.SubscriberIdentity subscriber, CancellationToken ct)
    {
        var existing = await FindCustomerAsync(subscriber.Reference, ct);
        if (existing?.Id is not null)
        {
            return existing.Id.Value;
        }

        try
        {
            var created = await CreateCustomerAsync(subscriber, ct);
            if (created.Id is null)
            {
                throw new SubscriptionBillingException(
                    SubscriptionBillingFailure.ProviderResponseUnreadable,
                    "The billing provider created a customer without returning its identifier.");
            }

            return created.Id.Value;
        }
        catch (SubscriptionBillingException ex) when (ex.Failure is SubscriptionBillingFailure.InvalidRequest
                                                       or SubscriptionBillingFailure.OutcomeUnknown)
        {
            // The provider enforces uniqueness on the customer reference, so a rejection here - or a write
            // whose outcome we could not observe - most likely means a racing request already created it.
            // Re-read before giving up.
            var reconciled = await FindCustomerAsync(subscriber.Reference, ct);
            if (reconciled?.Id is not null)
            {
                _logger.LogInformation(
                    "Reconciled customer {Reference} to {CustomerId} after a create that did not complete cleanly.",
                    subscriber.Reference,
                    reconciled.Id);

                return reconciled.Id.Value;
            }

            throw;
        }
    }

    /// <summary>
    /// Creates the subscription, then reconciles when the write's outcome could not be observed. A blocked
    /// re-send or a transport failure does not mean nothing happened, so the provider is re-read before
    /// any failure is reported.
    /// </summary>
    private async Task<Subscription> CreateSubscriptionAsync(
        int customerId,
        string planHandle,
        string subscriptionReference,
        CancellationToken ct)
    {
        try
        {
            return await CreateSubscriptionCoreAsync(customerId, planHandle, subscriptionReference, ct);
        }
        catch (SubscriptionBillingException ex) when (ex.Failure == SubscriptionBillingFailure.OutcomeUnknown)
        {
            var reconciled = await FindOccupyingSubscriptionAsync(customerId, planHandle, ct);
            if (reconciled is not null)
            {
                _logger.LogWarning(
                    "Reconciled subscription {SubscriptionId} for customer {CustomerId} on plan {PlanHandle} after a create whose outcome was unknown.",
                    reconciled.Id,
                    customerId,
                    planHandle);

                return reconciled;
            }

            throw;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // SDK calls. Each one owns its own catch ladder: the typed accessors live on the concrete
    // per-operation error type, so they cannot be read anywhere but here.
    // ---------------------------------------------------------------------------------------------

    private async Task<int> ResolveProductFamilyIdAsync(CancellationToken ct)
    {
        var cached = _cachedFamily;
        if (cached is not null && cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return cached.Id;
        }

        await _familyLookupGate.WaitAsync(ct);
        try
        {
            cached = _cachedFamily;
            if (cached is not null && cached.ExpiresAt > DateTimeOffset.UtcNow)
            {
                return cached.Id;
            }

            var id = await LookupProductFamilyIdAsync(ct);
            _cachedFamily = new CachedProductFamily(
                id,
                DateTimeOffset.UtcNow.AddSeconds(_options.ProductFamilyCacheSeconds));

            return id;
        }
        finally
        {
            _familyLookupGate.Release();
        }
    }

    /// <summary>
    /// Finds the configured product family by handle. There is no by-handle read for product families -
    /// the read operation takes a numeric id - so the families are listed and matched here. Handles are
    /// stable across catalog re-seeds; the numeric ids are not, which is why the id is cached briefly
    /// rather than configured.
    /// </summary>
    private async Task<int> LookupProductFamilyIdAsync(CancellationToken ct)
    {
        const string operation = "ProductFamilies.ListProductFamilies";
        var handle = _options.ProductFamilyHandle!;

        IReadOnlyList<ProductFamilyResponse> families;
        try
        {
            families = await _client.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: ct);
        }
        catch (SdkException<RawError> ex)
        {
            throw MaxioFailureTranslation.FromRawError(_logger, ex.Error, operation);
        }
        catch (JsonException ex)
        {
            throw MaxioFailureTranslation.FromUnreadablePayload(_logger, ex, operation);
        }
        catch (Exception ex) when (MaxioFailureTranslation.IsTransportFailure(ex))
        {
            throw MaxioFailureTranslation.FromTransport(_logger, ex, operation, isWrite: false);
        }

        var match = families
            .Select(response => response.ProductFamily)
            .FirstOrDefault(family => family is not null
                && string.Equals(family.Handle, handle, StringComparison.OrdinalIgnoreCase));

        if (match?.Id is null)
        {
            _logger.LogError(
                "No Maxio product family with handle '{Handle}' exists on this site (saw {Count} families).",
                handle,
                families.Count);

            throw new SubscriptionBillingException(
                SubscriptionBillingFailure.ProviderMisconfigured,
                "The configured subscription product family does not exist on the billing site.");
        }

        return match.Id.Value;
    }

    private async Task<IReadOnlyList<Product>> ListFamilyProductsAsync(CancellationToken ct)
    {
        var familyId = await ResolveProductFamilyIdAsync(ct);

        try
        {
            return await ListFamilyProductsCoreAsync(familyId, ct);
        }
        catch (SubscriptionBillingException ex) when (ex.Failure == SubscriptionBillingFailure.NotFound)
        {
            // The cached id belongs to a family that no longer exists - the catalog was re-seeded and the
            // numeric ids were reassigned. Drop the cache and resolve the handle once more.
            _logger.LogWarning("Cached Maxio product family id was stale; re-resolving the configured handle.");
            _cachedFamily = null;

            var refreshedId = await ResolveProductFamilyIdAsync(ct);
            return await ListFamilyProductsCoreAsync(refreshedId, ct);
        }
    }

    private async Task<IReadOnlyList<Product>> ListFamilyProductsCoreAsync(int familyId, CancellationToken ct)
    {
        const string operation = "ProductFamilies.ListProductsForProductFamily";

        var familyIdText = familyId.ToString(CultureInfo.InvariantCulture);
        var products = new List<Product>();

        // The list endpoints return a bare array: no total, no cursor, no "has more". Page until a page
        // comes back short, and cap the loop so a provider that ignores paging cannot spin forever.
        for (var page = 1; page <= _options.MaxPages; page++)
        {
            IReadOnlyList<ProductResponse> pageItems;
            try
            {
                pageItems = await _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: familyIdText,
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: false,
                    include: null,
                    page: page,
                    perPage: _options.PageSize,
                    ct: ct);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out var notFoundMessage))
                {
                    _logger.LogWarning(
                        "Maxio operation {Operation} reported the product family as not found: {Message}",
                        operation,
                        notFoundMessage);

                    throw new SubscriptionBillingException(
                        SubscriptionBillingFailure.NotFound,
                        "The configured subscription product family was not found.",
                        404);
                }

                if (ex.Error.TryGetRawError(out RawError raw))
                {
                    throw MaxioFailureTranslation.FromRawError(_logger, raw, operation);
                }

                throw new SubscriptionBillingException(
                    SubscriptionBillingFailure.ProviderUnavailable,
                    "The billing provider returned an error that could not be interpreted.");
            }
            catch (JsonException ex)
            {
                throw MaxioFailureTranslation.FromUnreadablePayload(_logger, ex, operation);
            }
            catch (Exception ex) when (MaxioFailureTranslation.IsTransportFailure(ex))
            {
                throw MaxioFailureTranslation.FromTransport(_logger, ex, operation, isWrite: false);
            }

            products.AddRange(pageItems.Select(item => item.Product));

            if (pageItems.Count < _options.PageSize)
            {
                return products;
            }
        }

        _logger.LogWarning(
            "Stopped paging Maxio products after {MaxPages} pages; the result may be incomplete.",
            _options.MaxPages);

        return products;
    }

    private async Task<Customer?> FindCustomerAsync(string reference, CancellationToken ct)
    {
        const string operation = "Customers.ReadCustomerByReference";

        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (MaxioFailureTranslation.IsNotFound(ex.Error))
        {
            // A miss is a normal branch of enrollment, not a failure.
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw MaxioFailureTranslation.FromRawError(_logger, ex.Error, operation);
        }
        catch (JsonException ex)
        {
            // A 200 whose body has no customer cannot be read as "not enrolled": that would turn a corrupt
            // response into a spurious create.
            throw MaxioFailureTranslation.FromUnreadablePayload(_logger, ex, operation);
        }
        catch (Exception ex) when (MaxioFailureTranslation.IsTransportFailure(ex))
        {
            throw MaxioFailureTranslation.FromTransport(_logger, ex, operation, isWrite: false);
        }
    }

    private async Task<Customer> CreateCustomerAsync(Domain.SubscriberIdentity subscriber, CancellationToken ct)
    {
        const string operation = "Customers.CreateCustomer";

        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = subscriber.FirstName,
                LastName = subscriber.LastName,
                Email = subscriber.Email,
                Reference = subscriber.Reference
            }
        };

        // One network send only: the SDK re-sends a POST after a transport failure on any verb, and
        // retries cannot be disabled.
        using var writeOnce = new MaxioWriteOnceScope(operation);

        try
        {
            var response = await _client.Customers.CreateCustomer(body, ct);
            return response.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // The generated 422 shape for this operation is a shared model whose fields do not look like
            // customer validation, so read it best-effort and never let it be the only path to a response.
            var detail = TryDescribeCustomerValidationError(ex.Error);
            if (detail is not null)
            {
                _logger.LogWarning("Maxio operation {Operation} rejected the request: {Detail}", operation, detail);
                throw new SubscriptionBillingException(
                    SubscriptionBillingFailure.InvalidRequest,
                    $"The billing provider rejected the customer details: {detail}",
                    422);
            }

            if (ex.Error.TryGetRawError(out RawError raw))
            {
                throw MaxioFailureTranslation.FromRawError(_logger, raw, operation);
            }

            _logger.LogError(ex, "Maxio operation {Operation} failed with an error payload that could not be read.", operation);
            throw new SubscriptionBillingException(
                SubscriptionBillingFailure.InvalidRequest,
                "The billing provider rejected the customer details.",
                422);
        }
        catch (JsonException ex)
        {
            throw MaxioFailureTranslation.FromUnreadablePayload(_logger, ex, operation);
        }
        catch (Exception ex) when (MaxioFailureTranslation.IsTransportFailure(ex))
        {
            throw MaxioFailureTranslation.FromTransport(_logger, ex, operation, isWrite: true);
        }
    }

    private string? TryDescribeCustomerValidationError(CreateCustomerError error)
    {
        try
        {
            if (!error.TryGetCustomerErrorResponse1(out var typed) || typed.Errors is null)
            {
                return null;
            }

            var messages = new List<string>();
            if (typed.Errors.PerPage is not null)
            {
                messages.AddRange(typed.Errors.PerPage);
            }

            if (typed.Errors.PricePoint is not null)
            {
                messages.AddRange(typed.Errors.PricePoint);
            }

            var text = string.Join("; ", messages.Where(message => !string.IsNullOrWhiteSpace(message)));
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the typed validation payload from a Maxio customer error.");
            return null;
        }
    }

    private async Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct)
    {
        const string operation = "Customers.ListCustomerSubscriptions";

        try
        {
            var response = await _client.Customers.ListCustomerSubscriptions(customerId, ct);
            return response
                .Select(item => item.Subscription)
                .Where(subscription => subscription is not null)
                .Select(subscription => subscription!)
                .ToList();
        }
        catch (SdkException<RawError> ex) when (MaxioFailureTranslation.IsNotFound(ex.Error))
        {
            return Array.Empty<Subscription>();
        }
        catch (SdkException<RawError> ex)
        {
            throw MaxioFailureTranslation.FromRawError(_logger, ex.Error, operation);
        }
        catch (JsonException ex)
        {
            throw MaxioFailureTranslation.FromUnreadablePayload(_logger, ex, operation);
        }
        catch (Exception ex) when (MaxioFailureTranslation.IsTransportFailure(ex))
        {
            throw MaxioFailureTranslation.FromTransport(_logger, ex, operation, isWrite: false);
        }
    }

    private async Task<Subscription?> FindOccupyingSubscriptionAsync(int customerId, string planHandle, CancellationToken ct)
    {
        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, ct);

        return subscriptions.FirstOrDefault(subscription =>
            string.Equals(subscription.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase)
            && MaxioSubscriptionStates.OccupiesPlan(subscription.State));
    }

    private async Task<Subscription> CreateSubscriptionCoreAsync(
        int customerId,
        string planHandle,
        string subscriptionReference,
        CancellationToken ct)
    {
        const string operation = "Subscriptions.CreateSubscription";

        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = planHandle,
                CustomerId = customerId,
                Reference = subscriptionReference,

                // Without this the site's default (automatic) collection applies and Maxio tries to charge
                // the first period straight away - which fails for a shopper with no payment profile even
                // when the plan itself does not require a credit card. No card fields are sent: this
                // application captures none and runs no 3-D Secure flow.
                PaymentCollectionMethod = _paymentCollectionMethod,
                NetTerms = string.IsNullOrWhiteSpace(_options.NetTerms) ? null : _options.NetTerms!.Trim()
            }
        };

        using var writeOnce = new MaxioWriteOnceScope(operation);

        try
        {
            var response = await _client.Subscriptions.CreateSubscription(body, ct);
            if (response.Subscription is null)
            {
                throw new SubscriptionBillingException(
                    SubscriptionBillingFailure.ProviderResponseUnreadable,
                    "The billing provider accepted the subscription but did not return it.");
            }

            return response.Subscription;
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var typed))
            {
                var detail = string.Join("; ", typed.Errors.Where(message => !string.IsNullOrWhiteSpace(message)));
                _logger.LogWarning("Maxio operation {Operation} rejected the request: {Detail}", operation, detail);

                // These plans require no payment method, so a 3-D Secure action link should never appear
                // here. If one does it is not a validation message the caller can act on - this
                // integration captures no card - so report a generic failure instead.
                if (detail.Contains("action_link", StringComparison.OrdinalIgnoreCase))
                {
                    throw new SubscriptionBillingException(
                        SubscriptionBillingFailure.ProviderUnavailable,
                        "The selected plan requires payment authorization, which this application does not support.",
                        422);
                }

                throw new SubscriptionBillingException(
                    SubscriptionBillingFailure.InvalidRequest,
                    string.IsNullOrWhiteSpace(detail)
                        ? "The billing provider rejected the subscription request."
                        : $"The billing provider rejected the subscription request: {detail}",
                    422);
            }

            if (ex.Error.TryGetRawError(out RawError raw))
            {
                throw MaxioFailureTranslation.FromRawError(_logger, raw, operation);
            }

            _logger.LogError(ex, "Maxio operation {Operation} failed with an error payload that could not be read.", operation);
            throw new SubscriptionBillingException(
                SubscriptionBillingFailure.InvalidRequest,
                "The billing provider rejected the subscription request.",
                422);
        }
        catch (JsonException ex)
        {
            // On a create this is the dangerous direction: the write may have succeeded and only the
            // response was unreadable, so the caller must reconcile rather than assume failure.
            _logger.LogError(ex, "Maxio operation {Operation} returned a payload that could not be read.", operation);
            throw new SubscriptionBillingException(
                SubscriptionBillingFailure.OutcomeUnknown,
                "The billing provider could not confirm whether the subscription was created. Re-read your subscriptions before retrying.",
                providerStatusCode: null,
                ex);
        }
        catch (Exception ex) when (MaxioFailureTranslation.IsTransportFailure(ex))
        {
            throw MaxioFailureTranslation.FromTransport(_logger, ex, operation, isWrite: true);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Mapping
    // ---------------------------------------------------------------------------------------------

    private Domain.SubscriptionPlan MapPlan(Product product) => new()
    {
        Id = product.Id,
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? product.Handle ?? string.Empty,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit?.Value,
        PaymentMethodRequired = ResolvePaymentMethodRequired(product),
        CreatedAt = product.CreatedAt
    };

    /// <summary>
    /// The provider carries two similarly named flags. <c>require_credit_card</c> is the one that gates
    /// enrollment; when both are present and disagree, that is worth knowing about once.
    /// </summary>
    private bool? ResolvePaymentMethodRequired(Product product)
    {
        if (product.RequireCreditCard is not null
            && product.RequestCreditCard is not null
            && product.RequireCreditCard != product.RequestCreditCard
            && Interlocked.Exchange(ref _creditCardFlagDisagreementLogged, 1) == 0)
        {
            _logger.LogInformation(
                "Maxio product {Handle} reports require_credit_card={Require} but request_credit_card={Request}; using require_credit_card.",
                product.Handle,
                product.RequireCreditCard,
                product.RequestCreditCard);
        }

        return product.RequireCreditCard;
    }

    private static Domain.CustomerSubscription MapSubscription(Subscription subscription) => new()
    {
        Id = subscription.Id,
        Reference = subscription.Reference,
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,
        PriceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents,
        Currency = subscription.Currency,
        State = subscription.State?.Value,
        IsActive = MaxioSubscriptionStates.IsEntitling(subscription.State),
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,

        // The subscription payload carries no next_billing_at; the provider documents current_period_ends_at
        // as the field to read instead, with next_assessment_at as the secondary source.
        NextBillingAt = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        CreatedAt = subscription.CreatedAt,
        CustomerId = subscription.Customer?.Id,
        CustomerReference = subscription.Customer?.Reference
    };

    // ---------------------------------------------------------------------------------------------
    // Call budget
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Gives every operation the same total time budget. The SDK's own timeout bounds a single attempt,
    /// not a whole call, so without this a stalling provider could cost several multiples of it.
    /// </summary>
    private async Task<T> WithBudgetAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(_options.RequestBudgetSeconds));

        try
        {
            return await call(budget.Token);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(
                "A Maxio operation exceeded its {Budget}s budget.",
                _options.RequestBudgetSeconds);

            throw new SubscriptionBillingException(
                SubscriptionBillingFailure.ProviderUnavailable,
                "The billing provider did not respond in time. Please try again shortly.",
                providerStatusCode: null,
                ex);
        }
    }

    private sealed record CachedProductFamily(int Id, DateTimeOffset ExpiresAt);
}
