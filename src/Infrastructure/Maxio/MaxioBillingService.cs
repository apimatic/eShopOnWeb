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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maxio Advanced Billing implementation of <see cref="ISubscriptionBillingService"/>.
/// </summary>
/// <remarks>
/// This class is the integration boundary. Every Maxio call goes through <c>ExecuteAsync</c>, which
/// applies the one call budget and translates every provider failure — API errors, transport failures,
/// unreadable bodies, blocked retries — into <see cref="BillingException"/>. Nothing from the SDK
/// escapes past this type.
/// </remarks>
public sealed class MaxioBillingService : ISubscriptionBillingService
{
    /// <summary>
    /// States a subscription can be in and no longer count as an enrolment. Everything else is treated
    /// as live, including a state Maxio adds after this was written: an unknown wire value deserializes
    /// into a <c>SubscriptionState</c> that equals none of the static members, and it is safer to report
    /// such a subscription than to silently drop it and enrol the customer a second time.
    /// </summary>
    private static readonly SubscriptionState[] TerminalStates =
    {
        SubscriptionState.Canceled,
        SubscriptionState.Expired,
        SubscriptionState.FailedToCreate,
        SubscriptionState.TrialEnded
    };

    private const int PageSize = 100;
    private const int MaxPages = 50;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioBillingService> _logger;
    private readonly MaxioKeyedLock _subscriberLock = new();
    private readonly SemaphoreSlim _familyIdGate = new(1, 1);

    private int? _cachedFamilyId;
    private DateTimeOffset _cachedFamilyIdExpiresAt;

    public MaxioBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var familyId = await GetProductFamilyIdAsync(cancellationToken);
        var products = await ListProductsAsync(familyId, cancellationToken);

        return products
            .Select(ToPlan)
            .OrderBy(p => p.PriceInCents ?? long.MaxValue)
            .ToList();
    }

    public async Task<IReadOnlyList<PlanComponent>> GetPlanComponentsAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var familyId = await GetProductFamilyIdAsync(cancellationToken);

        return await ExecuteAsync(nameof(GetPlanComponentsAsync), async ct =>
        {
            var components = new List<PlanComponent>();

            for (var page = 1; page <= MaxPages; page++)
            {
                var response = await _client.Components.ListComponentsForProductFamily(
                    productFamilyId: familyId,
                    includeArchived: false,
                    filter: null,
                    dateField: null,
                    endDate: null,
                    endDatetime: null,
                    startDate: null,
                    startDatetime: null,
                    page: page,
                    perPage: PageSize,
                    ct: ct);

                components.AddRange(response.Select(item => ToComponent(item.Component)));

                if (response.Count < PageSize)
                {
                    break;
                }
            }

            return (IReadOnlyList<PlanComponent>)components;
        }, cancellationToken);
    }

    public async Task<SubscribeToPlanResult> SubscribeAsync(
        SubscriberIdentity subscriber,
        string planHandle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new BillingException(BillingFailureKind.Rejected, "A plan handle is required to subscribe.");
        }

        EnsureConfigured();

        planHandle = planHandle.Trim();

        // Confirm the plan is one this deployment actually sells before creating anything, so a typo
        // fails as a clean 404 instead of a provider rejection halfway through the flow. The projection
        // is reused to fill in plan details Maxio may omit from a subscription's nested product.
        var plans = await GetPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));

        if (plan is null)
        {
            throw new BillingException(
                BillingFailureKind.NotFound,
                $"No plan with handle '{planHandle}' is available in product family '{_settings.ProductFamilyHandle}'.");
        }

        var reference = CustomerReferenceFor(subscriber);

        // Serialize concurrent subscribe attempts for this subscriber, so a double click cannot have
        // both requests observe "nothing exists yet".
        using var subscriberLock = await _subscriberLock.AcquireAsync(reference, cancellationToken);

        var (customer, customerCreated) = await EnsureCustomerAsync(subscriber, reference, cancellationToken);

        var customerId = customer.Id
            ?? throw new BillingException(
                BillingFailureKind.InvalidResponse,
                "Maxio returned a customer without an id, so the subscription could not be created.");

        var existing = await FindLiveSubscriptionAsync(customerId, plan.Handle, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Maxio customer {CustomerId} is already subscribed to plan {PlanHandle} (subscription {SubscriptionId}); returning the existing subscription.",
                customerId, plan.Handle, existing.Id);

            return new SubscribeToPlanResult(ToSubscription(existing, plans), alreadySubscribed: true, customerCreated);
        }

        Subscription created;

        try
        {
            created = await CreateSubscriptionAsync(customerId, plan.Handle, cancellationToken);
        }
        catch (BillingException ex) when (ex.Kind is BillingFailureKind.UnknownOutcome or BillingFailureKind.Conflict)
        {
            // The write may or may not have landed. Settle it by re-reading rather than guessing, and
            // only report the failure if Maxio really has no subscription for this plan.
            _logger.LogWarning(
                ex,
                "Subscription create for Maxio customer {CustomerId} on plan {PlanHandle} had an unresolved outcome; re-reading to settle it.",
                customerId, plan.Handle);

            var reconciled = await FindLiveSubscriptionAsync(customerId, plan.Handle, cancellationToken);
            if (reconciled is null)
            {
                throw;
            }

            return new SubscribeToPlanResult(ToSubscription(reconciled, plans), alreadySubscribed: true, customerCreated);
        }

        _logger.LogInformation(
            "Created Maxio subscription {SubscriptionId} for customer {CustomerId} on plan {PlanHandle}.",
            created.Id, customerId, plan.Handle);

        return new SubscribeToPlanResult(ToSubscription(created, plans), alreadySubscribed: false, customerCreated);
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(
        SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        EnsureConfigured();

        var reference = CustomerReferenceFor(subscriber);
        var customer = await FindCustomerAsync(reference, cancellationToken);

        // No billing customer means the user has simply never subscribed. That is an empty list, not a
        // failure, and certainly not a 404 for the caller's own account.
        if (customer?.Id is not { } customerId)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, cancellationToken);

        // Maxio does not document whether the nested product is populated on this listing, and there is
        // no include parameter to ask for it. Fetch the plans once and backfill from them rather than
        // issuing a product read per row.
        IReadOnlyList<SubscriptionPlan> plans;
        try
        {
            plans = await GetPlansAsync(cancellationToken);
        }
        catch (BillingException ex)
        {
            _logger.LogWarning(ex, "Could not load plans to enrich the subscription list; returning what Maxio reported.");
            plans = Array.Empty<SubscriptionPlan>();
        }

        return subscriptions
            .Select(s => ToSubscription(s, plans))
            .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
            .ToList();
    }

    private string CustomerReferenceFor(SubscriberIdentity subscriber) =>
        MaxioCustomerReference.For(_settings.CustomerReferencePrefix, subscriber.UserName);

    private void EnsureConfigured()
    {
        if (_settings.IsConfigured)
        {
            return;
        }

        throw new BillingException(
            BillingFailureKind.NotConfigured,
            "Maxio billing is not configured. " + string.Join(" ", _settings.Validate()));
    }

    // ---------------------------------------------------------------------------------------------
    // Product family: Maxio exposes no operation that takes a family handle, so the configured handle
    // is resolved to the numeric id by listing families and matching client-side. The id is cached for
    // a bounded time rather than the process lifetime, because Maxio reassigns ids when a site is
    // re-seeded.
    // ---------------------------------------------------------------------------------------------

    private async Task<int> GetProductFamilyIdAsync(CancellationToken cancellationToken)
    {
        if (_cachedFamilyId is { } cached && DateTimeOffset.UtcNow < _cachedFamilyIdExpiresAt)
        {
            return cached;
        }

        await _familyIdGate.WaitAsync(cancellationToken);
        try
        {
            if (_cachedFamilyId is { } stillCached && DateTimeOffset.UtcNow < _cachedFamilyIdExpiresAt)
            {
                return stillCached;
            }

            var handle = _settings.ProductFamilyHandle!;
            var familyId = await ExecuteAsync(nameof(GetProductFamilyIdAsync), async ct =>
            {
                var families = await _client.ProductFamilies.ListProductFamilies(
                    dateField: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    ct: ct);

                var match = families
                    .Select(f => f.ProductFamily)
                    .FirstOrDefault(f => f is not null
                        && string.Equals(f.Handle, handle, StringComparison.OrdinalIgnoreCase));

                if (match?.Id is not { } id)
                {
                    throw new BillingException(
                        BillingFailureKind.NotFound,
                        $"No Maxio product family with handle '{handle}' was found on this site.");
                }

                return id;
            }, cancellationToken);

            _cachedFamilyId = familyId;
            _cachedFamilyIdExpiresAt = DateTimeOffset.UtcNow.AddSeconds(_settings.CatalogCacheSeconds);

            _logger.LogInformation(
                "Resolved Maxio product family '{Handle}' to id {FamilyId}.", handle, familyId);

            return familyId;
        }
        finally
        {
            _familyIdGate.Release();
        }
    }

    private void InvalidateProductFamilyId() => _cachedFamilyIdExpiresAt = DateTimeOffset.MinValue;

    // ---------------------------------------------------------------------------------------------
    // Maxio calls. Each owns the catch for its own typed error; everything shared lives in ExecuteAsync.
    // ---------------------------------------------------------------------------------------------

    private Task<IReadOnlyList<Product>> ListProductsAsync(int familyId, CancellationToken cancellationToken) =>
        ExecuteAsync(nameof(ListProductsAsync), async ct =>
        {
            var products = new List<Product>();

            for (var page = 1; page <= MaxPages; page++)
            {
                IReadOnlyList<ProductResponse> response;

                try
                {
                    response = await _client.ProductFamilies.ListProductsForProductFamily(
                        productFamilyId: familyId.ToString(CultureInfo.InvariantCulture),
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
                        ct: ct);
                }
                catch (SdkException<ListProductsForProductFamilyError> ex)
                {
                    throw TranslateListProducts(ex);
                }
                catch (JsonException ex)
                {
                    // This operation deserializes its 404 body as a bare JSON string, so a 404 whose body
                    // is anything else throws here instead of surfacing as an SdkException. The family id
                    // was resolved from a live listing moments ago, so treat it as the family having gone
                    // away — drop the cached id so the next call re-resolves it.
                    InvalidateProductFamilyId();

                    throw new BillingException(
                        BillingFailureKind.NotFound,
                        $"Maxio product family '{_settings.ProductFamilyHandle}' could not be read.",
                        innerException: ex);
                }

                products.AddRange(response.Select(item => item.Product));

                if (response.Count < PageSize)
                {
                    break;
                }
            }

            return (IReadOnlyList<Product>)products;
        }, cancellationToken);

    private BillingException TranslateListProducts(SdkException<ListProductsForProductFamilyError> ex)
    {
        if (ex.Error.TryGetString(out var body))
        {
            InvalidateProductFamilyId();

            _logger.LogWarning("Maxio reported the product family as missing: {Body}", body);

            return new BillingException(
                BillingFailureKind.NotFound,
                $"Maxio product family '{_settings.ProductFamilyHandle}' was not found.",
                HttpStatusCode.NotFound,
                innerException: ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return FromRawError(nameof(ListProductsAsync), raw, ex);
        }

        return new BillingException(
            BillingFailureKind.Unavailable,
            "Maxio could not list the plans for this product family.",
            innerException: ex);
    }

    private Task<Customer?> FindCustomerAsync(string reference, CancellationToken cancellationToken) =>
        ExecuteAsync(nameof(FindCustomerAsync), async ct =>
        {
            try
            {
                var response = await _client.Customers.ReadCustomerByReference(reference, ct);
                return (Customer?)response.Customer;
            }
            catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                // The one status that means "this user has no billing customer yet". Every other status
                // stays an error: falling through to "create" on a transient failure is how a 500 turns
                // into a duplicate customer.
                return null;
            }
        }, cancellationToken);

    private async Task<(Customer Customer, bool Created)> EnsureCustomerAsync(
        SubscriberIdentity subscriber,
        string reference,
        CancellationToken cancellationToken)
    {
        var existing = await FindCustomerAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return (existing, false);
        }

        try
        {
            var created = await CreateCustomerAsync(subscriber, reference, cancellationToken);

            _logger.LogInformation(
                "Created Maxio customer {CustomerId} for reference {Reference}.", created.Id, reference);

            return (created, true);
        }
        catch (BillingException ex) when (ex.Kind is BillingFailureKind.Rejected
                                              or BillingFailureKind.Conflict
                                              or BillingFailureKind.UnknownOutcome)
        {
            // Maxio allows at most one customer per reference, so a rejection here is most likely the
            // losing side of a race with a concurrent request (or a retry of a write that did land).
            // Re-read: if the customer now exists, the caller's intent was satisfied either way.
            var raced = await FindCustomerAsync(reference, cancellationToken);
            if (raced is null)
            {
                throw;
            }

            _logger.LogInformation(
                "Reused Maxio customer {CustomerId} for reference {Reference} after a create conflict.",
                raced.Id, reference);

            return (raced, false);
        }
    }

    private Task<Customer> CreateCustomerAsync(
        SubscriberIdentity subscriber,
        string reference,
        CancellationToken cancellationToken) =>
        ExecuteAsync(nameof(CreateCustomerAsync), async ct =>
        {
            var (firstName, lastName) = ResolveName(subscriber);

            var body = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = subscriber.Email,
                    Reference = reference
                }
            };

            using var writeScope = new MaxioWriteScope();

            try
            {
                var response = await _client.Customers.CreateCustomer(body, ct);
                return response.Customer;
            }
            catch (SdkException<CreateCustomerError> ex)
            {
                throw TranslateCreateCustomer(ex);
            }
            catch (JsonException ex)
            {
                // The generated 422 model for this operation cannot represent a customer validation body,
                // so a real rejection can surface as a parse failure with the status already lost. It is
                // still a rejection: reporting it as an outage would tell a caller to retry something
                // that can never succeed.
                throw new BillingException(
                    BillingFailureKind.Rejected,
                    "Maxio rejected the customer details and the reason could not be read.",
                    innerException: ex);
            }
        }, cancellationToken);

    private BillingException TranslateCreateCustomer(SdkException<CreateCustomerError> ex)
    {
        if (ex.Error.TryGetCustomerErrorResponse1(out var validation))
        {
            // 422. The status is implied by which accessor matched — TryGetRawError is false here, so it
            // is not readable. The generated payload can only express two keys, neither of which is a
            // customer field, so treat any messages as a bonus and never assume a non-empty list.
            var messages = new List<string>();
            if (validation.Errors?.PerPage is { } perPage) messages.AddRange(perPage);
            if (validation.Errors?.PricePoint is { } pricePoint) messages.AddRange(pricePoint);

            _logger.LogWarning("Maxio rejected the customer create with {MessageCount} validation message(s).", messages.Count);

            return new BillingException(
                BillingFailureKind.Rejected,
                "Maxio rejected the customer details.",
                HttpStatusCode.UnprocessableEntity,
                messages,
                ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return FromRawError(nameof(CreateCustomerAsync), raw, ex);
        }

        return new BillingException(
            BillingFailureKind.Unavailable,
            "Maxio could not create the billing customer.",
            innerException: ex);
    }

    private Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken) =>
        ExecuteAsync(nameof(ListCustomerSubscriptionsAsync), async ct =>
        {
            // This is the only customer-scoped subscription listing Maxio offers; it has neither a state
            // filter nor pagination, so everything is filtered client-side.
            var response = await _client.Customers.ListCustomerSubscriptions(customerId, ct);

            return (IReadOnlyList<Subscription>)response
                .Select(item => item.Subscription)
                .Where(s => s is not null)
                .Select(s => s!)
                .ToList();
        }, cancellationToken);

    private async Task<Subscription?> FindLiveSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, cancellationToken);

        return subscriptions.FirstOrDefault(s =>
            IsLive(s.State)
            && string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
    }

    private Task<Subscription> CreateSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken) =>
        ExecuteAsync(nameof(CreateSubscriptionAsync), async ct =>
        {
            // Nothing about payment is sent: the plans this integration targets do not require a payment
            // method, and sending customer attributes alongside an existing customer id would fight the
            // idempotency the flow depends on.
            var subscription = new CreateSubscription
            {
                ProductHandle = planHandle,
                CustomerId = customerId
            };

            if (!string.IsNullOrWhiteSpace(_settings.PaymentCollectionMethod))
            {
                // Without this Maxio tries to charge the first period at signup and rejects the
                // enrolment outright — "No payment method was on file for the $299.00 balance" — even
                // though the plan does not require a credit card. Invoicing the customer instead is what
                // lets a shopper subscribe with no card capture and no 3-DS.
                subscription = subscription with
                {
                    PaymentCollectionMethod = CollectionMethod.FromValue(_settings.PaymentCollectionMethod!)
                };
            }

            var body = new CreateSubscriptionRequest { Subscription = subscription };

            using var writeScope = new MaxioWriteScope();

            try
            {
                var response = await _client.Subscriptions.CreateSubscription(body, ct);

                return response.Subscription
                    ?? throw new BillingException(
                        BillingFailureKind.InvalidResponse,
                        "Maxio accepted the subscription but returned no subscription details.");
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                throw TranslateCreateSubscription(ex);
            }
        }, cancellationToken);

    private BillingException TranslateCreateSubscription(SdkException<CreateSubscriptionError> ex)
    {
        if (ex.Error.TryGetErrorListResponse1(out var validation))
        {
            // 422. Surfaced verbatim: this is where a "payment method required" rejection would appear,
            // and collapsing it into a generic message would make that undiagnosable.
            var messages = validation.Errors.ToList();

            _logger.LogWarning(
                "Maxio rejected the subscription create: {Messages}", string.Join("; ", messages));

            return new BillingException(
                BillingFailureKind.Rejected,
                "Maxio rejected the subscription request.",
                HttpStatusCode.UnprocessableEntity,
                messages,
                ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return FromRawError(nameof(CreateSubscriptionAsync), raw, ex);
        }

        return new BillingException(
            BillingFailureKind.Unavailable,
            "Maxio could not create the subscription.",
            innerException: ex);
    }

    // ---------------------------------------------------------------------------------------------
    // The shared boundary: one call budget, one translation of every failure the SDK can produce.
    // ---------------------------------------------------------------------------------------------

    private async Task<T> ExecuteAsync<T>(string operation, Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        // The budget lives here rather than at each call site, so an operation added later cannot
        // silently end up with no ceiling.
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(_settings.CallBudgetSeconds));

        try
        {
            return await call(budget.Token);
        }
        catch (BillingException)
        {
            // Already translated by the operation that owns its typed error.
            throw;
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRawError(operation, ex.Error, ex);
        }
        catch (MaxioDuplicateSendBlockedException ex)
        {
            // A retry of a write was refused. Exactly one request left this process, but it may still
            // have been received, so the outcome is unknown rather than failed.
            _logger.LogWarning(ex, "Blocked a duplicate Maxio write during {Operation}.", operation);

            throw new BillingException(
                BillingFailureKind.UnknownOutcome,
                "The billing request could not be confirmed. It may already have taken effect.",
                innerException: ex);
        }
        catch (JsonException ex)
        {
            // A success body the SDK could not deserialize. The outcome really is unknown here, unlike
            // the operation-specific error-body cases handled above.
            _logger.LogError(ex, "Maxio returned a response during {Operation} that could not be processed.", operation);

            throw new BillingException(
                BillingFailureKind.InvalidResponse,
                "The billing system returned a response that could not be processed.",
                innerException: ex);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller went away; this is not a billing failure.
            throw;
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "Maxio call {Operation} exceeded the {Budget}s budget.", operation, _settings.CallBudgetSeconds);

            throw new BillingException(
                BillingFailureKind.Unavailable,
                "The billing system did not respond in time.",
                innerException: ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Maxio was unreachable during {Operation}.", operation);

            throw new BillingException(
                BillingFailureKind.Unavailable,
                "The billing system could not be reached.",
                innerException: ex);
        }
    }

    private BillingException FromRawError(string operation, RawError raw, Exception inner)
    {
        var status = raw.StatusCode;
        var body = ReadBodySafely(raw);

        _logger.LogWarning(
            "Maxio call {Operation} failed with HTTP {StatusCode}: {Body}", operation, (int)status, body);

        var kind = (int)status switch
        {
            (int)HttpStatusCode.Unauthorized or (int)HttpStatusCode.Forbidden => BillingFailureKind.Unauthenticated,
            (int)HttpStatusCode.NotFound => BillingFailureKind.NotFound,
            (int)HttpStatusCode.Conflict => BillingFailureKind.Conflict,
            (int)HttpStatusCode.TooManyRequests => BillingFailureKind.Unavailable,
            >= 400 and < 500 => BillingFailureKind.Rejected,
            _ => BillingFailureKind.Unavailable
        };

        var message = kind switch
        {
            BillingFailureKind.Unauthenticated => "The billing system rejected this application's credentials.",
            BillingFailureKind.NotFound => "The billing system has no record matching this request.",
            BillingFailureKind.Conflict => "The billing system reported a conflicting record.",
            BillingFailureKind.Rejected => "The billing system rejected this request.",
            _ => "The billing system is currently unavailable."
        };

        // The raw body is logged but not returned: it is arbitrary provider output (often HTML) rather
        // than the caller-safe validation messages a typed error carries.
        return new BillingException(kind, message, status, providerMessages: null, innerException: inner);
    }

    private string ReadBodySafely(RawError raw)
    {
        try
        {
            var body = raw.ReadAsString();
            return body.Length <= 1024 ? body : body[..1024] + "…";
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Maxio error body could not be read as text.");
            return "<unreadable>";
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Projections.
    // ---------------------------------------------------------------------------------------------

    private static bool IsLive(SubscriptionState? state) =>
        state is null || !TerminalStates.Contains(state);

    private static SubscriptionPlan ToPlan(Product product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit?.Value,
        TrialPriceInCents = product.TrialPriceInCents,
        TrialInterval = product.TrialInterval,
        TrialIntervalUnit = product.TrialIntervalUnit?.Value,
        SetupFeeInCents = product.InitialChargeInCents,
        RequiresCreditCard = product.RequireCreditCard ?? false,
        Taxable = product.Taxable,
        ProductFamilyHandle = product.ProductFamily?.Handle
    };

    private static PlanComponent ToComponent(Component component) => new()
    {
        Handle = component.Handle,
        Name = component.Name,
        Kind = component.Kind?.Value,
        UnitName = component.UnitName,
        PricePerUnitInCents = component.PricePerUnitInCents,
        UnitPrice = component.UnitPrice,
        PricingScheme = component.PricingScheme?.Value,
        Recurring = component.Recurring
    };

    private static CustomerSubscription ToSubscription(Subscription subscription, IReadOnlyList<SubscriptionPlan> plans)
    {
        var handle = subscription.Product?.Handle;

        // Maxio may omit the nested product on a listing and there is no include parameter to ask for
        // it, so fall back to the plans already loaded rather than reading each product individually.
        var plan = handle is null
            ? null
            : plans.FirstOrDefault(p => string.Equals(p.Handle, handle, StringComparison.OrdinalIgnoreCase));

        return new CustomerSubscription
        {
            Id = subscription.Id ?? 0,
            PlanHandle = handle ?? plan?.Handle,
            PlanName = subscription.Product?.Name ?? plan?.Name,
            State = subscription.State?.Value,
            IsLive = IsLive(subscription.State),
            // Read back from the response rather than echoing what was requested, so the reported
            // collection method is the one Maxio actually applied.
            PaymentCollectionMethod = subscription.PaymentCollectionMethod?.Value,
            PriceInCents = subscription.ProductPriceInCents
                ?? subscription.Product?.PriceInCents
                ?? plan?.PriceInCents,
            Interval = subscription.Product?.Interval ?? plan?.Interval,
            IntervalUnit = subscription.Product?.IntervalUnit?.Value ?? plan?.IntervalUnit,
            CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
            NextBillingAt = subscription.CurrentPeriodEndsAt,
            NextAssessmentAt = subscription.NextAssessmentAt,
            CreatedAt = subscription.CreatedAt,
            ActivatedAt = subscription.ActivatedAt,
            CanceledAt = subscription.CanceledAt
        };
    }

    private static (string FirstName, string LastName) ResolveName(SubscriberIdentity subscriber)
    {
        var first = subscriber.FirstName?.Trim();
        var last = subscriber.LastName?.Trim();

        if (!string.IsNullOrEmpty(first) && !string.IsNullOrEmpty(last))
        {
            return (first, last);
        }

        // Maxio requires both names. eShopOnWeb identities carry neither, so derive something stable and
        // recognisable from the email local part when the caller does not supply them.
        var localPart = subscriber.Email.Split('@')[0];
        var parts = localPart.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);

        var derivedFirst = Titleize(parts.FirstOrDefault() ?? subscriber.UserName);
        var derivedLast = parts.Length > 1 ? Titleize(parts[^1]) : "eShopOnWeb";

        return (string.IsNullOrEmpty(first) ? derivedFirst : first,
                string.IsNullOrEmpty(last) ? derivedLast : last);
    }

    private static string Titleize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Customer";
        }

        return value.Length == 1
            ? value.ToUpperInvariant()
            : char.ToUpperInvariant(value[0]) + value[1..];
    }
}
